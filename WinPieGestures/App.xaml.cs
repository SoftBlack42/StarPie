using System;
using System.CodeDom.Compiler;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace WinPieGestures;

public partial class App : Application
{
	private static Mutex? _singleInstanceMutex;
	private static EventWaitHandle? _instanceWakeEvent;
	private static RegisteredWaitHandle? _waitHandleRegistration;
	private static bool _isDuplicateInstance;
	private static bool _isExiting;
	private static bool _startupCompleted;
	private static bool _pendingSettingsRequest;
	private static int _pendingSettingsTabIndex = -1;

	private const string MutexName = "Global\\StarPie_SingleInstance_Mutex_9B8A7C";
	private const string WakeEventName = "Global\\StarPie_Wakeup_Event_9B8A7C";
	private const string AppId = "SoftBlack42.StarPie.App";

	public static GestureController? MainGestureController { get; private set; }
	public static MouseHook? MainMouseHook { get; private set; }
	public static KeyboardHook? MainKeyboardHook { get; private set; }
	public static SettingsWindow? MainSettingsWindow { get; private set; }
	public static TrayController? MainTrayController { get; private set; }
	public static bool IsExiting => _isExiting;

	[DllImport("shell32.dll", SetLastError = true)]
	private static extern void SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetForegroundWindow(nint hWnd);

	[StructLayout(LayoutKind.Sequential)]
	private struct PROCESS_POWER_THROTTLING_STATE
	{
		public uint Version;
		public uint ControlMask;
		public uint StateMask;
	}

	private const int ProcessPowerThrottling = 4;
	private const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
	private const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool SetProcessInformation(
		nint hProcess,
		int processInformationClass,
		ref PROCESS_POWER_THROTTLING_STATE processInformation,
		uint processInformationSize);

