using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Windows.Resources;
using GdiColor = System.Drawing.Color;
using GdiSize = System.Drawing.Size;

namespace WinPieGestures;

/// <summary>
/// 进程级系统托盘控制器。托盘生命周期独立于 SettingsWindow，
/// 确保静默启动和设置窗口关闭后仍可操作后台核心。
/// </summary>
public sealed class TrayController : IDisposable
{
	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ChangeWindowMessageFilter(uint message, uint dwFlag);

	[DllImport("user32.dll", SetLastError = true)]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool ChangeWindowMessageFilterEx(nint hWnd, uint message, uint action, nint changeInfo);

	private const uint MSGFLT_ADD = 1;
	private const uint MSGFLT_ALLOW = 1;

	private NotifyIcon? _notifyIcon;
	private ToolStripMenuItem? _pauseResumeMenuItem;
	private Icon? _ownedIcon;
	private bool _isPaused;
	private bool _isDark;
	private bool _disposed;

	public event Action<int>? OpenSettingsRequested;
	public event Action? TogglePauseRequested;
	public event Action? ElevateRequested;
	public event Action? ExitRequested;

	public void Initialize(bool isPaused, bool isDark)
	{
		ObjectDisposedException.ThrowIf(_disposed, this);
		if (_notifyIcon != null)
		{
			UpdatePauseState(isPaused);
			ApplyTheme(isDark);
			return;
		}

		_isPaused = isPaused;
		_isDark = isDark;
		_ownedIcon = LoadTrayIcon();
		_notifyIcon = new NotifyIcon
		{
			Icon = _ownedIcon,
			Visible = true,
			Text = I18n.T("TrayTooltip")
		};
		_notifyIcon.DoubleClick += NotifyIcon_DoubleClick;
		RefreshMenu();
		ApplyUipiProtection();
	}

	public void RefreshMenu()
	{
		if (_disposed || _notifyIcon == null)
		{
			return;
		}

		ContextMenuStrip menu = new ContextMenuStrip
		{
			ShowImageMargin = false,
			ShowCheckMargin = false,
			Font = new Font("Segoe UI", 9.5f, FontStyle.Regular),
			Padding = new Padding(3, 4, 3, 4)
		};

		ToolStripMenuItem versionItem = new ToolStripMenuItem(
			"StarPie v" + (Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.7.0"))
		{
			Enabled = false,
			Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
			Padding = new Padding(12, 6, 12, 6)
		};
		menu.Items.Add(versionItem);
		menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, 3, 0, 3) });

		_pauseResumeMenuItem = new ToolStripMenuItem(
			_isPaused ? I18n.T("TrayResume") : I18n.T("TrayPause"),
			null,
			(_, _) => TogglePauseRequested?.Invoke())
		{
			Padding = new Padding(12, 5, 12, 5)
		};
		menu.Items.Add(_pauseResumeMenuItem);

		menu.Items.Add(CreateMenuItem(I18n.T("TrayPreferences"), () => OpenSettingsRequested?.Invoke(-1)));
		menu.Items.Add(CreateMenuItem(I18n.T("TrayAppearance"), () => OpenSettingsRequested?.Invoke(1)));
		menu.Items.Add(CreateMenuItem(I18n.T("TrayGestures"), () => OpenSettingsRequested?.Invoke(2)));
		menu.Items.Add(CreateMenuItem(I18n.T("TrayAbout"), () => OpenSettingsRequested?.Invoke(4)));
		menu.Items.Add(CreateMenuItem(I18n.T("TrayElevate"), () => ElevateRequested?.Invoke()));
		menu.Items.Add(new ToolStripSeparator { Margin = new Padding(0, 3, 0, 3) });
		menu.Items.Add(CreateMenuItem(I18n.T("TrayExit"), () => ExitRequested?.Invoke()));

