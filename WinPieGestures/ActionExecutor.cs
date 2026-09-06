using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

	private class HotkeyDetails
	{
		public List<ushort> Modifiers { get; } = new List<ushort>();

		public List<ushort> SequenceKeys { get; } = new List<ushort>();

		public ushort MainKey { get; set; }
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
		if (text.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) || (text.Contains("!") && !text.Contains(":\\") && !text.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
		{
			string arguments2 = (text.StartsWith("shell:AppsFolder", StringComparison.OrdinalIgnoreCase) ? text : ("shell:AppsFolder\\" + text));
			try
			{
				Process.Start(new ProcessStartInfo
				{
					FileName = "explorer.exe",
					Arguments = arguments2,
					UseShellExecute = true
				});
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
					Type? shellType = Type.GetTypeFromProgID("Shell.Application");
					if (shellType != null)
					{
						dynamic shell = Activator.CreateInstance(shellType);
						string workDir = "";
						if (File.Exists(text))
						{
							workDir = Path.GetDirectoryName(text) ?? "";
						}
						shell.ShellExecute(text, arguments ?? "", workDir, "open", 1);
						AppLogger.LogInfo($"Launched '{text}' with Shell standard user integrity via Shell.Application");
						return;
					}
				}
				catch (Exception exShell)
				{
					AppLogger.LogInfo($"Shell.Application launch failed for '{text}', falling back to Process.Start: {exShell.Message}");
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
				System.Diagnostics.Process started = System.Diagnostics.Process.Start(processStartInfo);
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
			ushort[] modifiers = new ushort[] { 162, 163, 17, 160, 161, 16, 164, 165, 18, 91, 92 };
			List<INPUT> upInputs = new List<INPUT>();
			foreach (ushort mod in modifiers)
			{
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

	private static void ExecuteHotkey(string hotkeyString)
	{
		if (string.IsNullOrWhiteSpace(hotkeyString))
		{
			return;
		}

		HotkeyDetails hotkeyDetails = ParseHotkey(hotkeyString);
		if (hotkeyDetails.Modifiers.Count == 0 && hotkeyDetails.MainKey == 0)
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

		// 无修饰键的多键序列（例如 "U+U", "U+S", "A+B"）
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

	private static HotkeyDetails ParseHotkey(string hotkeyString)
	{
		HotkeyDetails hotkeyDetails = new HotkeyDetails();
		string[] array = hotkeyString.Split(new char[3] { '+', ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < array.Length; i++)
		{
			string text = array[i].Trim().ToLower();
			switch (text)
			{
			case "ctrl":
			case "control":
			case "lctrl":
			case "rctrl":
				if (!hotkeyDetails.Modifiers.Contains(162))
				{
					hotkeyDetails.Modifiers.Add(162);
				}
				continue;
			case "shift":
			case "lshift":
			case "rshift":
				if (!hotkeyDetails.Modifiers.Contains(160))
				{
					hotkeyDetails.Modifiers.Add(160);
				}
				continue;
			case "alt":
			case "menu":
			case "lalt":
			case "ralt":
				if (!hotkeyDetails.Modifiers.Contains(164))
				{
					hotkeyDetails.Modifiers.Add(164);
				}
				continue;
			case "win":
			case "lwin":
			case "rwin":
			case "windows":
				if (!hotkeyDetails.Modifiers.Contains(91))
				{
					hotkeyDetails.Modifiers.Add(91);
				}
				continue;
			}
			ushort num = MapKeyStringToVk(text);
			if (num != 0)
			{
				hotkeyDetails.MainKey = num;
				hotkeyDetails.SequenceKeys.Add(num);
			}
		}
		return hotkeyDetails;
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