	private static void DisablePowerThrottling()
	{
		try
		{
			if (Environment.OSVersion.Platform == PlatformID.Win32NT)
			{
				PROCESS_POWER_THROTTLING_STATE state = new PROCESS_POWER_THROTTLING_STATE
				{
					Version = PROCESS_POWER_THROTTLING_CURRENT_VERSION,
					ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
					StateMask = 0
				};
				SetProcessInformation(
					Process.GetCurrentProcess().Handle,
					ProcessPowerThrottling,
					ref state,
					(uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
				Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.Normal;
			}
		}
		catch
		{
		}
	}

	protected override void OnStartup(StartupEventArgs e)
	{
		DisablePowerThrottling();
		try
		{
			SetCurrentProcessExplicitAppUserModelID(AppId);
		}
		catch
		{
		}
		string commandLine = Environment.CommandLine;
		if (!commandLine.Contains("--allow-multiple", StringComparison.OrdinalIgnoreCase) && !commandLine.Contains("--test-instance", StringComparison.OrdinalIgnoreCase))
		{
			bool createdNew;
			try
			{
				_singleInstanceMutex = new Mutex(initiallyOwned: true, MutexName, out createdNew);
			}
			catch
			{
				createdNew = true;
			}
			if (!createdNew)
			{
				try
				{
					using EventWaitHandle eventWaitHandle = EventWaitHandle.OpenExisting(WakeEventName);
					eventWaitHandle.Set();
				}
				catch
				{
				}
				_isDuplicateInstance = true;
				Shutdown(0);
				return;
			}
			try
			{
				_instanceWakeEvent = new EventWaitHandle(initialState: false, EventResetMode.AutoReset, WakeEventName);
				_waitHandleRegistration = ThreadPool.RegisterWaitForSingleObject(_instanceWakeEvent, delegate
				{
					((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
					{
						WakeUpSettingsWindow();
					}, Array.Empty<object>());
				}, null, -1, executeOnlyOnce: false);
			}
			catch
			{
			}
		}
		base.OnStartup(e);
		AppLogger.LogInfo($"=== StarPie {AppVersionInfo.DisplayVersionWithPrefix} Starting (OS: {Environment.OSVersion}, .NET: {Environment.Version}, 64bit: {Environment.Is64BitProcess}, Elevated: {ConfigManager.IsElevated()}) ===");
		base.DispatcherUnhandledException += new DispatcherUnhandledExceptionEventHandler(App_DispatcherUnhandledException);
		AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;
		try
		{
			ConfigManager.LoadConfig();
			AppLogger.LogInfo("ConfigManager.LoadConfig completed");
			MainMouseHook = new MouseHook();
			MainMouseHook.Start();
			AppLogger.LogInfo("MainMouseHook started");
			MainKeyboardHook = new KeyboardHook();
			MainKeyboardHook.Start();
			AppLogger.LogInfo("MainKeyboardHook started");
			MainGestureController = new GestureController(MainMouseHook, MainKeyboardHook);
			MainTrayController = new TrayController();
			MainTrayController.OpenSettingsRequested += ShowSettingsWindow;
			MainTrayController.TogglePauseRequested += TogglePauseGestures;
			MainTrayController.ElevateRequested += RestartElevated;
			MainTrayController.ExitRequested += ExitApplication;
			MainTrayController.Initialize(MainMouseHook.IsPaused, IsCurrentThemeDark());
			AppLogger.LogInfo("TrayController initialized");
			_startupCompleted = true;
			if (!SettingsWindow.IsSilentLaunch() || _pendingSettingsRequest)
			{
				int requestedTab = _pendingSettingsRequest ? _pendingSettingsTabIndex : -1;
				_pendingSettingsRequest = false;
				_pendingSettingsTabIndex = -1;
				ShowSettingsWindow(requestedTab);
			}
			else
			{
				AppLogger.LogInfo("Silent launch: SettingsWindow creation deferred until first use");
				// 静默启动或开机自启时，在挂载完轻量级钩子后等待后台就绪（1.5秒后）执行一次工作集规整，将静默占用压至极限
				_ = System.Threading.Tasks.Task.Run(async () =>
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(1500).ConfigureAwait(false);
						MemoryOptimizer.TrimMemory(force: true);
					}
					catch
					{
					}
				});
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("StarPie initialization failed", ex);
			MessageBox.Show("初始化 StarPie 失败:\n" + ex.Message, "启动错误", MessageBoxButton.OK, MessageBoxImage.Hand);
			Shutdown();
		}
	}

	public static void WakeUpSettingsWindow()
	{
		ShowSettingsWindow();
	}

	public static void ShowSettingsWindow(int tabIndex = -1)
	{
		if (_isExiting || Application.Current == null)
		{
			return;
		}
		if (!Application.Current.Dispatcher.CheckAccess())
		{
			Application.Current.Dispatcher.BeginInvoke((Action)(() => ShowSettingsWindow(tabIndex)));
			return;
		}
		if (!_startupCompleted)
		{
			_pendingSettingsRequest = true;
			if (tabIndex >= 0)
			{
				_pendingSettingsTabIndex = tabIndex;
			}
			return;
		}

		if (MainSettingsWindow == null)
		{
			SettingsWindow window = new SettingsWindow();
			window.Closed += SettingsWindow_Closed;
			MainSettingsWindow = window;
			Application.Current.MainWindow = window;
			AppLogger.LogInfo("SettingsWindow created on demand");
		}

		MainSettingsWindow.ShowSettings(tabIndex);
		try
		{
			nint handle = new WindowInteropHelper(MainSettingsWindow).Handle;
			if (handle != IntPtr.Zero)
			{
				SetForegroundWindow(handle);
			}
		}
		catch
		{
		}
	}

	private static void SettingsWindow_Closed(object? sender, EventArgs e)
	{
		if (sender is not SettingsWindow closedWindow)
		{
			return;
		}
		closedWindow.Closed -= SettingsWindow_Closed;
		if (ReferenceEquals(MainSettingsWindow, closedWindow))
		{
			MainSettingsWindow = null;
		}
		if (Application.Current != null && ReferenceEquals(Application.Current.MainWindow, closedWindow))
		{
			Application.Current.MainWindow = null;
		}
		AppLogger.LogInfo("SettingsWindow closed and released");
		if (!_isExiting && Application.Current != null)
		{
			Application.Current.Dispatcher.BeginInvoke(
				(Action)(() => MemoryOptimizer.TrimMemory(force: true)),
				DispatcherPriority.ApplicationIdle);
		}
	}

	public static void RefreshTrayMenu()
	{
		MainTrayController?.RefreshMenu();
	}

	public static void ApplyTrayTheme(bool isDark)
	{
		MainTrayController?.ApplyTheme(isDark);
	}

	public static void ShowTrayBalloon(int timeout, string title, string message, System.Windows.Forms.ToolTipIcon icon)
	{
		MainTrayController?.ShowBalloonTip(timeout, title, message, icon);
	}

	private static bool IsCurrentThemeDark()
	{
		string theme = ConfigManager.CurrentConfig?.AppTheme ?? "System";
		if (string.Equals(theme, "System", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(theme))
		{
			return AppThemeManager.IsWindowsInDarkTheme();
		}
		return !string.Equals(theme, "Light", StringComparison.OrdinalIgnoreCase);
	}

	private static void TogglePauseGestures()
	{
		if (MainMouseHook == null)
		{
			return;
		}
		MainMouseHook.IsPaused = !MainMouseHook.IsPaused;
		MainTrayController?.UpdatePauseState(MainMouseHook.IsPaused);
	}

	public static void RestartElevated()
	{
		try
		{
			string fileName = Environment.ProcessPath ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "StarPie.exe");
			Process.Start(new ProcessStartInfo
			{
				FileName = fileName,
				Arguments = "--silent",
				UseShellExecute = true,
				Verb = "runas"
			});
			ExitApplication();
		}
		catch (Exception ex)
		{
			MessageBox.Show("提权重启失败或已取消: " + ex.Message, "管理员提权", MessageBoxButton.OK, MessageBoxImage.Exclamation);
		}
	}

	public static void ExitApplication()
	{
		if (_isExiting)
		{
			return;
		}
		_isExiting = true;
		MainTrayController?.Dispose();
		Application.Current?.Shutdown();
	}

	private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
	{
		try
		{
			AppLogger.LogError("WPF Dispatcher Unhandled Exception", e.Exception);
			e.Handled = true;
		}
		catch
		{
		}
	}

	private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
	{
		try
		{
			if (e.ExceptionObject is Exception ex)
			{
				AppLogger.LogError("AppDomain Unhandled Exception", ex);
			}
			else
			{
				AppLogger.LogError($"AppDomain Unhandled Exception Object: {e.ExceptionObject}");
			}
		}
		catch
		{
		}
	}

	protected override void OnExit(ExitEventArgs e)
	{
		if (_isDuplicateInstance)
		{
			base.OnExit(e);
			return;
		}
		AppLogger.LogInfo("=== StarPie Exiting ===");
		_isExiting = true;
		MainTrayController?.Dispose();
		MainTrayController = null;
		// 退出前自动还原所有窗口到首次平铺前的样式
		try
		{
			WindowTiler.RestoreLastLayout();
		}
		catch
		{
		}
		try
		{
			_waitHandleRegistration?.Unregister(null);
			_instanceWakeEvent?.Dispose();
			_singleInstanceMutex?.ReleaseMutex();
			_singleInstanceMutex?.Dispose();
		}
		catch
		{
		}
		try
		{
			// 本次启动若因配置损坏而回落默认，绝不能在退出时把默认值写回磁盘，
			// 否则用户尚可从 .corrupt 备份恢复的配置会被永久覆盖。
			if (ConfigManager.IsFallbackConfig)
			{
				AppLogger.LogInfo("Skipped exit-time config save: loaded config was a fallback default");
			}
			else
			{
				ConfigManager.SaveConfig();
			}
		}
		catch
		{
		}
		try
		{
			MainGestureController?.Dispose();
			MainGestureController = null;
			MainMouseHook?.Stop();
			MainKeyboardHook?.Stop();
		}
		catch
		{
		}
		AppLogger.Shutdown();
		base.OnExit(e);
	}
}
