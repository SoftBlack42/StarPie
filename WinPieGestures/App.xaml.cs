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

	private const int AttachParentProcess = -1;

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool AttachConsole(int dwProcessId);

	/// <summary>
	/// 让命令行通道的输出真正落到调用方的终端上。
	/// <para>
	/// StarPie 是 WinExe（没有控制台子系统），<c>Console.WriteLine</c> 默认无处可去。
	/// 于是 <c>--plugin-paths</c> / <c>--plugin-selftest</c> 这类「一条命令拿到结论」的通道，
	/// 实际只能去翻日志文件 —— 名不副实。这里把进程附到父进程的控制台并重新接上
	/// stdout / stderr，输出才看得见。
	/// </para>
	/// <para>
	/// <b>只在命令行里出现这些开关时才调用</b>：普通 GUI 启动完全不碰控制台，行为不变。
	/// </para>
	/// </summary>
	private static void AttachParentConsoleIfCli()
	{
		string commandLine = Environment.CommandLine;
		bool isCliChannel =
			commandLine.Contains("--plugin-paths", StringComparison.OrdinalIgnoreCase) ||
			commandLine.Contains("--plugin-selftest", StringComparison.OrdinalIgnoreCase);
		if (!isCliChannel)
		{
			return;
		}

		try
		{
			// 父进程没有控制台（比如从资源管理器或计划任务启动）时返回 false —— 这不是错误，
			// 此时报告仍然照常落盘，只是终端上看不到而已。
			if (!AttachConsole(AttachParentProcess))
			{
				return;
			}

			Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
			Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
		}
		catch (Exception ex)
		{
			// 接不上控制台不影响任何功能：报告与日志都在。但要留下痕迹，
			// 免得下次又出现「命令跑了、什么也没看见」却查不出原因的情况。
			AppLogger.LogInfo($"未能接入父控制台，本次仅在日志中输出：{ex.Message}");
		}
	}

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
		if (!commandLine.Contains("--allow-multiple", StringComparison.OrdinalIgnoreCase) && !commandLine.Contains("--test-instance", StringComparison.OrdinalIgnoreCase) && !commandLine.Contains("--plugin-selftest", StringComparison.OrdinalIgnoreCase))
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
				bool hasHandle = false;
				try
				{
					hasHandle = _singleInstanceMutex.WaitOne(150, false);
				}
				catch (AbandonedMutexException)
				{
					// 前一进程非正常终止并遗弃了互斥体，当前实例顺利接管所有权
					hasHandle = true;
				}
				catch
				{
				}

				if (!hasHandle)
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

			// 命令行通道（自检 / 路径诊断）要能在调用方的终端上直接看到输出。
			// 放在最前：晚一步接上，前面打的内容就丢了。
			AttachParentConsoleIfCli();

			// 插件系统自检模式：不启动钩子与托盘，跑完全链路直接退出。
			// 用途：① CI 里做无界面回归；② 用户报「插件装不上」时一条命令拿到全链路证据。
			if (TryRunPluginSelfTest()) return;

			// 插件路径诊断：只解析路径与登记，不启动钩子与托盘。
			if (TryRunPluginPathsReport()) return;

			if (ConfigManager.CurrentConfig?.EnableSoundEffects == true)
			{
				SoundEffectManager.Initialize();
				AppLogger.LogInfo("SoundEffectManager.Initialize completed");
			}
			else
			{
				AppLogger.LogInfo("SoundEffectManager initialization deferred because sound effects are disabled");
			}
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

			// ---- 插件系统 ----
			// 通知下沉到托盘气泡：插件的失败提示必须是「可忽略的」，绝不能用 MessageBox 打断用户。
			Plugins.PluginHost.NotificationSink = (title, message) =>
			{
				try
				{
					MainTrayController?.ShowBalloonTip(5000, title, message, System.Windows.Forms.ToolTipIcon.Info);
				}
				catch
				{
				}
			};

			// 初始化延迟到 UI 空闲时执行。Initialize 只做「纯文件读」的静态扫描（不加载任何程序集），
			// 但放在这里会挤占首帧；而插件系统早一秒晚一秒就绪对用户完全没有感知。
			Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle, new Action(() =>
			{
				try
				{
					Plugins.PluginHost.Initialize();
				}
				catch (Exception ex)
				{
					// 插件系统初始化失败绝不允许影响主程序启动
					AppLogger.LogError("Plugin system initialization failed", ex);
				}
			}));

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

	/// <summary>
	/// 处理 <c>--plugin-paths</c>：解析插件路径、跑一次目录搬迁与登记读取，
	/// 把结论<b>打印到终端</b>（同时写进日志）后退出。
	/// <para>
	/// 存在的意义只有一个：用户报「插件目录不对 / 插件不见了」时，不必让他翻设置界面截图 ——
	/// 一条命令就能拿到「可写宿主区在哪、只读扫描目录在哪、便携标志在不在、登记了几个插件」。
	/// 它同样会触发 <c>plugins\ → plugin-data\</c> 的一次性搬迁，而那恰恰是这类问题的第一嫌疑。
	/// </para>
	/// <para>
	/// 输出同时走终端与日志两个出口：终端是给人当场看的，日志是给事后回溯用的
	/// （用户往往只把命令输出贴进 issue，日志留在自己机器上）。
	/// </para>
	/// </summary>
	private bool TryRunPluginPathsReport()
	{
		if (!Environment.CommandLine.Contains("--plugin-paths", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		try
		{
			// 诊断模式是无界面短命进程，不该被计入启动健康统计（否则连跑两次就可能
			// 触发插件安全模式，把用户正常用着的插件自动禁用掉）。
			Plugins.PluginHost.HeadlessMode = true;
			Plugins.PluginHost.Initialize();

			string[] lines =
			{
				"=== 插件路径诊断 ===",
				$"  可写宿主区　：{Plugins.PluginPaths.Root}" +
					$"（存在={System.IO.Directory.Exists(Plugins.PluginPaths.Root)}，便携模式={Plugins.PluginPaths.IsPortable}）",
				$"  只读扫描目录：{Plugins.PluginPaths.ScanRoot}" +
					$"（存在={Plugins.PluginPaths.ScanRootExists}）",
				$"  便携标志文件：{Plugins.PluginPaths.PortableFlagPresent}",
				$"  已登记插件　：{Plugins.PluginHost.InstalledCount} 个",
				$"  扫描候选　　：{Plugins.PluginHost.ScanCandidates()} 个",
				"=== 插件路径诊断结束 ===",
			};

			foreach (string line in lines)
			{
				AppLogger.LogInfo(line);
				Console.WriteLine(line);
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError("插件路径诊断失败", ex);
			Console.WriteLine($"[FAIL] 插件路径诊断失败：{ex.Message}");
		}

		Shutdown(0);
		return true;
	}

	/// <summary>
	/// 处理 <c>--plugin-selftest &lt;dll&gt; [report] [--skip-invoke]</c>。
	/// 返回 true 表示已接管本次启动（调用方应直接 return，不要继续装配钩子与托盘）。
	/// 附加 <c>--skip-invoke</c> 时跳过最后的真实调用段，自检不再改变本机环境（亮度/音量等）。
	/// </summary>
	private bool TryRunPluginSelfTest()
	{
		string commandLine = Environment.CommandLine;
		if (!commandLine.Contains("--plugin-selftest", StringComparison.OrdinalIgnoreCase))
		{
			return false;
		}

		string dllPath = "";
		string reportPath = "";

		// --skip-invoke 是个开关而非位置参数，故不参与上面的位置解析，直接整串探测。
		// 目的：自检的 [4] 段会真实下发键鼠/调节系统状态（实测会把屏幕亮度推高），
		// 日常回归只要识别、注册与选择器接缝结论，不该动用户的机器。
		bool skipInvoke = commandLine.Contains("--skip-invoke", StringComparison.OrdinalIgnoreCase);

		string[] arguments = Environment.GetCommandLineArgs();
		for (int i = 0; i < arguments.Length; i++)
		{
			if (!string.Equals(arguments[i], "--plugin-selftest", StringComparison.OrdinalIgnoreCase))
			{
				continue;
			}

			if (i + 1 < arguments.Length && !arguments[i + 1].StartsWith("--", StringComparison.Ordinal))
			{
				dllPath = arguments[i + 1];
			}
			if (i + 2 < arguments.Length && !arguments[i + 2].StartsWith("--", StringComparison.Ordinal))
			{
				reportPath = arguments[i + 2];
			}
			break;
		}

		int exitCode = 1;
		try
		{
			exitCode = Plugins.PluginSelfTest.Run(dllPath, reportPath, skipInvoke);
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Plugin self-test crashed", ex);
		}

		AppLogger.LogInfo($"=== Plugin self-test finished with exit code {exitCode} ===");
		Shutdown(exitCode);
		return true;
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

		// 插件系统收尾：停用全部插件（撤销贡献点 + 剪断事件订阅 + 尽力卸载 ALC），并把健康度落盘。
		// 必须在托盘与设置窗口释放之前做 —— 插件停用过程可能产生需要用托盘显示的提示。
		try
		{
			Plugins.PluginHost.ShutdownAll();
		}
		catch (Exception ex)
		{
			AppLogger.LogError("Plugin system shutdown failed", ex);
		}

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
			SoundEffectManager.Shutdown();
		}
		catch
		{
		}
		AppLogger.Shutdown();
		base.OnExit(e);
	}
}
