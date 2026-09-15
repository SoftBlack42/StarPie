using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Windows;

namespace WinPieGestures;

public static class ActionExecutor
{
	private delegate bool EnumWindowsProc(nint hWnd, nint lParam);

	private struct MOUSEINPUT
	{
		public int dx;

		public int dy;

		public uint mouseData;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	private struct KEYBDINPUT
	{
		public ushort wVk;

		public ushort wScan;

		public uint dwFlags;

		public uint time;

		public nint dwExtraInfo;
	}

	private struct HARDWAREINPUT
	{
		public uint uMsg;

		public ushort wParamL;

		public ushort wParamH;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct InputUnion
	{
		[FieldOffset(0)]
		public MOUSEINPUT mi;

		[FieldOffset(0)]
		public KEYBDINPUT ki;

		[FieldOffset(0)]
		public HARDWAREINPUT hi;
	}

	private struct INPUT
	{
		public uint type;

		public InputUnion U;
	}

	public class HotkeyStep
	{
		public List<ushort> Modifiers { get; } = new List<ushort>();

		public ushort MainKey { get; set; }

		public string RawToken { get; set; } = string.Empty;

		public override string ToString()
		{
			List<string> parts = new List<string>();
			foreach (ushort mod in Modifiers)
			{
				parts.Add(mod switch
				{
					162 => "Ctrl",
					160 => "Shift",
					164 => "Alt",
					91 => "Win",
					_ => $"Mod({mod})"
				});
			}
			if (MainKey != 0)
			{
				parts.Add($"VK({MainKey})");
			}
			return string.Join("+", parts);
		}
	}

	public class HotkeyDetails
	{
		public List<ushort> Modifiers { get; } = new List<ushort>();

		public List<ushort> SequenceKeys { get; } = new List<ushort>();

		public ushort MainKey { get; set; }

		public List<HotkeyStep> Steps { get; } = new List<HotkeyStep>();
	}

	private const int SW_HIDE = 0;

	private const int SW_SHOWNORMAL = 1;

	private const int SW_SHOWMINIMIZED = 2;

	private const int SW_SHOWMAXIMIZED = 3;

	private const int SW_SHOW = 5;

	private const int SW_MINIMIZE = 6;

	private const int SW_RESTORE = 9;

	private const uint INPUT_KEYBOARD = 1u;

	private const uint KEYEVENTF_KEYUP = 2u;

	private const uint KEYEVENTF_EXTENDEDKEY = 1u;

	private const ushort VK_LCONTROL = 162;

	private const ushort VK_LSHIFT = 160;

	private const ushort VK_LMENU = 164;

	private const ushort VK_LWIN = 91;

	private const ushort VK_VOLUME_MUTE = 173;

	private const ushort VK_VOLUME_DOWN = 174;

	private const ushort VK_VOLUME_UP = 175;

	private const ushort VK_LEFT = 37;

	private const ushort VK_UP = 38;

	private const ushort VK_RIGHT = 39;

	private const ushort VK_DOWN = 40;

	private const ushort VK_ESCAPE = 27;

	private const ushort VK_RETURN = 13;

	private const ushort VK_TAB = 9;

	private const ushort VK_SPACE = 32;

	[DllImport("user32.dll")]
	private static extern bool LockWorkStation();