		ContextMenuStrip? previousMenu = _notifyIcon.ContextMenuStrip;
		_notifyIcon.ContextMenuStrip = menu;
		previousMenu?.Dispose();
		ApplyTheme(_isDark);
		UpdatePauseState(_isPaused);
	}

	public void ApplyTheme(bool isDark)
	{
		_isDark = isDark;
		if (_disposed || _notifyIcon?.ContextMenuStrip == null)
		{
			return;
		}

		ContextMenuStrip menu = _notifyIcon.ContextMenuStrip;
		menu.Renderer = new ModernTrayRenderer(isDark);
		menu.BackColor = isDark
			? GdiColor.FromArgb(24, 24, 27)
			: GdiColor.FromArgb(255, 255, 255);

		GdiColor enabledColor = isDark
			? GdiColor.FromArgb(244, 244, 245)
			: GdiColor.FromArgb(15, 23, 42);
		GdiColor disabledColor = isDark
			? GdiColor.FromArgb(148, 163, 184)
			: GdiColor.FromArgb(100, 116, 139);

		foreach (ToolStripItem item in menu.Items)
		{
			if (item is ToolStripMenuItem menuItem)
			{
				menuItem.ForeColor = menuItem.Enabled ? enabledColor : disabledColor;
			}
		}
		menu.Invalidate();
	}

	public void UpdatePauseState(bool isPaused)
	{
		_isPaused = isPaused;
		if (_disposed || _notifyIcon == null)
		{
			return;
		}

		if (_pauseResumeMenuItem != null)
		{
			_pauseResumeMenuItem.Text = isPaused ? I18n.T("TrayResume") : I18n.T("TrayPause");
		}
		_notifyIcon.Text = isPaused
			? "StarPie (" + I18n.T("TrayPause") + ")"
			: I18n.T("TrayTooltip");
	}

	public void ShowBalloonTip(int timeout, string title, string message, ToolTipIcon icon)
	{
		if (_disposed || _notifyIcon == null)
		{
			return;
		}
		try
		{
			_notifyIcon.ShowBalloonTip(timeout, title, message, icon);
		}
		catch
		{
		}
	}

	private void ApplyUipiProtection()
	{
		try
		{
			uint[] globalMessages =
			{
				0x0233, // WM_DROPFILES
				0x004A, // WM_COPYDATA
				0x0049, // WM_COPYGLOBALDATA
				0x001A, // WM_SETTINGCHANGE
				0x007E, // WM_DISPLAYCHANGE
				0x0111, // WM_COMMAND
				0x0400, // WM_USER
				0x0401  // WM_USER + 1
			};

			foreach (uint message in globalMessages)
			{
				try
				{
					ChangeWindowMessageFilter(message, MSGFLT_ADD);
				}
				catch
				{
				}
			}

			if (_notifyIcon == null)
			{
				return;
			}

			try
			{
				FieldInfo? windowField = typeof(NotifyIcon).GetField(
					"window",
					BindingFlags.NonPublic | BindingFlags.Instance);
				if (windowField?.GetValue(_notifyIcon) is not NativeWindow nativeWindow || nativeWindow.Handle == IntPtr.Zero)
				{
					return;
				}

				uint[] windowMessages =
				{
					0x0233, // WM_DROPFILES
					0x004A, // WM_COPYDATA
					0x0049, // WM_COPYGLOBALDATA
					0x001A, // WM_SETTINGCHANGE
					0x007E, // WM_DISPLAYCHANGE
					0x0111, // WM_COMMAND
					0x0400, // WM_USER
					0x0401, // WM_USER + 1
					0x0200, // WM_MOUSEMOVE
					0x0201, // WM_LBUTTONDOWN
					0x0202, // WM_LBUTTONUP
					0x0204, // WM_RBUTTONDOWN
					0x0205  // WM_RBUTTONUP
				};

				foreach (uint message in windowMessages)
				{
					try
					{
						ChangeWindowMessageFilterEx(nativeWindow.Handle, message, MSGFLT_ALLOW, IntPtr.Zero);
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}
		catch
		{
		}
	}

	private void NotifyIcon_DoubleClick(object? sender, EventArgs e)
	{
		OpenSettingsRequested?.Invoke(-1);
	}

	private static ToolStripMenuItem CreateMenuItem(string text, Action onClick)
	{
		return new ToolStripMenuItem(text, null, (_, _) => onClick())
		{
			Padding = new Padding(12, 5, 12, 5)
		};
	}

	private static Icon LoadTrayIcon()
	{
		GdiSize smallSize = SystemInformation.SmallIconSize;
		if (smallSize.Width <= 0 || smallSize.Height <= 0)
		{
			smallSize = new GdiSize(16, 16);
		}

		Icon? icon = TryLoadPackIcon("tray_icon.ico", smallSize)
			?? TryLoadFileIcon("tray_icon.ico", smallSize)
			?? TryLoadPackIcon("app_icon.ico", smallSize)
			?? TryLoadFileIcon("app_icon.ico", smallSize);

		if (icon != null)
		{
			return icon;
		}

		try
		{
			string? processPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
			if (!string.IsNullOrEmpty(processPath) && File.Exists(processPath))
			{
				return Icon.ExtractAssociatedIcon(processPath) ?? (Icon)SystemIcons.Application.Clone();
			}
		}
		catch
		{
		}
		return (Icon)SystemIcons.Application.Clone();
	}

	private static Icon? TryLoadPackIcon(string fileName, GdiSize size)
	{
		try
		{
			StreamResourceInfo? resource = System.Windows.Application.GetResourceStream(
				new Uri($"pack://application:,,,/{fileName}"));
			if (resource == null)
			{
				return null;
			}
			using Stream stream = resource.Stream;
			return new Icon(stream, size);
		}
		catch
		{
			return null;
		}
	}

	private static Icon? TryLoadFileIcon(string fileName, GdiSize size)
	{
		try
		{
			string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);
			if (!File.Exists(path))
			{
				return null;
			}
			using FileStream stream = File.OpenRead(path);
			return new Icon(stream, size);
		}
		catch
		{
			return null;
		}
	}

	public void Dispose()
	{
		if (_disposed)
		{
			return;
		}
		_disposed = true;

		if (_notifyIcon != null)
		{
			_notifyIcon.DoubleClick -= NotifyIcon_DoubleClick;
			_notifyIcon.Visible = false;
			_notifyIcon.ContextMenuStrip?.Dispose();
			_notifyIcon.Dispose();
			_notifyIcon = null;
		}
		_pauseResumeMenuItem = null;
		_ownedIcon?.Dispose();
		_ownedIcon = null;
	}
}