	[DllImport("shell32.dll", CharSet = CharSet.Auto)]
	private static extern int SHEmptyRecycleBin(IntPtr hwnd, string? pszRootPath, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern nint GetForegroundWindow();

	[DllImport("user32.dll")]
	private static extern bool SetForegroundWindow(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hWnd, int nCmdShow);

	[DllImport("user32.dll")]
	private static extern bool IsIconic(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool BringWindowToTop(nint hWnd);

	[DllImport("user32.dll")]
	private static extern bool EnumWindows(EnumWindowsProc enumProc, nint lParam);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint GetWindowThreadProcessId(nint hWnd, out uint lpdwProcessId);

	[DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
	private static extern int GetWindowText(nint hWnd, StringBuilder lpString, int nMaxCount);

	
	[DllImport("user32.dll")]
	private static extern uint MapVirtualKey(uint uCode, uint uMapType);

	[DllImport("user32.dll")]
	private static extern short GetAsyncKeyState(int nVirtKey);

	[DllImport("user32.dll")]
	private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, nint dwExtraInfo);

	[DllImport("user32.dll", SetLastError = true)]
	private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct SHELLEXECUTEINFO
	{
		public int cbSize;
		public uint fMask;
		public nint hwnd;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpVerb;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpFile;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpParameters;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpDirectory;
		public int nShow;
		public nint hInstApp;
		public nint lpIDList;
		[MarshalAs(UnmanagedType.LPWStr)] public string? lpClass;
		public nint hkeyClass;
		public uint dwHotKey;
		public nint hMonitor;
		public nint hProcess;
	}

	private const uint SEE_MASK_NOCLOSEPROCESS = 0x00000040;
	private const uint SEE_MASK_NOASYNC = 0x00000100;
	private const uint SEE_MASK_HMONITOR = 0x00200000;

	[DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
	private static extern bool ShellExecuteEx(ref SHELLEXECUTEINFO lpExecInfo);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern uint GetProcessId(nint hProcess);

	[DllImport("kernel32.dll", SetLastError = true)]
	private static extern bool CloseHandle(nint hObject);

	private static readonly Channel<ActionItem> s_actionChannel = Channel.CreateUnbounded<ActionItem>(new UnboundedChannelOptions
	{
		SingleReader = true,
		SingleWriter = false
	});

	static ActionExecutor()
	{
		Thread worker = new Thread(ProcessActionQueue)
		{
			Name = "StarPie.ActionExecutor",
			IsBackground = true,
			Priority = ThreadPriority.AboveNormal
		};
		worker.Start();
	}

	public static void EnqueueAction(ActionItem action)
	{
		if (action != null)
		{
			s_actionChannel.Writer.TryWrite(action);
		}
	}

	private static void ProcessActionQueue()
	{
		var reader = s_actionChannel.Reader;
		while (true)
		{
			try
			{
				if (reader.WaitToReadAsync().AsTask().Result)
				{
					while (reader.TryRead(out ActionItem? action))
					{
						if (action != null)
						{
							try
							{
								Execute(action);
							}
							catch
							{
							}
						}
					}
				}
			}
			catch
			{
			}
		}
	}

	public static void Execute(ActionItem action)
	{
		if (action == null)
		{
			return;
		}
		try
		{
			AppLogger.LogInfo($"Executing Action: Name='{action.Name}', Type='{action.Type}', Param='{action.Parameter}', Args='{action.Arguments}', Term='{action.CommandTerminal}'");
			switch (action.Type.Trim())
			{
			case "Launch":
				ExecuteLaunch(action.Parameter, action.Arguments, action.RunAsStandardUser);
				break;
			case "Folder":
			case "OpenFolder":
				ExecuteFolder(action.Parameter);
				break;
			case "Ocr":
			case "ScreenOcr":
				OcrManager.StartCaptureAndRecognize();
				break;
			case "Hotkey":
				ExecuteHotkey(action.Parameter);
				break;
			case "Command":
				ExecuteCommand(action.Parameter, action.CommandTerminal);
				break;
			case "SwitchWindow":
				ExecuteSwitchWindow(action.Parameter);
				break;
			case "Tile":
				WindowTiler.ExecuteTile(action.Parameter);
				break;
			case "TileRestore":
				WindowTiler.RestoreLastLayout();
				break;
			case "MoveMonitor":
				WindowTiler.MoveWindowToNextMonitor();
				break;
			case "ToggleTopmost":
				WindowTiler.ToggleWindowTopmost(action.Parameter);
				break;
			case "WindowOpacity":
				WindowTiler.SetWindowOpacity(action.Parameter);
				break;
			case "Text":
			case "String":
				SendTextInput(action.Parameter);
				break;
			case "WebUrl":
			case "Url":
				ExecuteWebUrl(action.Parameter, action.BrowserChoice, action.BrowserPath);
				break;
			case "System":
				ExecuteSystem(action.Parameter);
				break;
			case "ShellTool":
				ExecuteShellTool(action.Parameter);
				break;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to execute action '{action.Name}' (Type: {action.Type}, Param: {action.Parameter})", ex);
			MessageBox.Show("Failed to execute action '" + action.Name + "': " + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

	public static bool TryToggleProcessWindow(string processOrExePath)
	{
		if (string.IsNullOrWhiteSpace(processOrExePath))
		{
			return false;
		}
		string text = Path.GetFileNameWithoutExtension(processOrExePath).ToLowerInvariant();
		if (text == "explorer" || text == "cmd" || text == "powershell" || text == "wsl" || text == "calc" || text == "calculator" || text == "calculatorapp")
		{
			return false;
		}
		Process[] processesByName = Process.GetProcessesByName(text);
		if ((processesByName == null || processesByName.Length == 0) && text.EndsWith("64"))
		{
			processesByName = Process.GetProcessesByName(text.Substring(0, text.Length - 2));
		}
		if (processesByName == null || processesByName.Length == 0)
		{
			return false;
		}
		nint foregroundWindow = GetForegroundWindow();
		List<nint> windowHandles = new List<nint>();
		Process[] array = processesByName;
		foreach (Process process in array)
		{
			try
			{
				if (process.MainWindowHandle != IntPtr.Zero && IsWindowVisible(process.MainWindowHandle))
				{
					windowHandles.Add(process.MainWindowHandle);
					continue;
				}
				int pid = process.Id;
				EnumWindows(delegate(nint hWnd, nint lParam)
				{
					GetWindowThreadProcessId(hWnd, out var lpdwProcessId);
					if (lpdwProcessId == pid && IsWindowVisible(hWnd))
					{
						StringBuilder stringBuilder = new StringBuilder(256);
						GetWindowText(hWnd, stringBuilder, 256);
						if (stringBuilder.Length > 0)
						{
							windowHandles.Add(hWnd);
						}
					}
					return true;
				}, IntPtr.Zero);
			}
			catch
			{
			}
		}
		if (windowHandles.Count == 0)
		{
			return false;
		}
		foreach (nint item in windowHandles)
		{
			if (item == foregroundWindow && !IsIconic(item))
			{
				ShowWindow(item, 6);
				return true;
			}
		}
		nint num = windowHandles[0];
		if (IsIconic(num))
		{
			ShowWindow(num, 9);
		}
		else
		{
			ShowWindow(num, 5);
		}
		SetForegroundWindow(num);
		BringWindowToTop(num);
		return true;
	}

	public static bool TryToggleFolderWindow(string folderPath)
	{
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			return false;
		}
		string b = folderPath.Trim().Trim('"').TrimEnd('\\', '/');
		try
		{
			Type typeFromProgID = Type.GetTypeFromProgID("Shell.Application");
			if (typeFromProgID != null)
			{
				dynamic val = Activator.CreateInstance(typeFromProgID);
				if (val != null)
				{
					dynamic val2 = val.Windows();
					int num = val2.Count;
					nint foregroundWindow = GetForegroundWindow();
					for (int i = 0; i < num; i++)
					{
						try
						{
							dynamic val3 = val2.Item(i);
							if (!((val3 != null) ? true : false))
							{
								continue;
							}
							string text = val3.LocationURL?.ToString() ?? "";
							if (string.IsNullOrEmpty(text) || !text.StartsWith("file:///", StringComparison.OrdinalIgnoreCase) || !string.Equals(Uri.UnescapeDataString(new Uri(text).LocalPath).TrimEnd('\\', '/'), b, StringComparison.OrdinalIgnoreCase))
							{
								continue;
							}
							nint num2 = (nint)val3.HWND;
							if (num2 == IntPtr.Zero)
							{
								continue;
							}
							if (num2 == foregroundWindow && !IsIconic(num2))
							{
								ShowWindow(num2, 6);
							}
							else
							{
								if (IsIconic(num2))
								{
									ShowWindow(num2, 9);
								}
								else
								{
									ShowWindow(num2, 5);
								}
								SetForegroundWindow(num2);
								BringWindowToTop(num2);
							}
							return true;
						}
						catch
						{
						}
					}
				}
			}
		}
		catch
		{
		}
		return false;
	}

	public static string? FindBrowserExecutable(string browserName)
	{
		string exeName = browserName.ToLowerInvariant() switch
		{
			"chrome" => "chrome.exe",
			"edge" => "msedge.exe",
			"firefox" => "firefox.exe",
			_ => browserName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? browserName : (browserName + ".exe")
		};

		try
		{
			string subKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\" + exeName;
			using (var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(subKey))
			{
				if (key?.GetValue(null) is string hklmPath && File.Exists(hklmPath))
				{
					return hklmPath;
				}
			}
			using (var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(subKey))
			{
				if (key?.GetValue(null) is string hkcuPath && File.Exists(hkcuPath))
				{
					return hkcuPath;
				}
			}
		}
		catch
		{
		}

		try
		{
			string progFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
			string progFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
			string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

			List<string> candidatePaths = new List<string>();
			if (browserName.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
			{
				candidatePaths.Add(Path.Combine(progFiles, @"Google\Chrome\Application\chrome.exe"));
				candidatePaths.Add(Path.Combine(progFilesX86, @"Google\Chrome\Application\chrome.exe"));
				candidatePaths.Add(Path.Combine(localAppData, @"Google\Chrome\Application\chrome.exe"));
			}
			else if (browserName.Equals("Edge", StringComparison.OrdinalIgnoreCase))
			{
				candidatePaths.Add(Path.Combine(progFilesX86, @"Microsoft\Edge\Application\msedge.exe"));
				candidatePaths.Add(Path.Combine(progFiles, @"Microsoft\Edge\Application\msedge.exe"));
			}
			else if (browserName.Equals("Firefox", StringComparison.OrdinalIgnoreCase))
			{
				candidatePaths.Add(Path.Combine(progFiles, @"Mozilla Firefox\firefox.exe"));
				candidatePaths.Add(Path.Combine(progFilesX86, @"Mozilla Firefox\firefox.exe"));
			}

			foreach (string path in candidatePaths)
			{
				if (File.Exists(path))
				{
					return path;
				}
			}
		}
		catch
		{
		}

		return null;
	}

	private static void ExecuteWebUrl(string url, string? browserChoice, string? customBrowserPath)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return;
		}
		string target = url.Trim();
		if (!target.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
		    !target.StartsWith("https://", StringComparison.OrdinalIgnoreCase) &&
		    !target.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
		{
			target = "https://" + target;
		}

		string browser = browserChoice?.Trim() ?? "Default";
		AppLogger.LogInfo($"Executing WebUrl: URL='{target}', Browser='{browser}', CustomPath='{customBrowserPath}'");

		try
		{
			if (browser.Equals("Chrome", StringComparison.OrdinalIgnoreCase))
			{
				string? chromeExe = FindBrowserExecutable("Chrome");
				Process.Start(new ProcessStartInfo
				{
					FileName = !string.IsNullOrEmpty(chromeExe) ? chromeExe : "chrome.exe",
					Arguments = $"\"{target}\"",
					UseShellExecute = true
				});
			}
			else if (browser.Equals("Edge", StringComparison.OrdinalIgnoreCase))
			{
				string? edgeExe = FindBrowserExecutable("Edge");
				if (!string.IsNullOrEmpty(edgeExe))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = edgeExe,
						Arguments = $"\"{target}\"",
						UseShellExecute = true
					});
				}
				else
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "microsoft-edge:" + target,
						UseShellExecute = true
					});
				}
			}
			else if (browser.Equals("Firefox", StringComparison.OrdinalIgnoreCase))
			{
				string? firefoxExe = FindBrowserExecutable("Firefox");
				Process.Start(new ProcessStartInfo
				{
					FileName = !string.IsNullOrEmpty(firefoxExe) ? firefoxExe : "firefox.exe",
					Arguments = $"\"{target}\"",
					UseShellExecute = true
				});
			}
			else if (browser.Equals("Custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(customBrowserPath) && File.Exists(customBrowserPath))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = customBrowserPath,
					Arguments = $"\"{target}\"",
					UseShellExecute = true
				});
			}
			else
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = target,
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to open WebUrl '{target}' with browser '{browser}'", ex);
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = target,
					UseShellExecute = true
				});
			}
			catch (Exception ex2)
			{
				MessageBox.Show("无法打开目标网址: " + ex2.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Warning);
			}
		}
	}

	private static void SafeSetClipboardText(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return;
		}
		try
		{
			if (Application.Current?.Dispatcher != null)
			{
				Application.Current.Dispatcher.Invoke(() =>
				{
					try
					{
						System.Windows.Clipboard.SetDataObject(text, true);
					}
					catch
					{
						System.Windows.Clipboard.SetText(text);
					}
				});
				return;
			}
		}
		catch
		{
		}

		// 备用：若 Dispatcher 不可用或发生跨线程异常，启动独立 STA 线程安全写入
		try
		{
			Thread staThread = new Thread(() =>
			{
				try
				{
					System.Windows.Clipboard.SetDataObject(text, true);
				}
				catch
				{
					try { System.Windows.Clipboard.SetText(text); } catch { }
				}
			});
			staThread.SetApartmentState(ApartmentState.STA);
			staThread.IsBackground = true;
			staThread.Start();
			staThread.Join(500);
		}
		catch
		{
		}
	}

	public static void ExecuteShellTool(string verb)
	{
		if (string.IsNullOrWhiteSpace(verb)) return;
		AppLogger.LogInfo($"Executing ShellTool verb: '{verb}'");

		string v = verb.Trim();
		switch (v)
		{
			case "Windows.CopyAsPath":
			case "copy_path":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count > 0)
				{
					SafeSetClipboardText(string.Join(Environment.NewLine, selected));
				}
				else if (!string.IsNullOrEmpty(folder))
				{
					SafeSetClipboardText(folder);
				}
				break;
			}
			case "StarPie.Builtin.ScreenOCR":
			case "builtin_ocr":
			case "Ocr":
			case "ScreenOcr":
			{
				OcrManager.StartCaptureAndRecognize();
				break;
			}
			case "Windows.RunAs":
			case "run_as_admin":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string targetExe = selected.FirstOrDefault(s => s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ||
															    s.EndsWith(".bat", StringComparison.OrdinalIgnoreCase) ||
															    s.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) ||
															    s.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase)) ?? "";
				if (!string.IsNullOrEmpty(targetExe))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = targetExe,
						Verb = "runas",
						UseShellExecute = true,
						WorkingDirectory = Path.GetDirectoryName(targetExe) ?? folder
					});
				}
				else
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "cmd.exe",
						Verb = "runas",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			case "Windows.TaskManager":
			case "task_manager":
			{
				Process.Start(new ProcessStartInfo("taskmgr.exe") { UseShellExecute = true });
				break;
			}
			case "Windows.SnippingTool":
			case "snipping_tool":
			{
				try
				{
					Process.Start(new ProcessStartInfo("ms-screenclip:") { UseShellExecute = true });
				}
				catch
				{
					ExecuteHotkey("Win+Shift+S");
				}
				break;
			}
			case "Windows.NewFolder":
			case "new_folder":
			{
				ExecuteHotkey("Ctrl+Shift+N");
				break;
			}
			case "Windows.Properties":
			case "file_properties":
			{
				ExecuteHotkey("Alt+Enter");
				break;
			}
			case "Windows.Lock":
			case "lock_screen":
			{
				LockWorkStation();
				break;
			}
			case "Windows.EmptyRecycleBin":
			case "empty_recycle_bin":
			{
				SHEmptyRecycleBin(IntPtr.Zero, null, 7u);
				break;
			}
			case "VSCode.Open":
			case "vscode_open":
			{
				var (folder, selected) = GetActiveExplorerContext();
				if (selected.Count > 0)
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "code",
						Arguments = string.Join(" ", selected.Select(s => $"\"{s}\"")),
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				else
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "code",
						Arguments = $"\"{folder}\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			case "Git.BashHere":
			case "git_bash_here":
			{
				var (folder, _) = GetActiveExplorerContext();
				string[] possibleGitPaths = new[]
				{
					@"C:\Program Files\Git\git-bash.exe",
					@"C:\Program Files (x86)\Git\git-bash.exe",
					Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\Git\git-bash.exe")
				};
				string gitExe = possibleGitPaths.FirstOrDefault(File.Exists) ?? "git-bash.exe";
				Process.Start(new ProcessStartInfo
				{
					FileName = gitExe,
					Arguments = $"--cd=\"{folder}\"",
					UseShellExecute = true,
					WorkingDirectory = folder
				});
				break;
			}
			case "Windows.Terminal":
			case "windows_terminal":
			{
				var (folder, _) = GetActiveExplorerContext();
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "wt.exe",
						Arguments = $"-d \"{folder}\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				catch
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "powershell.exe",
						Arguments = $"-NoExit -Command \"Set-Location '{folder}'\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			case "Windows.CmdHere":
			case "cmd_here":
			{
				var (folder, _) = GetActiveExplorerContext();
				Process.Start(new ProcessStartInfo
				{
					FileName = "cmd.exe",
					Arguments = $"/K cd /d \"{folder}\"",
					UseShellExecute = true,
					WorkingDirectory = folder
				});
				break;
			}
			case "Windows.PowerShellHere":
			case "powershell_here":
			{
				var (folder, _) = GetActiveExplorerContext();
				Process.Start(new ProcessStartInfo
				{
					FileName = "powershell.exe",
					Arguments = $"-NoExit -Command \"Set-Location '{folder}'\"",
					UseShellExecute = true,
					WorkingDirectory = folder
				});
				break;
			}
			case "7-Zip.ExtractHere":
			case "7z_extract_here":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string sevenZipExe = Find7ZipExecutable();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (!string.IsNullOrEmpty(targetArchive) && !string.IsNullOrEmpty(sevenZipExe))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = sevenZipExe,
						Arguments = $"x \"{targetArchive}\" -o\"{folder}\" -y",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			case "7-Zip.ExtractToFolder":
			case "7z_extract_folder":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string sevenZipExe = Find7ZipExecutable();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (!string.IsNullOrEmpty(targetArchive) && !string.IsNullOrEmpty(sevenZipExe))
				{
					string outFolder = Path.Combine(folder, Path.GetFileNameWithoutExtension(targetArchive));
					Process.Start(new ProcessStartInfo
					{
						FileName = sevenZipExe,
						Arguments = $"x \"{targetArchive}\" -o\"{outFolder}\" -y",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			case "Bandizip.AutoExtract":
			case "bandizip_extract":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string bzExe = FindBandizipExecutable();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (!string.IsNullOrEmpty(targetArchive) && !string.IsNullOrEmpty(bzExe))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = bzExe,
						Arguments = $"x -y -o:\"{folder}\" \"{targetArchive}\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			case "WinRAR.ExtractHere":
			case "winrar_extract":
			{
				var (folder, selected) = GetActiveExplorerContext();
				string winrarExe = FindWinRarExecutable();
				string targetArchive = selected.FirstOrDefault(s => IsArchive(s)) ?? "";
				if (!string.IsNullOrEmpty(targetArchive) && !string.IsNullOrEmpty(winrarExe))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = winrarExe,
						Arguments = $"x -ibck -y \"{targetArchive}\" \"{folder}\\\"",
						UseShellExecute = true,
						WorkingDirectory = folder
					});
				}
				break;
			}
			default:
				AppLogger.LogWarn($"Unknown ShellTool verb: '{verb}'");
				break;
		}
	}

	private static (string folder, List<string> selectedPaths) GetActiveExplorerContext()
	{
		string folder = "";
		List<string> selected = new List<string>();
		try
		{
			nint fgHwnd = GetForegroundWindow();
			Type? shellType = Type.GetTypeFromProgID("Shell.Application");
			if (shellType != null)
			{
				dynamic? shell = Activator.CreateInstance(shellType);
				if (shell != null)
				{
					dynamic windows = shell.Windows();
					int count = windows.Count;
					for (int i = 0; i < count; i++)
					{
						try
						{
							dynamic item = windows.Item(i);
							if (item == null) continue;
							long hwnd = item.HWND;
							if ((nint)hwnd == fgHwnd)
							{
								dynamic doc = item.Document;
								if (doc != null)
								{
									folder = doc.Folder?.Self?.Path ?? "";
									dynamic sel = doc.SelectedItems();
									if (sel != null)
									{
										int selCount = sel.Count;
										for (int j = 0; j < selCount; j++)
										{
											string p = sel.Item(j)?.Path ?? "";
											if (!string.IsNullOrEmpty(p)) selected.Add(p);
										}
									}
								}
								break;
							}
						}
						catch { }
					}
				}
			}
		}
		catch { }

		if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
		{
			folder = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
		}
		return (folder, selected);
	}

	private static bool IsArchive(string path)
	{
		if (string.IsNullOrEmpty(path)) return false;
		string ext = Path.GetExtension(path).ToLowerInvariant();
		return ext == ".zip" || ext == ".7z" || ext == ".rar" || ext == ".tar" || ext == ".gz" || ext == ".bz2" || ext == ".xz" || ext == ".iso";
	}

	private static string Find7ZipExecutable()
	{
		string[] paths = new[]
		{
			@"C:\Program Files\7-Zip\7zG.exe",
			@"C:\Program Files\7-Zip\7z.exe",
			@"C:\Program Files (x86)\7-Zip\7zG.exe",
			@"C:\Program Files (x86)\7-Zip\7z.exe"
		};
		return paths.FirstOrDefault(File.Exists) ?? "7zG.exe";
	}

	private static string FindBandizipExecutable()
	{
		string[] paths = new[]
		{
			@"C:\Program Files\Bandizip\Bandizip.exe",
			@"C:\Program Files\Bandizip\bz.exe",
			@"C:\Program Files (x86)\Bandizip\Bandizip.exe"
		};
		return paths.FirstOrDefault(File.Exists) ?? "Bandizip.exe";
	}

	private static string FindWinRarExecutable()
	{
		string[] paths = new[]
		{
			@"C:\Program Files\WinRAR\WinRAR.exe",
			@"C:\Program Files (x86)\WinRAR\WinRAR.exe"
		};
		return paths.FirstOrDefault(File.Exists) ?? "WinRAR.exe";
	}

	private static void ExecuteFolder(string folderPath)
	{
		if (string.IsNullOrWhiteSpace(folderPath))
		{
			return;
		}
		string text = Environment.ExpandEnvironmentVariables(folderPath.Trim().Trim('"'));
		AppLogger.LogInfo($"Executing OpenFolder: '{text}'");
		try
		{
			if (text.StartsWith("::{", StringComparison.OrdinalIgnoreCase) || text.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = text,
					UseShellExecute = true
				});
				return;
			}
			if (Directory.Exists(text))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "\"" + text + "\"",
					UseShellExecute = true
				});
			}
			else if (File.Exists(text))
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = "/select,\"" + text + "\"",
					UseShellExecute = true
				});
			}
			else
			{
				try
				{
					string? root = System.IO.Path.GetPathRoot(text);
					if (!string.IsNullOrEmpty(root) && !Directory.Exists(root))
					{
						AppLogger.LogWarn($"Target drive '{root}' for path '{text}' is not available on this machine.");
						Application.Current?.Dispatcher.BeginInvoke(() =>
						{
							try
							{
								MessageBox.Show($"无法打开目标路径 '{folderPath}'：\n当前计算机上找不到指定的驱动器或磁盘分区（{root}）。", "StarPie - 路径不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
							}
							catch { }
						});
						return;
					}
				}
				catch { }

				Process.Start(new ProcessStartInfo
				{
					FileName = text,
					UseShellExecute = true
				});
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to open folder '{folderPath}'", ex);
			Application.Current?.Dispatcher.BeginInvoke(() =>
			{
				try
				{
					MessageBox.Show("无法打开文件夹 '" + folderPath + "':\n" + ex.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Exclamation);
				}
				catch { }
			});
		}
	}

	private static void ExecuteLaunch(string path, string arguments, bool runAsStandardUser = false)
	{
		if (string.IsNullOrWhiteSpace(path))
		{
			return;
		}
		string text = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
		AppLogger.LogInfo($"Executing Launch: Path='{text}', Args='{arguments}', StandardUser={runAsStandardUser}");
		// 目标显示器 = 鼠标当前所在显示器；启动前快照顶层窗口，供启动后识别新窗口并兜底搬到目标显示器
		nint hMonitor = GetCursorMonitorHandle();
		HashSet<nint> windowSnapshot = WindowTiler.SnapshotTopLevelWindows();
		if (text.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) || (text.Contains("!") && !text.Contains(":\\") && !text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
		{
			string arguments2 = (text.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) ? text : ("shell:AppsFolder\\" + text));
			try
			{
				// 优先直接 ShellExecuteEx 并携带鼠标所在显示器句柄；失败再回退 explorer.exe。
				// 打包应用经激活管理器创建进程，显示器提示通常不会传递，因此再挂启动后兜底搬移
				if (hMonitor == IntPtr.Zero || !TryShellExecuteOnMonitor(arguments2, null, null, hMonitor, out _))
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "explorer.exe",
						Arguments = arguments2,
						UseShellExecute = true
					});
				}
				WatchAndRelocateNewWindows(hMonitor, windowSnapshot, null, text);
			}
			catch (Exception ex)
			{
				AppLogger.LogError($"Failed to launch UWP app: {arguments2}", ex);
				throw;
			}
		}
		else
		{
			if (runAsStandardUser)
			{
				try
				{
					string workDir = "";
					if (File.Exists(text))
					{
						workDir = Path.GetDirectoryName(text) ?? "";
					}
					if (TryLaunchUnelevatedViaExplorer(text, arguments ?? "", workDir))
					{
						AppLogger.LogInfo($"Launched '{text}' with Explorer standard user integrity via IShellDispatch2 (de-elevated)");
						// IShellDispatch2 无法携带显示器句柄，只能靠启动后兜底搬移
						WatchAndRelocateNewWindows(hMonitor, windowSnapshot, null, text);
						return;
					}
				}
				catch (Exception exShell)
				{
					AppLogger.LogWarn($"Unelevated launch failed for '{text}', falling back to Process.Start: {exShell.Message}");
				}
			}

			string exeName = Path.GetFileNameWithoutExtension(text).ToLowerInvariant();
			bool isShellOrSpecial = exeName == "explorer" || exeName == "cmd" || exeName == "powershell" || exeName == "wsl" || exeName == "calc" || exeName == "calculator" || exeName == "calculatorapp";
			if (!isShellOrSpecial && string.IsNullOrWhiteSpace(arguments) && TryToggleProcessWindow(text))
			{
				AppLogger.LogInfo($"Toggled active window for existing process '{text}'");
				return;
			}
			ProcessStartInfo processStartInfo = new ProcessStartInfo
			{
				FileName = text,
				Arguments = (arguments ?? string.Empty),
				UseShellExecute = true
			};
			try
			{
				if (File.Exists(text))
				{
					string directoryName = Path.GetDirectoryName(text);
					if (!string.IsNullOrEmpty(directoryName) && Directory.Exists(directoryName))
					{
						processStartInfo.WorkingDirectory = directoryName;
					}
				}
				else if (Directory.Exists(text))
				{
					processStartInfo.WorkingDirectory = text;
				}
			}
			catch
			{
			}
			try
			{
				// 携带鼠标所在显示器句柄启动（SEE_MASK_HMONITOR），新窗口默认落在当前屏幕而非主屏；
				// ShellExecuteEx 失败时回退到 Process.Start 保持原有行为
				System.Diagnostics.Process? started = null;
				if (hMonitor == IntPtr.Zero || !TryShellExecuteOnMonitor(processStartInfo.FileName, processStartInfo.Arguments, processStartInfo.WorkingDirectory, hMonitor, out started))
				{
					started = System.Diagnostics.Process.Start(processStartInfo);
				}
				// 兜底：启动器转交（如 wt.exe 别名、Code.exe 启动器）或打包应用时显示器提示会丢失，启动后核对并搬移
				WatchAndRelocateNewWindows(hMonitor, windowSnapshot, started, text);
				// 启动后自动把新窗口拉到前台（后台等待主窗口出现 → ActivateWindow，含前台解锁链）
				if (started != null)
				{
					System.Diagnostics.Process proc = started;
					System.Threading.Tasks.Task.Run(delegate
					{
						try
						{
							for (int i = 0; i < 40; i++)
							{
								if (proc.MainWindowHandle != IntPtr.Zero)
								{
									break;
								}
								System.Threading.Thread.Sleep(50);
							}
							if (proc.MainWindowHandle != IntPtr.Zero)
							{
								System.Threading.Thread.Sleep(150); // 等窗口内容就绪再激活
								WindowTaskbarHelper.ActivateWindow(proc.MainWindowHandle);
							}
						}
						catch
						{
						}
					});
				}
			}
			catch (Exception ex)
			{
				AppLogger.LogError($"Process.Start failed for '{text}' with args '{arguments}'", ex);
				throw;
			}
		}
	}

	/// <summary>
	/// Issue #58: 当 StarPie 以管理员提权运行时，通过 Windows 资源管理器 (explorer.exe) 桌面 Shell 中转以标准普通用户权限 (Medium Integrity) 启动外部程序。
	/// 解决以普通权限启动失效、终端仍带管理员盾牌、以及因 UIPI 隔离无法拖入外部文件的问题。
	/// </summary>
	/// <summary>
	/// 启动后兜底：监视新出现的顶层应用窗口，若不在目标显示器上则平移过去。
	/// 覆盖 ShellExecuteEx 显示器提示无法传递的场景：UWP/打包应用激活、标准用户降权启动、启动器转交。
	/// 主路径为 WinEvent 钩子：EVENT_OBJECT_CREATE 时窗口尚未显示即搬移，避免先在主屏闪现再跳过去；
	/// EVENT_OBJECT_SHOW 时再校正一次；后台轮询作为兜底。总时长 4s，首个窗口出现后再观察 1.5s。
	/// </summary>
	private static void WatchAndRelocateNewWindows(nint hMonitor, HashSet<nint> snapshot, Process? proc, string label)
	{
		if (hMonitor == IntPtr.Zero || snapshot == null)
		{
			return;
		}
		new LaunchRelocateSession(hMonitor, snapshot, proc, label).Start();
	}

	/// <summary>
	/// 单次启动的窗口搬移会话。钩子回调（UI 线程）只负责入队，所有 SetWindowPos/ShowWindow 均在后台线程执行，
	/// 避免目标进程初始化期间不泵消息导致 UI 线程被同步等待卡住。
	/// </summary>
	private sealed class LaunchRelocateSession
	{
		private delegate void WinEventDelegate(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime);

		[DllImport("user32.dll")]
		private static extern nint SetWinEventHook(uint eventMin, uint eventMax, nint hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

		[DllImport("user32.dll")]
		private static extern bool UnhookWinEvent(nint hWinEventHook);

		private const uint EVENT_OBJECT_CREATE = 0x8000;
		private const uint EVENT_OBJECT_SHOW = 0x8002;
		private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
		private const uint WINEVENT_SKIPOWNPROCESS = 0x0002;
		private const int TotalTimeoutMs = 4000;
		private const int SettleMs = 1500;

		private readonly nint _hMonitor;
		private readonly HashSet<nint> _snapshot;
		private readonly Process? _proc;
		private readonly string _label;
		private readonly WinEventDelegate _hookProc; // 持有委托引用，防止钩子存活期间被 GC 回收
		private readonly Stopwatch _clock = Stopwatch.StartNew();
		private readonly object _lock = new object();
		private readonly HashSet<nint> _handled = new HashSet<nint>();
		private readonly HashSet<nint> _created = new HashSet<nint>();
		private readonly Queue<(nint hwnd, string stage)> _pending = new Queue<(nint, string)>();
		private readonly AutoResetEvent _wake = new AutoResetEvent(false);
		private nint _hook;
		private long _settleDeadline = -1;
		private volatile bool _stopped;

		public LaunchRelocateSession(nint hMonitor, HashSet<nint> snapshot, Process? proc, string label)
		{
			_hMonitor = hMonitor;
			_snapshot = snapshot;
			_proc = proc;
			_label = label;
			_hookProc = OnWinEvent;
		}

		public void Start()
		{
			System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
			if (dispatcher != null)
			{
				dispatcher.BeginInvoke(new Action(delegate
				{
					if (_stopped)
					{
						return;
					}
					try
					{
						_hook = SetWinEventHook(EVENT_OBJECT_CREATE, EVENT_OBJECT_SHOW, IntPtr.Zero, _hookProc, 0u, 0u, WINEVENT_OUTOFCONTEXT | WINEVENT_SKIPOWNPROCESS);
					}
					catch (Exception ex)
					{
						AppLogger.LogWarn($"[Launch] SetWinEventHook failed: {ex.Message}");
					}
				}));
			}
			System.Threading.Tasks.Task.Run(WorkerLoop);
		}

		/// <summary>钩子回调（UI 线程）：仅筛选并入队，立即返回。</summary>
		private void OnWinEvent(nint hWinEventHook, uint eventType, nint hwnd, int idObject, int idChild, uint idEventThread, uint dwmsEventTime)
		{
			try
			{
				if (_stopped || hwnd == IntPtr.Zero || idObject != 0 || idChild != 0)
				{
					return;
				}
				if (eventType == EVENT_OBJECT_CREATE)
				{
					if (!WindowTiler.IsCandidateAppWindow(hwnd, requireVisible: false))
					{
						return;
					}
					lock (_lock)
					{
						_created.Add(hwnd);
						_pending.Enqueue((hwnd, "create"));
					}
					_wake.Set();
				}
				else if (eventType == EVENT_OBJECT_SHOW)
				{
					// 仅处理本会话期间新建的窗口，避免把旧窗口的重新显示（如取消最小化）误搬
					bool isNew;
					lock (_lock)
					{
						isNew = _created.Contains(hwnd) || !_snapshot.Contains(hwnd);
					}
					if (!isNew || !WindowTiler.IsCandidateAppWindow(hwnd, requireVisible: false))
					{
						return;
					}
					lock (_lock)
					{
						_pending.Enqueue((hwnd, "show"));
					}
					_wake.Set();
				}
			}
			catch
			{
			}
		}

		private void WorkerLoop()
		{
			try
			{
				while (_clock.ElapsedMilliseconds < TotalTimeoutMs)
				{
					_wake.WaitOne(50);
					while (true)
					{
						(nint hwnd, string stage) item;
						lock (_lock)
						{
							if (_pending.Count == 0)
							{
								break;
							}
							item = _pending.Dequeue();
						}
						// show 阶段允许对已处理窗口再校正一次：部分程序在显示前会自行重设位置
						Handle(item.hwnd, item.stage, firstOnly: item.stage != "show");
					}
					foreach (nint h in WindowTiler.FindNewAppWindows(_snapshot))
					{
						Handle(h, "poll", firstOnly: true);
					}
					if (_proc != null)
					{
						try
						{
							_proc.Refresh();
							nint mw = _proc.MainWindowHandle;
							if (mw != IntPtr.Zero)
							{
								Handle(mw, "main", firstOnly: true);
							}
						}
						catch
						{
						}
					}
					long settle = Interlocked.Read(ref _settleDeadline);
					if (settle >= 0 && _clock.ElapsedMilliseconds >= settle)
					{
						break;
					}
				}
				bool none;
				lock (_lock)
				{
					none = _handled.Count == 0;
				}
				if (none)
				{
					AppLogger.LogInfo($"[Launch] No new window detected within {TotalTimeoutMs}ms for '{_label}'");
				}
			}
			catch (Exception ex)
			{
				AppLogger.LogWarn($"[Launch] Relocate session failed for '{_label}': {ex.Message}");
			}
			finally
			{
				Stop();
			}
		}

		private void Handle(nint hwnd, string stage, bool firstOnly)
		{
			bool isNew;
			lock (_lock)
			{
				isNew = _handled.Add(hwnd);
			}
			if (!isNew && firstOnly)
			{
				return;
			}
			bool moved = WindowTiler.EnsureWindowOnMonitor(hwnd, _hMonitor);
			if (isNew || moved)
			{
				AppLogger.LogInfo($"[Launch] Window 0x{hwnd:X} ({stage}) for '{_label}': {(moved ? "moved to cursor monitor" : "already on cursor monitor")}");
			}
			if (Interlocked.Read(ref _settleDeadline) < 0)
			{
				Interlocked.Exchange(ref _settleDeadline, _clock.ElapsedMilliseconds + SettleMs);
			}
		}

		private void Stop()
		{
			if (_stopped)
			{
				return;
			}
			_stopped = true;
			System.Windows.Threading.Dispatcher? dispatcher = Application.Current?.Dispatcher;
			if (dispatcher == null)
			{
				return;
			}
			// UnhookWinEvent 必须在安装钩子的线程上调用
			dispatcher.BeginInvoke(new Action(delegate
			{
				try
				{
					if (_hook != IntPtr.Zero)
					{
						UnhookWinEvent(_hook);
						_hook = IntPtr.Zero;
					}
				}
				catch
				{
				}
				_wake.Dispose();
			}));
		}
	}

	/// <summary>
	/// 获取鼠标当前所在显示器的句柄（失败返回 IntPtr.Zero）
	/// </summary>
	private static nint GetCursorMonitorHandle()
	{
		try
		{
			return ScreenHelper.GetScreenContextAtPoint().HMonitor;
		}
		catch
		{
			return IntPtr.Zero;
		}
	}

	/// <summary>
	/// 通过 ShellExecuteEx + SEE_MASK_HMONITOR 在指定显示器上启动目标。
	/// 对于不自行记忆窗口位置的程序，系统会把其首个窗口放在该显示器上，而不是主屏幕。
	/// 返回 false 表示调用失败，调用方应回退到 Process.Start。
	/// </summary>
	private static bool TryShellExecuteOnMonitor(string file, string? arguments, string? workingDir, nint hMonitor, out Process? process)
	{
		process = null;
		if (string.IsNullOrWhiteSpace(file))
		{
			return false;
		}
		SHELLEXECUTEINFO info = new SHELLEXECUTEINFO
		{
			cbSize = Marshal.SizeOf<SHELLEXECUTEINFO>(),
			fMask = SEE_MASK_NOCLOSEPROCESS | SEE_MASK_NOASYNC | SEE_MASK_HMONITOR,
			hwnd = IntPtr.Zero,
			lpVerb = null,
			lpFile = file,
			lpParameters = string.IsNullOrEmpty(arguments) ? null : arguments,
			lpDirectory = string.IsNullOrEmpty(workingDir) ? null : workingDir,
			nShow = SW_SHOWNORMAL,
			hMonitor = hMonitor
		};
		bool ok = false;
		int lastError = 0;
		try
		{
			// ShellExecuteEx 需要 STA 公寓（与 .NET Process.Start 内部处理一致）；
			// 动作队列工作线程是 MTA，因此在独立 STA 线程上同步执行
			if (Thread.CurrentThread.GetApartmentState() == ApartmentState.STA)
			{
				ok = ShellExecuteEx(ref info);
				lastError = Marshal.GetLastWin32Error();
			}
			else
			{
				SHELLEXECUTEINFO localInfo = info;
				Thread staThread = new Thread(() =>
				{
					ok = ShellExecuteEx(ref localInfo);
					lastError = Marshal.GetLastWin32Error();
				});
				staThread.SetApartmentState(ApartmentState.STA);
				staThread.IsBackground = true;
				staThread.Start();
				staThread.Join();
				info = localInfo;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogWarn($"ShellExecuteEx threw for '{file}': {ex.Message}");
			return false;
		}
		if (!ok)
		{
			AppLogger.LogWarn($"ShellExecuteEx failed for '{file}' (error {lastError}), falling back to Process.Start");
			return false;
		}
		if (info.hProcess != IntPtr.Zero)
		{
			try
			{
				uint pid = GetProcessId(info.hProcess);
				if (pid != 0)
				{
					process = Process.GetProcessById((int)pid);
				}
			}
			catch
			{
				// 进程可能已退出（例如启动器立即转交给已有实例），忽略即可
			}
			finally
			{
				CloseHandle(info.hProcess);
			}
		}
		return true;
	}

	public static bool TryLaunchUnelevatedViaExplorer(string path, string arguments, string workingDir)
	{
		try
		{
			Type? shellType = Type.GetTypeFromProgID("Shell.Application");
			if (shellType == null) return false;

			object? shell = Activator.CreateInstance(shellType);
			if (shell == null) return false;

			object? windows = shellType.InvokeMember("Windows", BindingFlags.InvokeMethod, null, shell, null);
			if (windows == null) return false;

			// SWC_DESKTOP = 8, SWFO_NEEDDISPATCH = 1
			object[] args = new object[] { 0, Type.Missing, 8, 0, 1 };
			ParameterModifier[] modifiers = new ParameterModifier[1];
			modifiers[0] = new ParameterModifier(5);
			modifiers[0][3] = true;

			object? desktop = windows.GetType().InvokeMember(
				"FindWindowSW",
				BindingFlags.InvokeMethod,
				null,
				windows,
				args,
				modifiers,
				null,
				null);

			if (desktop == null) return false;

			object? doc = desktop.GetType().InvokeMember("Document", BindingFlags.GetProperty, null, desktop, null);
			if (doc == null) return false;

			object? app = doc.GetType().InvokeMember("Application", BindingFlags.GetProperty, null, doc, null);
			if (app == null) return false;

			app.GetType().InvokeMember(
				"ShellExecute",
				BindingFlags.InvokeMethod,
				null,
				app,
				new object[] { path, arguments ?? "", workingDir ?? "", "open", 1 });

			return true;
		}
		catch (Exception ex)
		{
			AppLogger.LogWarn($"TryLaunchUnelevatedViaExplorer failed for '{path}': {ex.Message}");
			return false;
		}
	}

	/// <summary>Runs a command in the selected terminal (cmd / PowerShell / WSL), with or without a window.</summary>
	private static void ExecuteCommand(string command, string? terminal)
	{
		if (string.IsNullOrWhiteSpace(command))
		{
			return;
		}
		string term = string.IsNullOrEmpty(terminal) ? "cmd" : terminal.Trim().ToLowerInvariant();
		bool hidden = term.EndsWith("_hidden", StringComparison.OrdinalIgnoreCase);
		string shell = hidden ? term.Substring(0, term.Length - "_hidden".Length) : term;
		// Escape embedded quotes for the cmd/PowerShell wrappers ("" is the escape inside Windows quoting)
		string quoted = command.Replace("\"", "\"\"");
		AppLogger.LogInfo($"Executing Command: Shell='{shell}', Hidden={hidden}, Cmd='{command}'");
		try
		{
			switch (shell)
			{
			case "powershell":
				// Visible: keep the window open (-NoExit). Hidden: run to completion.
				Process.Start(new ProcessStartInfo("powershell.exe", (hidden ? "-NoProfile -Command \"" : "-NoProfile -NoExit -Command \"") + quoted + "\"")
				{
					UseShellExecute = false,
					CreateNoWindow = hidden
				});
				break;
			case "wsl":
				// WSL receives the raw command after "--"; no extra quoting needed
				Process.Start(new ProcessStartInfo("wsl.exe", "-- " + command)
				{
					UseShellExecute = false,
					CreateNoWindow = hidden
				});
				break;
			default:
				// Visible: keep the window open (/k). Hidden: /c so no lingering process.
				Process.Start(new ProcessStartInfo("cmd.exe", (hidden ? "/c \"" : "/k \"") + quoted + "\"")
				{
					UseShellExecute = false,
					CreateNoWindow = hidden
				});
				break;
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Failed to run command '{command}' in '{terminal}'", ex);
			MessageBox.Show("Failed to run command: " + ex.Message, "StarPie", MessageBoxButton.OK, MessageBoxImage.Hand);
		}
	}

/// <summary>切换到任务栏第 N 个窗口；参数缺失/非法默认第 1 个。全程后台线程执行（UIA 遍历/前台激活不得阻塞 UI 与钩子线程）。</summary>
	private static void ExecuteSwitchWindow(string? parameter)
	{
		int n = 1;
		if (int.TryParse(parameter?.Trim(), out int parsed) && parsed > 0)
		{
			n = parsed;
		}
		System.Threading.Tasks.Task.Run(delegate
		{
			if (!WindowTaskbarHelper.ActivateTaskbarSlot(n))
			{
				System.Diagnostics.Debug.WriteLine($"[SwitchWindow] 任务栏第 {n} 个槽位不可用");
			}
		});
	}

	private static bool IsStandardKeyToken(string token)
	{
		string t = token.Trim().ToLowerInvariant();
		return t == "ctrl" || t == "shift" || t == "alt" || t == "win" ||
		       t == "tab" || t == "enter" || t == "esc" || t == "space" ||
		       t == "backspace" || t == "delete" || t == "insert" ||
		       (t.StartsWith("f") && int.TryParse(t.Substring(1), out _)) ||
		       (t.StartsWith("num"));
	}

	public const nint StarPieExtraInfo = 0x53544152;

	public static void ReleaseStuckModifiers()
	{
		try
		{
			// 智能解卡自愈：强制下发 KeyUp 清空系统粘滞状态（双通道 SendInput + keybd_event 注入）
			// 核心守卫：若用户手指物理上正在按着该修饰键（GetAsyncKeyState & 0x8000 != 0），绝不可注销抬起，杜绝破坏前台应用（如 Maya、Photoshop、Blender）的原生组合键
			ushort[] modifiers = new ushort[] { 162, 163, 17, 160, 161, 16, 164, 165, 18, 91, 92 };
			List<INPUT> upInputs = new List<INPUT>();
			foreach (ushort mod in modifiers)
			{
				if ((GetAsyncKeyState((int)mod) & 0x8000) != 0)
				{
					// 用户物理手指正在按压此修饰键，保持物理按下，跳过注销
					continue;
				}
				upInputs.Add(CreateKeyInput(mod, down: false));
				uint flags = KEYEVENTF_KEYUP;
				if (mod == 91 || mod == 92 || mod == 165 || mod == 163)
				{
					flags |= KEYEVENTF_EXTENDEDKEY;
				}
				keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
			}
			if (upInputs.Count > 0)
			{
				SendInput((uint)upInputs.Count, upInputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
			}
		}
		catch
		{
		}
	}

	private static void ExecuteSingleStep(HotkeyStep step)
	{
		try
		{
			// 1. 纯修饰键步骤 (如 Alt 轻敲呼出 Ribbon 菜单，或单独按下 Shift/Ctrl)
			if (step.MainKey == 0 && step.Modifiers.Count > 0)
			{
				List<INPUT> modDowns = new List<INPUT>();
				foreach (ushort mod in step.Modifiers)
				{
					modDowns.Add(CreateKeyInput(mod, down: true));
				}
				SendInput((uint)modDowns.Count, modDowns.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(15);

				List<INPUT> modUps = new List<INPUT>();
				for (int i = step.Modifiers.Count - 1; i >= 0; i--)
				{
					modUps.Add(CreateKeyInput(step.Modifiers[i], down: false));
				}
				SendInput((uint)modUps.Count, modUps.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				return;
			}

			// 2. 复合键步骤 (如 Alt+H 或 Ctrl+K)
			if (step.Modifiers.Count > 0 && step.MainKey != 0)
			{
				List<INPUT> modDowns = new List<INPUT>();
				foreach (ushort mod in step.Modifiers)
				{
					modDowns.Add(CreateKeyInput(mod, down: true));
				}
				SendInput((uint)modDowns.Count, modDowns.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(12);

				INPUT keyDown = CreateKeyInput(step.MainKey, down: true);
				INPUT keyUp = CreateKeyInput(step.MainKey, down: false);

				if (step.MainKey == 44) // VK_SNAPSHOT
				{
					SendInput(2u, new INPUT[] { keyDown, keyUp }, Marshal.SizeOf(typeof(INPUT)));
				}
				else
				{
					SendInput(1u, new INPUT[] { keyDown }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(15);
					SendInput(1u, new INPUT[] { keyUp }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(12);
				}

				List<INPUT> modUps = new List<INPUT>();
				for (int i = step.Modifiers.Count - 1; i >= 0; i--)
				{
					modUps.Add(CreateKeyInput(step.Modifiers[i], down: false));
				}
				SendInput((uint)modUps.Count, modUps.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				return;
			}

			// 3. 单键轻敲步骤 (如 H, V, F 等)
			if (step.MainKey != 0)
			{
				INPUT keyDown = CreateKeyInput(step.MainKey, down: true);
				INPUT keyUp = CreateKeyInput(step.MainKey, down: false);

				if (step.MainKey == 44) // VK_SNAPSHOT
				{
					SendInput(2u, new INPUT[] { keyDown, keyUp }, Marshal.SizeOf(typeof(INPUT)));
				}
				else
				{
					SendInput(1u, new INPUT[] { keyDown }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(15);
					SendInput(1u, new INPUT[] { keyUp }, Marshal.SizeOf(typeof(INPUT)));
				}
			}
		}
		catch (Exception ex)
		{
			AppLogger.LogError($"Error in ExecuteSingleStep ({step}): {ex.Message}");
		}
	}

	private static void ExecuteHotkey(string hotkeyString)
	{
		if (string.IsNullOrWhiteSpace(hotkeyString))
		{
			return;
		}

		HotkeyDetails hotkeyDetails = ParseHotkey(hotkeyString);
		if (hotkeyDetails.Modifiers.Count == 0 && hotkeyDetails.MainKey == 0 && hotkeyDetails.Steps.Count == 0)
		{
			// 如果是连续按键字母（例如 "UU", "US", "WASD"），优先使用原生虚拟按键流发送，保证系统菜单与快捷键接收
			string rawStr = hotkeyString.Trim();
			if (rawStr.Length >= 2 && rawStr.Length <= 8 && rawStr.All(char.IsLetterOrDigit))
			{
				AppLogger.LogInfo($"Executing Key Sequence from raw token: '{rawStr}'");
				foreach (char c in rawStr)
				{
					ushort vk = MapKeyStringToVk(c.ToString());
					if (vk != 0)
					{
						INPUT kDown = CreateKeyInput(vk, down: true);
						INPUT kUp = CreateKeyInput(vk, down: false);
						SendInput(1u, new INPUT[] { kDown }, Marshal.SizeOf(typeof(INPUT)));
						System.Threading.Thread.Sleep(15);
						SendInput(1u, new INPUT[] { kUp }, Marshal.SizeOf(typeof(INPUT)));
						System.Threading.Thread.Sleep(25);
					}
					else
					{
						SendTextInput(c.ToString());
					}
				}
				return;
			}
			AppLogger.LogInfo($"Executing Text Input: '{hotkeyString}'");
			SendTextInput(hotkeyString);
			return;
		}

		// 多步骤序列（包括显式步进如 "Alt, H, V, F"、连续多键如 "Alt+H+V+F"、"U+U"、"Ctrl+K, Ctrl+C" 等）
		if (hotkeyDetails.Steps.Count > 1)
		{
			AppLogger.LogInfo($"Executing Multi-Step Hotkey: '{hotkeyString}' ({hotkeyDetails.Steps.Count} steps: [{string.Join(" -> ", hotkeyDetails.Steps)}])");
			System.Threading.Thread.Sleep(15);
			for (int s = 0; s < hotkeyDetails.Steps.Count; s++)
			{
				HotkeyStep step = hotkeyDetails.Steps[s];
				ExecuteSingleStep(step);
				if (s < hotkeyDetails.Steps.Count - 1)
				{
					int stepDelay = (step.Modifiers.Contains(164) && step.MainKey == 0) ? 35 : 25;
					System.Threading.Thread.Sleep(stepDelay);
				}
			}
			return;
		}

		// 无修饰键的多键序列回退兼容（例如 "U+U", "U+S", "A+B"）
		if (hotkeyDetails.Modifiers.Count == 0 && hotkeyDetails.SequenceKeys.Count > 1)
		{
			AppLogger.LogInfo($"Executing Key Sequence: [{string.Join(" -> ", hotkeyDetails.SequenceKeys)}]");
			foreach (ushort vk in hotkeyDetails.SequenceKeys)
			{
				INPUT kDown = CreateKeyInput(vk, down: true);
				INPUT kUp = CreateKeyInput(vk, down: false);
				SendInput(1u, new INPUT[] { kDown }, Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(15);
				SendInput(1u, new INPUT[] { kUp }, Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(25);
			}
			return;
		}

		AppLogger.LogInfo($"Executing Hotkey: '{hotkeyString}' (MainKey: {hotkeyDetails.MainKey}, Modifiers: [{string.Join(",", hotkeyDetails.Modifiers)}])");

		// 给 DWM 窗口焦点平稳回落预留短暂缓冲时延（轮盘关闭后目标窗口焦点就绪）
		System.Threading.Thread.Sleep(10);

		try
		{
			// 1. If pure modifier combo (e.g. Shift + Alt, Ctrl + Shift)
			if (hotkeyDetails.MainKey == 0 && hotkeyDetails.Modifiers.Count > 0)
			{
				List<INPUT> modDowns = new List<INPUT>();
				foreach (ushort mod in hotkeyDetails.Modifiers)
				{
					modDowns.Add(CreateKeyInput(mod, down: true));
				}
				SendInput((uint)modDowns.Count, modDowns.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(10);
				List<INPUT> modUps = new List<INPUT>();
				for (int i = hotkeyDetails.Modifiers.Count - 1; i >= 0; i--)
				{
					modUps.Add(CreateKeyInput(hotkeyDetails.Modifiers[i], down: false));
				}
				SendInput((uint)modUps.Count, modUps.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				return;
			}

			// 2. Standard Modifier + Main Key combo
			List<INPUT> downInputs = new List<INPUT>();
			foreach (ushort modifier in hotkeyDetails.Modifiers)
			{
				downInputs.Add(CreateKeyInput(modifier, down: true));
			}
			if (downInputs.Count > 0)
			{
				SendInput((uint)downInputs.Count, downInputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
				System.Threading.Thread.Sleep(12);
			}

			if (hotkeyDetails.MainKey != 0)
			{
				INPUT keySeqDown = CreateKeyInput(hotkeyDetails.MainKey, down: true);
				INPUT keySeqUp = CreateKeyInput(hotkeyDetails.MainKey, down: false);

				if (hotkeyDetails.MainKey == 44) // VK_SNAPSHOT (PrintScreen)
				{
					// 瞬态快门模式：PrintScreen Down 与 Up 作为一个原子数据包同时发送（0ms 间隔）
					// 消除在 Down 和 Up 之间由于外部截图工具抢占全局输入焦点而造成的按键序列截断
					SendInput(2u, new INPUT[] { keySeqDown, keySeqUp }, Marshal.SizeOf(typeof(INPUT)));
				}
				else
				{
					SendInput(1u, new INPUT[] { keySeqDown }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(12);
					SendInput(1u, new INPUT[] { keySeqUp }, Marshal.SizeOf(typeof(INPUT)));
					System.Threading.Thread.Sleep(10);
				}
			}
		}
		finally
		{
			// 3. 无论中间是否发生异常，始终安全下发所有已按下修饰键的抬起事件，彻底杜绝系统级粘滞
			// 第一通道：SendInput 队列下发 KeyUp
			List<INPUT> upInputs = new List<INPUT>();
			for (int num = hotkeyDetails.Modifiers.Count - 1; num >= 0; num--)
			{
				upInputs.Add(CreateKeyInput(hotkeyDetails.Modifiers[num], down: false));
			}
			if (upInputs.Count > 0)
			{
				SendInput((uint)upInputs.Count, upInputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
			}

			// 第二通道：keybd_event 直接同步 win32k 全局击键状态表（跨进程/跨权限防御）
			foreach (ushort mod in hotkeyDetails.Modifiers)
			{
				uint flags = KEYEVENTF_KEYUP;
				if (mod == 91 || mod == 92 || mod == 165 || mod == 163)
				{
					flags |= KEYEVENTF_EXTENDEDKEY;
				}
				keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
				if (mod == 162 || mod == 163) keybd_event(17, 0, KEYEVENTF_KEYUP, StarPieExtraInfo); // VK_CONTROL
				else if (mod == 160 || mod == 161) keybd_event(16, 0, KEYEVENTF_KEYUP, StarPieExtraInfo); // VK_SHIFT
				else if (mod == 164 || mod == 165) keybd_event(18, 0, KEYEVENTF_KEYUP, StarPieExtraInfo); // VK_MENU
				else if (mod == 91 || mod == 92) keybd_event((byte)mod, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, StarPieExtraInfo);
			}

			// 4. 双重保险：针对截图软件（Snipaste/PixPin/微信截屏等）或 Win 键系统菜单抢焦场景，
			// 在 +35ms 与 +85ms 异步补发修饰键释放，彻底消灭残留粘滞
			bool hasWinMod = hotkeyDetails.Modifiers.Contains(91) || hotkeyDetails.Modifiers.Contains(92);
			if ((hotkeyDetails.MainKey == 44 || hasWinMod) && hotkeyDetails.Modifiers.Count > 0)
			{
				var modsCopy = hotkeyDetails.Modifiers.ToArray();
				System.Threading.Tasks.Task.Run(async () =>
				{
					try
					{
						await System.Threading.Tasks.Task.Delay(35).ConfigureAwait(false);
						foreach (var mod in modsCopy)
						{
							uint flags = KEYEVENTF_KEYUP;
							if (mod == 91 || mod == 92 || mod == 165 || mod == 163) flags |= KEYEVENTF_EXTENDEDKEY;
							keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
							if (mod == 162 || mod == 163) keybd_event(17, 0, KEYEVENTF_KEYUP, StarPieExtraInfo);
							else if (mod == 91 || mod == 92) keybd_event((byte)mod, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, StarPieExtraInfo);
						}

						await System.Threading.Tasks.Task.Delay(50).ConfigureAwait(false);
						foreach (var mod in modsCopy)
						{
							uint flags = KEYEVENTF_KEYUP;
							if (mod == 91 || mod == 92 || mod == 165 || mod == 163) flags |= KEYEVENTF_EXTENDEDKEY;
							keybd_event((byte)mod, 0, flags, StarPieExtraInfo);
							if (mod == 162 || mod == 163) keybd_event(17, 0, KEYEVENTF_KEYUP, StarPieExtraInfo);
							else if (mod == 91 || mod == 92) keybd_event((byte)mod, 0, KEYEVENTF_KEYUP | KEYEVENTF_EXTENDEDKEY, StarPieExtraInfo);
						}
					}
					catch
					{
					}
				});
			}
		}
	}

	private static void ExecuteSystem(string presetName)
	{
		if (string.IsNullOrEmpty(presetName))
		{
			return;
		}
		string text = presetName.Trim().ToLowerInvariant();

		switch (text)
		{
		case "windowswitcher":
		case "taskswitcher":
		case "alttabsticky":
			ExecuteHotkey("Ctrl+Alt+Tab");
			break;
		case "alttab":
		case "switchwindow":
			ExecuteHotkey("Alt+Tab");
			break;
		case "closewindow":
			ExecuteHotkey("Alt+F4");
			break;
		case "minimize":
			ExecuteHotkey("Win+Down");
			break;
		case "maximize":
			ExecuteHotkey("Win+Up");
			break;
		case "snapleft":
			ExecuteHotkey("Win+Left");
			break;
		case "snapright":
			ExecuteHotkey("Win+Right");
			break;
		case "taskview":
			ExecuteHotkey("Win+Tab");
			break;
		case "prevdesktop":
			ExecuteHotkey("Win+Ctrl+Left");
			break;
		case "nextdesktop":
			ExecuteHotkey("Win+Ctrl+Right");
			break;
		case "showdesktop":
			ExecuteHotkey("Win+D");
			break;
		case "fullscreen":
			ExecuteHotkey("F11");
			break;
		case "screenshot":
			ExecuteHotkey("Win+Shift+S");
			break;
		case "taskmanager":
			if (!TryToggleProcessWindow("taskmgr"))
			{
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "taskmgr.exe",
						UseShellExecute = true
					});
				}
				catch
				{
					ExecuteHotkey("Ctrl+Shift+Esc");
				}
			}
			break;
		case "explorer":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					UseShellExecute = true
				});
			}
			catch
			{
				ExecuteHotkey("Win+E");
			}
			break;
		case "opensettings":
		case "openstarpie":
		case "starpie":
		case "starpie控制台":
		case "控制台":
			Application.Current?.Dispatcher?.BeginInvoke((Action)delegate
			{
				App.ShowSettingsWindow();
			});
			break;
		case "settings":
			if (!TryToggleProcessWindow("SystemSettings"))
			{
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "ms-settings:",
						UseShellExecute = true
					});
				}
				catch
				{
					ExecuteHotkey("Win+I");
				}
			}
			break;
		case "calculator":
			AppLogger.LogInfo("Launching System Calculator");
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "calc.exe",
					UseShellExecute = true
				});
			}
			catch (Exception ex1)
			{
				AppLogger.LogWarn($"Direct calc.exe launch failed: {ex1.Message}. Attempting ms-calculator: URI...");
				try
				{
					Process.Start(new ProcessStartInfo
					{
						FileName = "ms-calculator:",
						UseShellExecute = true
					});
				}
				catch (Exception ex2)
				{
					AppLogger.LogError("Failed to launch calculator via ms-calculator: URI as well", ex2);
					ExecuteHotkey("Win+R");
				}
			}
			break;
		case "rundialog":
			ExecuteHotkey("Win+R");
			break;
		case "windowssearch":
			ExecuteHotkey("Win+S");
			break;
		case "quicksearch":
		case "quickfinder":
		case "nativesearch":
		case "文件秒搜":
		case "快速秒搜":
		case "原生秒搜":
			Application.Current?.Dispatcher?.BeginInvoke((Action)delegate
			{
				QuickSearchWindow.ShowOrActivate();
			});
			break;
		case "clipboardhistory":
			ExecuteHotkey("Win+V");
			break;
		case "lockworkstation":
		case "锁定屏幕":
		case "锁屏":
		case "lock":
			LockWorkStation();
			break;
		case "volumeup":
			SimulateSingleKey(175);
			break;
		case "volumedown":
			SimulateSingleKey(174);
			break;
		case "volumemute":
			SimulateSingleKey(173);
			break;
		case "playpause":
			SimulateSingleKey(179);
			break;
		case "nexttrack":
			SimulateSingleKey(176);
			break;
		case "prevtrack":
			SimulateSingleKey(177);
			break;
		case "stopmedia":
			SimulateSingleKey(178);
			break;
		case "newtab":
			ExecuteHotkey("Ctrl+T");
			break;
		case "closetab":
			ExecuteHotkey("Ctrl+W");
			break;
		case "reopentab":
			ExecuteHotkey("Ctrl+Shift+T");
			break;
		case "refresh":
			ExecuteHotkey("F5");
			break;
		case "hardrefresh":
			ExecuteHotkey("Ctrl+F5");
			break;
		case "zoomin":
			ExecuteHotkey("Ctrl+Plus");
			break;
		case "zoomout":
			ExecuteHotkey("Ctrl+Minus");
			break;
		case "zoomreset":
			ExecuteHotkey("Ctrl+0");
			break;
		case "sleep":
		case "睡眠":
		case "休眠":
		case "suspend":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "rundll32.exe",
					Arguments = "powrprof.dll,SetSuspendState 0,1,0",
					UseShellExecute = true
				});
			}
			catch { }
			break;
		case "restart":
		case "重启":
		case "reboot":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/r /t 0",
					UseShellExecute = true
				});
			}
			catch { }
			break;
		case "shutdown":
		case "关机":
		case "poweroff":
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "shutdown.exe",
					Arguments = "/s /t 0",
					UseShellExecute = true
				});
			}
			catch { }
			break;
		}
	}

	private static void SimulateSingleKey(ushort vk)
	{
		INPUT[] pInputs = new INPUT[2]
		{
			CreateKeyInput(vk, down: true),
			CreateKeyInput(vk, down: false)
		};
		SendInput(2u, pInputs, Marshal.SizeOf(typeof(INPUT)));
	}

	public static void SendTextInput(string text)
	{
		if (string.IsNullOrEmpty(text)) return;
		List<INPUT> inputs = new List<INPUT>();
		foreach (char c in text)
		{
			INPUT down = new INPUT { type = 1u };
			down.U.ki = new KEYBDINPUT
			{
				wVk = 0,
				wScan = (ushort)c,
				dwFlags = 4u, // KEYEVENTF_UNICODE
				time = 0u,
				dwExtraInfo = StarPieExtraInfo
			};
			INPUT up = new INPUT { type = 1u };
			up.U.ki = new KEYBDINPUT
			{
				wVk = 0,
				wScan = (ushort)c,
				dwFlags = 4u | 2u, // KEYEVENTF_UNICODE | KEYEVENTF_KEYUP
				time = 0u,
				dwExtraInfo = StarPieExtraInfo
			};
			inputs.Add(down);
			inputs.Add(up);
		}
		SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf(typeof(INPUT)));
	}

	private static INPUT CreateKeyInput(ushort vk, bool down)
	{
		INPUT result = new INPUT
		{
			type = 1u
		};
		ushort scan = (ushort)MapVirtualKey((uint)vk, 0u);
		if (vk == 44) // VK_SNAPSHOT: 扫描码必须为 0，规避 PS/2 SysReq (0x54) 扫描码异常
		{
			scan = 0;
		}
		result.U.ki = new KEYBDINPUT
		{
			wVk = vk,
			wScan = scan,
			dwFlags = ((!down) ? 2u : 0u),
			time = 0u,
			dwExtraInfo = StarPieExtraInfo
		};
		if (vk == 33 || vk == 34 || vk == 35 || vk == 36 ||
		    vk == 37 || vk == 38 || vk == 39 || vk == 40 ||
		    vk == 45 || vk == 46 ||
		    vk == 91 || vk == 92 ||
		    vk == 111 ||
		    vk == 163 || vk == 165 ||
		    (vk >= 166 && vk <= 179))
		{
			result.U.ki.dwFlags |= 1u;
		}
		return result;
	}

	public static HotkeyStep ParseHotkeyStepToken(string token)
	{
		HotkeyStep step = new HotkeyStep { RawToken = token };
		string[] pieces = token.Split(new char[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		foreach (string piece in pieces)
		{
			string lower = piece.Trim().ToLowerInvariant();
			switch (lower)
			{
				case "ctrl":
				case "control":
				case "lctrl":
				case "rctrl":
					if (!step.Modifiers.Contains(162)) step.Modifiers.Add(162);
					break;
				case "shift":
				case "lshift":
				case "rshift":
					if (!step.Modifiers.Contains(160)) step.Modifiers.Add(160);
					break;
				case "alt":
				case "menu":
				case "lalt":
				case "ralt":
					if (!step.Modifiers.Contains(164)) step.Modifiers.Add(164);
					break;
				case "win":
				case "lwin":
				case "rwin":
				case "windows":
					if (!step.Modifiers.Contains(91)) step.Modifiers.Add(91);
					break;
				default:
					ushort vk = MapKeyStringToVk(lower);
					if (vk != 0)
					{
						step.MainKey = vk;
					}
					break;
			}
		}
		return step;
	}

	public static HotkeyDetails ParseHotkey(string hotkeyString)
	{
		HotkeyDetails details = new HotkeyDetails();
		if (string.IsNullOrWhiteSpace(hotkeyString))
		{
			return details;
		}

		string normalized = hotkeyString.Trim().Replace("->", ",").Replace(">", ",");

		// 1. 如果包含显式步进分隔符（逗号 ',' 或分号 ';'）
		if (normalized.Contains(',') || normalized.Contains(';'))
		{
			string[] chunks = normalized.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (string chunk in chunks)
			{
				string trimmed = chunk.Trim();
				if (!string.IsNullOrEmpty(trimmed))
				{
					HotkeyStep step = ParseHotkeyStepToken(trimmed);
					details.Steps.Add(step);
					foreach (ushort mod in step.Modifiers)
					{
						if (!details.Modifiers.Contains(mod)) details.Modifiers.Add(mod);
					}
					if (step.MainKey != 0)
					{
						details.MainKey = step.MainKey;
						details.SequenceKeys.Add(step.MainKey);
					}
				}
			}
			return details;
		}

		// 2. 无显式步进分隔符：解析以 '+' 或空格分隔的按键
		string[] tokens = normalized.Split(new char[] { '+', ' ' }, StringSplitOptions.RemoveEmptyEntries);
		List<ushort> detectedModifiers = new List<ushort>();
		List<ushort> detectedMainKeys = new List<ushort>();
		List<string> rawNonModifiers = new List<string>();

		for (int i = 0; i < tokens.Length; i++)
		{
			string text = tokens[i].Trim().ToLowerInvariant();
			switch (text)
			{
				case "ctrl":
				case "control":
				case "lctrl":
				case "rctrl":
					if (!detectedModifiers.Contains(162)) detectedModifiers.Add(162);
					break;
				case "shift":
				case "lshift":
				case "rshift":
					if (!detectedModifiers.Contains(160)) detectedModifiers.Add(160);
					break;
				case "alt":
				case "menu":
				case "lalt":
				case "ralt":
					if (!detectedModifiers.Contains(164)) detectedModifiers.Add(164);
					break;
				case "win":
				case "lwin":
				case "rwin":
				case "windows":
					if (!detectedModifiers.Contains(91)) detectedModifiers.Add(91);
					break;
				default:
					ushort vk = MapKeyStringToVk(text);
					if (vk != 0)
					{
						detectedMainKeys.Add(vk);
						rawNonModifiers.Add(tokens[i]);
					}
					break;
			}
		}

		// 2.1 若包含多个非修饰键（如 "Alt+H+V+F"、"U+U"、"A+B+C"），自动按步进序列解析
		if (detectedMainKeys.Count > 1)
		{
			// 如果首项是修饰键（如 Alt），则将其作为第 1 步，后续每一个键作为独立步
			if (detectedModifiers.Count > 0 && tokens.Length > detectedMainKeys.Count)
			{
				string firstLower = tokens[0].Trim().ToLowerInvariant();
				bool firstIsMod = (firstLower == "alt" || firstLower == "menu" || firstLower == "ctrl" || firstLower == "shift" || firstLower == "win");
				if (firstIsMod)
				{
					// 第一步：轻敲修饰键（如 Alt 唤醒 Ribbon KeyTips）
					HotkeyStep modStep = new HotkeyStep { RawToken = tokens[0] };
					modStep.Modifiers.AddRange(detectedModifiers);
					details.Steps.Add(modStep);

					// 后续步：每个非修饰键作为独立步（如 H -> V -> F）
					for (int k = 0; k < detectedMainKeys.Count; k++)
					{
						HotkeyStep keyStep = new HotkeyStep
						{
							MainKey = detectedMainKeys[k],
							RawToken = rawNonModifiers[k]
						};
						details.Steps.Add(keyStep);
					}
				}
				else
				{
					// 若修饰键与主键混合，第一步组合，后续单键
					HotkeyStep firstComboStep = new HotkeyStep { RawToken = tokens[0] };
					firstComboStep.Modifiers.AddRange(detectedModifiers);
					firstComboStep.MainKey = detectedMainKeys[0];
					details.Steps.Add(firstComboStep);

					for (int k = 1; k < detectedMainKeys.Count; k++)
					{
						HotkeyStep keyStep = new HotkeyStep
						{
							MainKey = detectedMainKeys[k],
							RawToken = rawNonModifiers[k]
						};
						details.Steps.Add(keyStep);
					}
				}
			}
			else
			{
				// 无修饰键的多键序列（如 "U+U", "A+B+C"）
				for (int k = 0; k < detectedMainKeys.Count; k++)
				{
					HotkeyStep keyStep = new HotkeyStep
					{
						MainKey = detectedMainKeys[k],
						RawToken = rawNonModifiers[k]
					};
					details.Steps.Add(keyStep);
				}
			}

			details.Modifiers.AddRange(detectedModifiers);
			details.SequenceKeys.AddRange(detectedMainKeys);
			details.MainKey = detectedMainKeys.LastOrDefault();
			return details;
		}

		// 2.2 常规单步快捷键（如 "Ctrl+C", "Win+Shift+S", "Alt+F4", "Shift+Alt"）
		HotkeyStep singleStep = new HotkeyStep { RawToken = normalized };
		singleStep.Modifiers.AddRange(detectedModifiers);
		if (detectedMainKeys.Count == 1)
		{
			singleStep.MainKey = detectedMainKeys[0];
		}
		details.Steps.Add(singleStep);
		details.Modifiers.AddRange(detectedModifiers);
		if (detectedMainKeys.Count == 1)
		{
			details.MainKey = detectedMainKeys[0];
			details.SequenceKeys.Add(detectedMainKeys[0]);
		}
		return details;
	}

	private static ushort MapKeyStringToVk(string keyToken)
	{
		if (string.IsNullOrEmpty(keyToken))
		{
			return 0;
		}
		string text = keyToken.ToLower().Trim();
		if (text.StartsWith("d") && text.Length == 2 && char.IsDigit(text[1]))
		{
			return (ushort)text[1];
		}
		if (text.Length == 1)
		{
			char c = text[0];
			if (c >= 'a' && c <= 'z')
			{
				return (ushort)(65 + (c - 97));
			}
			if (c >= '0' && c <= '9')
			{
				return c;
			}
			switch (c)
			{
			case ';':
				return 186;
			case '+':
			case '=':
				return 187;
			case ',':
				return 188;
			case '-':
				return 189;
			case '.':
				return 190;
			case '/':
				return 191;
			case '`':
				return 192;
			case '[':
				return 219;
			case '\\':
				return 220;
			case ']':
				return 221;
			case '\'':
				return 222;
			}
		}
		if (text.StartsWith("f") && int.TryParse(text.Substring(1), out var result) && result >= 1 && result <= 24)
		{
			return (ushort)(112 + (result - 1));
		}
		if (text.StartsWith("num") || text.StartsWith("numpad"))
		{
			string text2 = text.Replace("numpad", "").Replace("num", "");
			if (int.TryParse(text2, out var result2) && result2 >= 0 && result2 <= 9)
			{
				return (ushort)(96 + result2);
			}
			switch (text2)
			{
			case "add":
			case "plus":
			case "+":
				return 107;
			case "subtract":
			case "minus":
			case "-":
				return 109;
			case "multiply":
			case "star":
			case "*":
				return 106;
			case "divide":
			case "slash":
			case "/":
				return 111;
			case "decimal":
			case "dot":
			case ".":
				return 110;
			}
		}
		switch (text)
		{
		case "left":
			return 37;
		case "up":
			return 38;
		case "right":
			return 39;
		case "down":
			return 40;
		case "home":
			return 36;
		case "end":
			return 35;
		case "pgup":
		case "prior":
		case "pageup":
			return 33;
		case "pgdn":
		case "next":
		case "pagedown":
			return 34;
		case "ins":
		case "insert":
			return 45;
		case "del":
		case "delete":
			return 46;
		case "back":
		case "backspace":
			return 8;
		case "tab":
			return 9;
		case "enter":
		case "return":
			return 13;
		case "esc":
		case "escape":
			return 27;
		case "space":
		case "spacebar":
			return 32;
		case "prtscn":
		case "prtsc":
		case "prntscrn":
		case "snapshot":
		case "printscreen":
		case "print_screen":
		case "print screen":
		case "print":
			return 44;
		case "pause":
			return 19;
		case "capslock":
			return 20;
		case "scrolllock":
			return 145;
		case "numlock":
			return 144;
		case "plus":
			return 187;
		case "minus":
			return 189;
		case "comma":
			return 188;
		case "dot":
		case "period":
			return 190;
		case "slash":
			return 191;
		case "backslash":
			return 220;
		case "semicolon":
			return 186;
		case "quote":
			return 222;
		case "bracketleft":
		case "openbracket":
			return 219;
		case "bracketright":
		case "closebracket":
			return 221;
		case "tilde":
		case "backquote":
			return 192;
		case "volumeup":
			return 175;
		case "volumedown":
			return 174;
		case "mute":
		case "volumemute":
			return 173;
		case "playpause":
		case "mediaplaypause":
			return 179;
		case "nexttrack":
		case "medianext":
			return 176;
		case "mediaprev":
		case "prevtrack":
			return 177;
		case "stopmedia":
		case "mediastop":
			return 178;
		case "browserback":
			return 166;
		case "browserforward":
			return 167;
		case "browserrefresh":
			return 168;
		case "browserstop":
			return 169;
		case "browsersearch":
			return 170;
		case "browserfavorites":
			return 171;
		case "browserhome":
			return 172;
		default:
			return 0;
		}
	}
}
