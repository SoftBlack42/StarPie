using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public class SlotViewModel : INotifyPropertyChanged, IDisposable
{
	public static readonly List<SystemPresetItem> SystemPresetList = new List<SystemPresetItem>
	{
		new SystemPresetItem
		{
			Key = "WindowSwitcher",
			Category = "窗口管理",
			DisplayName = "常驻窗口切换器 (Window Switcher / Ctrl+Alt+Tab)",
			DefaultName = "窗口切换",
			DefaultIconKey = "TaskView"
		},
		new SystemPresetItem
		{
			Key = "AltTab",
			Category = "窗口管理",
			DisplayName = "快速切至上一窗口 (Alt+Tab)",
			DefaultName = "切换窗口",
			DefaultIconKey = "TaskView"
		},
		new SystemPresetItem
		{
			Key = "CloseWindow",
			Category = "窗口管理",
			DisplayName = "关闭当前窗口 (Close / Alt+F4)",
			DefaultName = "关闭窗口",
			DefaultIconKey = "CloseWindow"
		},
		new SystemPresetItem
		{
			Key = "Minimize",
			Category = "窗口管理",
			DisplayName = "最小化窗口 (Minimize / Win+Down)",
			DefaultName = "最小化",
			DefaultIconKey = "Minimize"
		},
		new SystemPresetItem
		{
			Key = "Maximize",
			Category = "窗口管理",
			DisplayName = "最大化/还原 (Maximize / Win+Up)",
			DefaultName = "最大化",
			DefaultIconKey = "Maximize"
		},
		new SystemPresetItem
		{
			Key = "SnapLeft",
			Category = "窗口管理",
			DisplayName = "左半屏贴靠 (Snap Left / Win+Left)",
			DefaultName = "靠左分屏",
			DefaultIconKey = "SnapLeft"
		},
		new SystemPresetItem
		{
			Key = "SnapRight",
			Category = "窗口管理",
			DisplayName = "右半屏贴靠 (Snap Right / Win+Right)",
			DefaultName = "靠右分屏",
			DefaultIconKey = "SnapRight"
		},
		new SystemPresetItem
		{
			Key = "TaskView",
			Category = "窗口管理",
			DisplayName = "任务视图/多任务 (Task View / Win+Tab)",
			DefaultName = "任务视图",
			DefaultIconKey = "TaskView"
		},
		new SystemPresetItem
		{
			Key = "PrevDesktop",
			Category = "窗口管理",
			DisplayName = "上一虚拟桌面 (Prev Desktop)",
			DefaultName = "上一桌面",
			DefaultIconKey = "PrevDesktop"
		},
		new SystemPresetItem
		{
			Key = "NextDesktop",
			Category = "窗口管理",
			DisplayName = "下一虚拟桌面 (Next Desktop)",
			DefaultName = "下一桌面",
			DefaultIconKey = "NextDesktop"
		},
		new SystemPresetItem
		{
			Key = "ShowDesktop",
			Category = "窗口管理",
			DisplayName = "显示桌面 (Desktop / Win+D)",
			DefaultName = "显示桌面",
			DefaultIconKey = "ShowDesktop"
		},
		new SystemPresetItem
		{
			Key = "FullScreen",
			Category = "窗口管理",
			DisplayName = "全屏切换 (Full Screen / F11)",
			DefaultName = "全屏切换",
			DefaultIconKey = "FullScreen"
		},
		new SystemPresetItem
		{
			Key = "Screenshot",
			Category = "窗口管理",
			DisplayName = "屏幕截图 (Screenshot / Win+Shift+S)",
			DefaultName = "屏幕截图",
			DefaultIconKey = "Screenshot"
		},
		new SystemPresetItem
		{
			Key = "TaskManager",
			Category = "系统工具",
			DisplayName = "任务管理器 (Task Manager / Ctrl+Shift+Esc)",
			DefaultName = "任务管理器",
			DefaultIconKey = "TaskManager"
		},
		new SystemPresetItem
		{
			Key = "Explorer",
			Category = "系统工具",
			DisplayName = "文件资源管理器 (Explorer / Win+E)",
			DefaultName = "资源管理器",
			DefaultIconKey = "Explorer"
		},
		new SystemPresetItem
		{
			Key = "OpenSettings",
			Category = "系统工具",
			DisplayName = "StarPie 控制台 (StarPie Settings)",
			DefaultName = "StarPie控制台",
			DefaultIconKey = "Settings"
		},
		new SystemPresetItem
		{
			Key = "Settings",
			Category = "系统工具",
			DisplayName = "Windows 设置 (Settings / Win+I)",
			DefaultName = "系统设置",
			DefaultIconKey = "Settings"
		},
		new SystemPresetItem
		{
			Key = "Calculator",
			Category = "系统工具",
			DisplayName = "计算器 (Calculator / calc.exe)",
			DefaultName = "计算器",
			DefaultIconKey = "Calculator"
		},
		new SystemPresetItem
		{
			Key = "RunDialog",
			Category = "系统工具",
			DisplayName = "运行窗口 (Run / Win+R)",
			DefaultName = "运行",
			DefaultIconKey = "RunDialog"
		},
		new SystemPresetItem
		{
			Key = "WindowsSearch",
			Category = "系统工具",
			DisplayName = "系统搜索 (Search / Win+S)",
			DefaultName = "搜索",
			DefaultIconKey = "WindowsSearch"
		},
		new SystemPresetItem
		{
			Key = "QuickSearch",
			Category = "系统工具",
			DisplayName = "全盘文件与程序秒搜 (Quick Finder)",
			DefaultName = "快速秒搜",
			DefaultIconKey = "Search"
		},
		new SystemPresetItem
		{
			Key = "ClipboardHistory",
			Category = "系统工具",
			DisplayName = "剪贴板历史 (Clipboard / Win+V)",
			DefaultName = "剪贴板",
			DefaultIconKey = "ClipboardHistory"
		},
		new SystemPresetItem
		{
			Key = "Lock",
			Category = "系统工具",
			DisplayName = "锁定电脑 (Lock Workstation)",
			DefaultName = "锁定电脑",
			DefaultIconKey = "Lock"
		},
		new SystemPresetItem
		{
			Key = "VolumeUp",
			Category = "媒体音效",
			DisplayName = "音量增加 (Volume Up)",
			DefaultName = "音量加",
			DefaultIconKey = "VolumeUp"
		},
		new SystemPresetItem
		{
			Key = "VolumeDown",
			Category = "媒体音效",
			DisplayName = "音量减小 (Volume Down)",
			DefaultName = "音量减",
			DefaultIconKey = "VolumeDown"
		},
		new SystemPresetItem
		{
			Key = "VolumeMute",
			Category = "媒体音效",
			DisplayName = "静音切换 (Mute)",
			DefaultName = "静音切换",
			DefaultIconKey = "VolumeMute"
		},
		new SystemPresetItem
		{
			Key = "PlayPause",
			Category = "媒体音效",
			DisplayName = "播放/暂停 (Play/Pause)",
			DefaultName = "播放/暂停",
			DefaultIconKey = "PlayPause"
		},
		new SystemPresetItem
		{
			Key = "NextTrack",
			Category = "媒体音效",
			DisplayName = "下一曲 (Next Track)",
			DefaultName = "下一曲",
			DefaultIconKey = "NextTrack"
		},
		new SystemPresetItem
		{
			Key = "PrevTrack",
			Category = "媒体音效",
			DisplayName = "上一曲 (Previous Track)",
			DefaultName = "上一曲",
			DefaultIconKey = "PrevTrack"
		},
		new SystemPresetItem
		{
			Key = "StopMedia",
			Category = "媒体音效",
			DisplayName = "停止播放 (Stop)",
			DefaultName = "停止",
			DefaultIconKey = "VolumeMute"
		},
		new SystemPresetItem
		{
			Key = "NewTab",
			Category = "网页浏览",
			DisplayName = "新建标签页 (New Tab / Ctrl+T)",
			DefaultName = "新建标签",
			DefaultIconKey = "NewTab"
		},
		new SystemPresetItem
		{
			Key = "CloseTab",
			Category = "网页浏览",
			DisplayName = "关闭标签页 (Close Tab / Ctrl+W)",
			DefaultName = "关闭标签",
			DefaultIconKey = "CloseTab"
		},
		new SystemPresetItem
		{
			Key = "ReopenTab",
			Category = "网页浏览",
			DisplayName = "恢复关闭标签 (Reopen / Ctrl+Shift+T)",
			DefaultName = "恢复标签",
			DefaultIconKey = "ReopenTab"
		},
		new SystemPresetItem
		{
			Key = "Refresh",
			Category = "网页浏览",
			DisplayName = "刷新页面 (Refresh / F5)",
			DefaultName = "刷新",
			DefaultIconKey = "Refresh"
		},
		new SystemPresetItem
		{
			Key = "HardRefresh",
			Category = "网页浏览",
			DisplayName = "强制刷新 (Hard Refresh / Ctrl+F5)",
			DefaultName = "强制刷新",
			DefaultIconKey = "Refresh"
		},
		new SystemPresetItem
		{
			Key = "ZoomIn",
			Category = "网页浏览",
			DisplayName = "页面放大 (Zoom In / Ctrl++)",
			DefaultName = "放大",
			DefaultIconKey = "ZoomIn"
		},
		new SystemPresetItem
		{
			Key = "ZoomOut",
			Category = "网页浏览",
			DisplayName = "页面缩小 (Zoom Out / Ctrl+-)",
			DefaultName = "缩小",
			DefaultIconKey = "ZoomOut"
		},
		new SystemPresetItem
		{
			Key = "ZoomReset",
			Category = "网页浏览",
			DisplayName = "默认缩放 (Reset Zoom / Ctrl+0)",
			DefaultName = "默认缩放",
			DefaultIconKey = "ZoomReset"
		},
		new SystemPresetItem
		{
			Key = "Sleep",
			Category = "电源控制",
			DisplayName = "系统睡眠 (Sleep)",
			DefaultName = "睡眠",
			DefaultIconKey = "Sleep"
		},
		new SystemPresetItem
		{
			Key = "Restart",
			Category = "电源控制",
			DisplayName = "重启电脑 (Restart)",
			DefaultName = "重启",
			DefaultIconKey = "Restart"
		},
		new SystemPresetItem
		{
			Key = "Shutdown",
			Category = "电源控制",
			DisplayName = "关闭电脑 (Shutdown)",
			DefaultName = "关机",
			DefaultIconKey = "Shutdown"
		}
	};

	public static readonly Dictionary<string, string> SystemPresets = SystemPresetList.ToDictionary((SystemPresetItem x) => x.Key, (SystemPresetItem x) => x.FormattedDisplay);

	public int PositionIndex { get; private set; }

	public int SectorCount { get; private set; }

	public string DirectionLabel { get; private set; }

	public ActionItem Action { get; private set; }

	public bool IsVisible { get; private set; }

	public bool CanMoveUp { get; private set; }

	public bool CanMoveDown { get; private set; }

	public string Name
	{
		get
		{
			return Action.Name ?? "";
		}
		set
		{
			if (Action.Name != value)
			{
				Action.Name = value;
				OnPropertyChanged("Name");
			}
		}
	}

	public string Type
	{
		get
		{
			if (!string.IsNullOrEmpty(Action.Type))
			{
				return Action.Type;
			}
			return "Hotkey";
		}
		set
		{
			if (!(Action.Type != value) || string.IsNullOrEmpty(value))
			{
				return;
			}
			Action.Type = value;
			if ((value == "Folder" || value == "OpenFolder") && string.IsNullOrEmpty(IconKey))
			{
				IconKey = "Folder";
				if (string.IsNullOrEmpty(Name) || Name.StartsWith("快捷动作") || Name.StartsWith("动作"))
				{
					Name = I18n.T("ActionTypeFolderShort");
				}
			}
			if ((value == "WebUrl" || value == "Url") && string.IsNullOrEmpty(IconKey))
			{
				IconKey = "Globe";
				if (string.IsNullOrEmpty(Name) || Name.StartsWith("快捷动作") || Name.StartsWith("动作"))
				{
					Name = I18n.T("ActionTypeWebUrlShort");
				}
			}
			if (value == "SwitchWindow" && (string.IsNullOrEmpty(Name) || Name.StartsWith("快捷动作") || Name.StartsWith("动作")))
			{
				Name = I18n.T("ActionTypeSwitchWindowShort");
			}
			if (value == "SwitchWindow" && string.IsNullOrWhiteSpace(Action.Parameter))
			{
				NthWindowIndex = "1";
			}
			if (value == "Tile" && (string.IsNullOrEmpty(Name) || Name.StartsWith("快捷动作") || Name.StartsWith("动作")))
			{
				Name = I18n.T("ActionTypeTileShort");
			}
			if (value == "Tile" && string.IsNullOrWhiteSpace(Action.Parameter))
			{
				Action.Parameter = "2L";
			}
			if (value == "Tile" && string.IsNullOrEmpty(IconKey) && string.IsNullOrEmpty(InheritAppIconPath))
			{
				IconKey = "Tile"; // 默认使用平铺四宫格 logo
			}
			if (value == "Tile" && (Action.SubActions == null || Action.SubActions.Count == 0))
			{
				// 平铺动作自带子菜单：7 布局 + 「恢复上次平铺」——放置即带还原入口
				PopulateTileSubActions();
			}
			if (value == "ShellTool")
			{
				if (string.IsNullOrEmpty(IconKey))
				{
					IconKey = "Copy";
				}
				if (string.IsNullOrEmpty(Name) || Name.StartsWith("快捷动作") || Name.StartsWith("动作"))
				{
					Name = "复制文件/文件夹路径";
				}
				if (string.IsNullOrEmpty(Action.Parameter))
				{
					Action.Parameter = "Windows.CopyAsPath";
				}
			}
			OnPropertyChanged("Type");
			OnPropertyChanged("IsHotkeyType");
			OnPropertyChanged("IsLaunchType");
			OnPropertyChanged("IsWebUrlType");
			OnPropertyChanged("IsFolderType");
			OnPropertyChanged("IsSystemType");
			OnPropertyChanged("IsCommandType");
			OnPropertyChanged("IsSwitchWindowType");
			OnPropertyChanged("IsTileType");
			OnPropertyChanged("IsOcrType");
			OnPropertyChanged("IsShellToolType");
		}
	}

	public bool IsWebUrlType => Type == "WebUrl" || Type == "Url";

	public string BrowserChoice
	{
		get => Action.BrowserChoice ?? "Default";
		set
		{
			if (Action.BrowserChoice != value)
			{
				Action.BrowserChoice = value;
				OnPropertyChanged("BrowserChoice");
				OnPropertyChanged("IsCustomBrowser");
			}
		}
	}

	public string BrowserPath
	{
		get => Action.BrowserPath ?? "";
		set
		{
			if (Action.BrowserPath != value)
			{
				Action.BrowserPath = value;
				OnPropertyChanged("BrowserPath");
			}
		}
	}

	public bool IsCustomBrowser => string.Equals(BrowserChoice, "Custom", StringComparison.OrdinalIgnoreCase);

	public string Parameter
	{
		get
		{
			return Action.Parameter ?? "";
		}
		set
		{
			if (Action.Parameter != value)
			{
				Action.Parameter = value;
				OnPropertyChanged("Parameter");
			}
		}
	}

	public string CommandTerminal
	{
		get
		{
			return Action.CommandTerminal ?? "cmd";
		}
		set
		{
			if (Action.CommandTerminal != value && !string.IsNullOrEmpty(value))
			{
				Action.CommandTerminal = value;
				OnPropertyChanged("CommandTerminal");
			}
		}
	}

	public string Arguments
	{
		get
		{
			return Action.Arguments ?? "";
		}
		set
		{
			if (Action.Arguments != value)
			{
				Action.Arguments = value;
				OnPropertyChanged("Arguments");
			}
		}
	}

	public string IconKey
	{
		get
		{
			return Action.IconKey ?? "";
		}
		set
		{
			if (Action.IconKey != value)
			{
				Action.IconKey = value;
				OnPropertyChanged("IconKey");
				OnPropertyChanged("IconDisplayText");
				OnPropertyChanged("HasVectorIcon");
				OnPropertyChanged("ShowVectorIcon");
				OnPropertyChanged("VectorIconData");
			}
		}
	}

	public string CustomIconSvg
	{
		get
		{
			return Action.CustomIconSvg ?? "";
		}
		set
		{
			if (Action.CustomIconSvg != value)
			{
				Action.CustomIconSvg = value;
				OnPropertyChanged("CustomIconSvg");
				OnPropertyChanged("IconDisplayText");
				OnPropertyChanged("HasVectorIcon");
				OnPropertyChanged("ShowVectorIcon");
				OnPropertyChanged("VectorIconData");
			}
		}
	}

	public string? InheritAppIconPath
	{
		get => Action.InheritAppIconPath;
		set
		{
			if (Action.InheritAppIconPath != value)
			{
				Action.InheritAppIconPath = value;
				OnPropertyChanged(nameof(InheritAppIconPath));
				OnPropertyChanged(nameof(HasInheritedAppIcon));
				OnPropertyChanged(nameof(InheritedAppIcon));
				OnPropertyChanged(nameof(ShowVectorIcon));
				OnPropertyChanged(nameof(IconDisplayText));
			}
		}
	}

	public bool HasInheritedAppIcon => !string.IsNullOrWhiteSpace(InheritAppIconPath);

	public ImageSource? InheritedAppIcon => HasInheritedAppIcon ? IconHelper.GetIcon(InheritAppIconPath!) : null;

	public bool ShowVectorIcon => HasVectorIcon && !HasInheritedAppIcon;

	public string IconDisplayText
	{
		get
		{
			if (HasInheritedAppIcon)
			{
				try
				{
					string fn = System.IO.Path.GetFileNameWithoutExtension(InheritAppIconPath);
					if (!string.IsNullOrEmpty(fn)) return fn;
				}
				catch { }
			}
			if (!string.IsNullOrEmpty(IconKey))
			{
				return IconKey;
			}
			if (!string.IsNullOrEmpty(CustomIconSvg))
			{
				return "自定义SVG";
			}
			return "图标...";
		}
	}

	public bool HasVectorIcon => VectorIconData != null;

	public Geometry? VectorIconData
	{
		get
		{
			string text = null;
			if (!string.IsNullOrEmpty(CustomIconSvg))
			{
				text = CustomIconSvg;
			}
			else if (!string.IsNullOrEmpty(IconKey))
			{
				if (IconKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
				{
					IconHelper.CustomIconItem customIconItem = IconHelper.GetCustomIcons().FirstOrDefault((IconHelper.CustomIconItem c) => c.Key == IconKey);
					if (customIconItem != null && customIconItem.IsSvg)
					{
						text = customIconItem.SvgData;
					}
				}
				else
				{
					text = IconHelper.GetSvgPathByKey(IconKey);
				}
			}
			if (!string.IsNullOrEmpty(text))
			{
				try
				{
					return Geometry.Parse(text);
				}
				catch
				{
				}
			}
			return null;
		}
	}

	public string SelectedSystemPreset
	{
		get
		{
			if (!(Action.Type == "System"))
			{
				return "Lock";
			}
			return Action.Parameter ?? "Lock";
		}
		set
		{
			if (!(Action.Parameter != value) || string.IsNullOrEmpty(value))
			{
				return;
			}
			Action.Parameter = value;
			OnPropertyChanged("SelectedSystemPreset");
			OnPropertyChanged("Parameter");
			SystemPresetItem systemPresetItem = SystemPresetList.FirstOrDefault((SystemPresetItem x) => string.Equals(x.Key, value, StringComparison.OrdinalIgnoreCase));
			if (systemPresetItem != null)
			{
				if (string.IsNullOrEmpty(Name) || Name == "快捷动作" || SystemPresetList.Any((SystemPresetItem p) => p.DefaultName == Name))
				{
					Name = systemPresetItem.DefaultName;
				}
				if (string.IsNullOrEmpty(IconKey) || SystemPresetList.Any((SystemPresetItem p) => p.DefaultIconKey == IconKey))
				{
					IconKey = systemPresetItem.DefaultIconKey;
				}
			}
		}
	}

	public bool IsHotkeyType => Type == "Hotkey";

	public bool IsLaunchType => Type == "Launch";

	public bool IsFolderType
	{
		get
		{
			if (!(Type == "Folder"))
			{
				return Type == "OpenFolder";
			}
			return true;
		}
	}

	public bool IsSystemType => Type == "System";

	public bool IsOcrType => Type == "Ocr" || Type == "ScreenOcr";

	public bool IsShellToolType => Type == "ShellTool";

	public int SubActionCount => Action.SubActions?.Count ?? 0;

	public string SubActionButtonText
	{
		get
		{
			if (SubActionCount <= 0)
			{
				return "➕ 级联";
			}
			return $"⚙\ufe0f 级联 ({SubActionCount})";
		}
	}

	public List<ActionTypeOption> ActionTypes => new List<ActionTypeOption>
	{
		new ActionTypeOption
		{
			Tag = "Hotkey",
			DisplayText = I18n.T("ActionTypeHotkeyShort")
		},
		new ActionTypeOption
		{
			Tag = "Launch",
			DisplayText = I18n.T("ActionTypeLaunchShort")
		},
		new ActionTypeOption
		{
			Tag = "WebUrl",
			DisplayText = I18n.T("ActionTypeWebUrlShort")
		},
		new ActionTypeOption
		{
			Tag = "Folder",
			DisplayText = I18n.T("ActionTypeFolderShort")
		},
		new ActionTypeOption
		{
			Tag = "Command",
			DisplayText = I18n.T("ActionTypeCommandShort")
		},
		new ActionTypeOption
		{
			Tag = "SwitchWindow",
			DisplayText = I18n.T("ActionTypeSwitchWindowShort")
		},
		new ActionTypeOption
		{
			Tag = "Tile",
			DisplayText = I18n.T("ActionTypeTileShort")
		},
		new ActionTypeOption
		{
			Tag = "MoveMonitor",
			DisplayText = I18n.T("ActionTypeMoveMonitorShort")
		},
		new ActionTypeOption
		{
			Tag = "ToggleTopmost",
			DisplayText = I18n.T("ActionTypeTopmostShort")
		},
		new ActionTypeOption
		{
			Tag = "WindowOpacity",
			DisplayText = I18n.T("ActionTypeOpacityShort")
		},
		new ActionTypeOption
		{
			Tag = "ShellTool",
			DisplayText = "⚡ " + I18n.T("ActionTypeShellToolShort")
		},
		new ActionTypeOption
		{
			Tag = "System",
			DisplayText = I18n.T("ActionTypeSystemShort")
		}
	};

	public static List<ActionTypeItem> AggregatedActionTypes => new List<ActionTypeItem>
	{
		new ActionTypeItem { Tag = "Hotkey", DisplayText = "⌨️ " + I18n.T("ActionTypeHotkeyShort") },
		new ActionTypeItem { Tag = "Launch", DisplayText = "🚀 " + I18n.T("ActionTypeLaunchShort") },
		new ActionTypeItem { Tag = "WebUrl", DisplayText = "🌐 " + I18n.T("ActionTypeWebUrlShort") },
		new ActionTypeItem { Tag = "Folder", DisplayText = "📁 " + I18n.T("ActionTypeFolderShort") },
		new ActionTypeItem { Tag = "Command", DisplayText = "💻 " + I18n.T("ActionTypeCommandShort") },
		new ActionTypeItem { Tag = "Ocr", DisplayText = "📝 " + I18n.T("ActionTypeOcrShort") },
		new ActionTypeItem { Tag = "WindowManager", DisplayText = "🪟 " + I18n.T("ActionTypeWindowManagerShort") },
		new ActionTypeItem { Tag = "ShellTool", DisplayText = "⚡ " + I18n.T("ActionTypeShellToolShort") },
		new ActionTypeItem { Tag = "System", DisplayText = "⚙️ " + I18n.T("ActionTypeSystemShort") }
	}
	// 插件动作追加在内置动作之后：内置项的顺序属于用户的肌肉记忆，不应被打乱。
	// 插件系统关闭 / 未安装插件时返回的只是那个兜底项，行为与从前完全一致。
	.Concat(PluginActionBinding.BuildActionTypeItems())
	.ToList();

	public static List<ActionTypeItem> LocalizedActionTypes => new List<ActionTypeItem>
	{
		new ActionTypeItem
		{
			Tag = "Hotkey",
			DisplayText = "⌨️ " + I18n.T("ActionTypeHotkeyShort")
		},
		new ActionTypeItem
		{
			Tag = "Launch",
			DisplayText = "🚀 " + I18n.T("ActionTypeLaunchShort")
		},
		new ActionTypeItem
		{
			Tag = "WebUrl",
			DisplayText = "🌐 " + I18n.T("ActionTypeWebUrlShort")
		},
		new ActionTypeItem
		{
			Tag = "Folder",
			DisplayText = "📁 " + I18n.T("ActionTypeFolderShort")
		},
		new ActionTypeItem
		{
			Tag = "Command",
			DisplayText = "💻 " + I18n.T("ActionTypeCommandShort")
		},
		new ActionTypeItem
		{
			Tag = "SwitchWindow",
			DisplayText = "🔢 " + I18n.T("ActionTypeSwitchWindowShort")
		},
		new ActionTypeItem
		{
			Tag = "Tile",
			DisplayText = "🔲 " + I18n.T("ActionTypeTileShort")
		},
		new ActionTypeItem
		{
			Tag = "MoveMonitor",
			DisplayText = "🖥️ " + I18n.T("ActionTypeMoveMonitorShort")
		},
		new ActionTypeItem
		{
			Tag = "ToggleTopmost",
			DisplayText = "📌 " + I18n.T("ActionTypeTopmostShort")
		},
		new ActionTypeItem
		{
			Tag = "WindowOpacity",
			DisplayText = "👁️ " + I18n.T("ActionTypeOpacityShort")
		},
		new ActionTypeItem
		{
			Tag = "Ocr",
			DisplayText = "📝 " + I18n.T("ActionTypeOcrShort")
		},
		new ActionTypeItem
		{
			Tag = "ShellTool",
			DisplayText = "⚡ " + I18n.T("ActionTypeShellToolShort")
		},
		new ActionTypeItem
		{
			Tag = "System",
			DisplayText = "⚙️ " + I18n.T("ActionTypeSystemShort")
		}
	}
	.Concat(PluginActionBinding.BuildActionTypeItems())
	.ToList();

	/// <summary>Localized terminal options (shared by the sub-action editor).</summary>
	public static List<ActionTypeItem> LocalizedTerminals => new List<ActionTypeItem>
	{
		new ActionTypeItem { Tag = "cmd", DisplayText = I18n.T("TerminalCmd") },
		new ActionTypeItem { Tag = "powershell", DisplayText = I18n.T("TerminalPowerShell") },
		new ActionTypeItem { Tag = "wsl", DisplayText = I18n.T("TerminalWsl") },
		new ActionTypeItem { Tag = "cmd_hidden", DisplayText = I18n.T("TerminalCmdHidden") },
		new ActionTypeItem { Tag = "powershell_hidden", DisplayText = I18n.T("TerminalPowerShellHidden") },
		new ActionTypeItem { Tag = "wsl_hidden", DisplayText = I18n.T("TerminalWslHidden") }
	};

	/// <summary>Localized terminal options for Command actions.</summary>
	public List<ActionTypeOption> Terminals => new List<ActionTypeOption>
	{
		new ActionTypeOption { Tag = "cmd", DisplayText = I18n.T("TerminalCmd") },
		new ActionTypeOption { Tag = "powershell", DisplayText = I18n.T("TerminalPowerShell") },
		new ActionTypeOption { Tag = "wsl", DisplayText = I18n.T("TerminalWsl") },
		new ActionTypeOption { Tag = "cmd_hidden", DisplayText = I18n.T("TerminalCmdHidden") },
		new ActionTypeOption { Tag = "powershell_hidden", DisplayText = I18n.T("TerminalPowerShellHidden") },
		new ActionTypeOption { Tag = "wsl_hidden", DisplayText = I18n.T("TerminalWslHidden") }
	};

	public bool IsCommandType => Type == "Command";

	public bool IsSwitchWindowType => Type == "SwitchWindow";

	public bool IsTileType => Type == "Tile";

	/// <summary>平铺布局下拉（key → 显示名）。</summary>
	public static List<ActionTypeOption> StaticTileLayoutOptions
	{
		get
		{
			List<ActionTypeOption> list = new List<ActionTypeOption>
			{
				new ActionTypeOption { Tag = WindowTiler.CycleParam, DisplayText = "🔄 " + I18n.T("TileCycleLabel") },
				new ActionTypeOption { Tag = WindowTiler.CycleBackParam, DisplayText = "⬅️ " + I18n.T("TileCycleBackLabel") },
				new ActionTypeOption { Tag = WindowTiler.RestoreParam, DisplayText = "⏪ " + I18n.T("TileRestoreAllLabel") }
			};
			foreach (string key in WindowTiler.LayoutKeys)
			{
				list.Add(new ActionTypeOption { Tag = key, DisplayText = WindowTiler.LayoutDisplayName(key) });
			}
			return list;
		}
	}

	public List<ActionTypeOption> TileLayoutOptions => StaticTileLayoutOptions;

	/// <summary>一键预置子项：7 种布局 + 「恢复上次平铺」（级联子菜单 = 布局/还原选择器）。</summary>
	public void PopulateTileSubActions()
	{
		List<ActionItem> list = new List<ActionItem>();
		foreach (string key in WindowTiler.LayoutKeys)
		{
			list.Add(new ActionItem { Type = "Tile", Parameter = key, Name = WindowTiler.LayoutDisplayName(key), IconKey = "Tile" });
		}
		list.Add(new ActionItem { Type = "Tile", Parameter = WindowTiler.RestoreParam, Name = I18n.T("TileRestoreAllLabel"), IconKey = "Tile" });
		Action.SubActions = list;
		NotifySubActionsChanged();
	}

	/// <summary>平铺布局（写入 Parameter）。</summary>
	public string TileLayout
	{
		get
		{
			return Action.Parameter ?? "";
		}
		set
		{
			if (Action.Parameter != value)
			{
				Action.Parameter = value;
				OnPropertyChanged("TileLayout");
			}
		}
	}

	/// <summary>任务栏第 N 个窗口的序号（1~20，仅数字）。</summary>
	public string NthWindowIndex
	{
		get
		{
			return Action.Parameter ?? "";
		}
		set
		{
			string digits = string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());
			if (int.TryParse(digits, out int n))
			{
				n = Math.Max(1, Math.Min(20, n));
				digits = n.ToString();
			}
			if (Action.Parameter != digits)
			{
				Action.Parameter = digits;
				OnPropertyChanged("NthWindowIndex");
			}
		}
	}

	public string TestButtonText => I18n.T("BtnTest");

	public string PositionSlotLabel => I18n.T("SectorPositionSlot");

	public string MoveUpToolTip => I18n.T("SectorMoveUp");

	public string MoveDownToolTip => I18n.T("SectorMoveDown");

	private readonly Action _languageChangedHandler;

	private bool _isDisposed;

	public event PropertyChangedEventHandler? PropertyChanged;

	public void NotifySubActionsChanged()
	{
		OnPropertyChanged("SubActionCount");
		OnPropertyChanged("SubActionButtonText");
	}

	public SlotViewModel(int positionIndex, int sectorCount, string directionLabel, ActionItem action)
	{
		PositionIndex = positionIndex;
		SectorCount = sectorCount;
		DirectionLabel = directionLabel;
		Action = action ?? new ActionItem
		{
			Type = "Hotkey",
			Name = "快捷动作",
			Parameter = ""
		};
		IsVisible = true;
		CanMoveUp = positionIndex > 0;
		CanMoveDown = positionIndex < sectorCount - 1;
		_languageChangedHandler = HandleLanguageChanged;
		I18n.LanguageChanged += _languageChangedHandler;
	}

	public SlotViewModel(string directionLabel, ActionItem action)
		: this(0, 8, directionLabel, action)
	{
	}

	public void Update(int positionIndex, int sectorCount, string directionLabel, ActionItem? action, bool isVisible)
	{
		PositionIndex = positionIndex;
		SectorCount = sectorCount;
		DirectionLabel = directionLabel ?? string.Empty;
		Action = action ?? new ActionItem
		{
			Type = "Hotkey",
			Name = $"快捷动作 {positionIndex + 1}",
			Parameter = ""
		};
		IsVisible = isVisible;
		CanMoveUp = isVisible && positionIndex > 0;
		CanMoveDown = isVisible && positionIndex < sectorCount - 1;

		OnPropertyChanged(nameof(PositionIndex));
		OnPropertyChanged(nameof(SectorCount));
		OnPropertyChanged(nameof(DirectionLabel));
		OnPropertyChanged(nameof(Action));
		OnPropertyChanged(nameof(IsVisible));
		OnPropertyChanged(nameof(CanMoveUp));
		OnPropertyChanged(nameof(CanMoveDown));
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(Type));
		OnPropertyChanged(nameof(Parameter));
		OnPropertyChanged(nameof(Arguments));
		OnPropertyChanged(nameof(IconKey));
		OnPropertyChanged(nameof(CustomIconSvg));
		OnPropertyChanged(nameof(IconDisplayText));
		OnPropertyChanged(nameof(HasVectorIcon));
		OnPropertyChanged(nameof(ShowVectorIcon));
		OnPropertyChanged(nameof(VectorIconData));
		OnPropertyChanged(nameof(InheritAppIconPath));
		OnPropertyChanged(nameof(HasInheritedAppIcon));
		OnPropertyChanged(nameof(InheritedAppIcon));
		OnPropertyChanged(nameof(SelectedSystemPreset));
		OnPropertyChanged(nameof(IsHotkeyType));
		OnPropertyChanged(nameof(IsLaunchType));
		OnPropertyChanged(nameof(IsFolderType));
		OnPropertyChanged(nameof(IsSystemType));
		OnPropertyChanged(nameof(IsCommandType));
		OnPropertyChanged(nameof(IsOcrType));
		OnPropertyChanged(nameof(IsShellToolType));
		OnPropertyChanged(nameof(CommandTerminal));
		OnPropertyChanged(nameof(Terminals));
		OnPropertyChanged(nameof(TestButtonText));
		OnPropertyChanged(nameof(PositionSlotLabel));
		OnPropertyChanged(nameof(MoveUpToolTip));
		OnPropertyChanged(nameof(MoveDownToolTip));
		OnPropertyChanged(nameof(SubActionCount));
		OnPropertyChanged(nameof(SubActionButtonText));
	}

	private void HandleLanguageChanged()
	{
		if (_isDisposed)
		{
			return;
		}
		OnPropertyChanged(nameof(ActionTypes));
		OnPropertyChanged(nameof(TestButtonText));
		OnPropertyChanged(nameof(PositionSlotLabel));
		OnPropertyChanged(nameof(MoveUpToolTip));
		OnPropertyChanged(nameof(MoveDownToolTip));
		OnPropertyChanged(nameof(IconDisplayText));
		OnPropertyChanged(nameof(SubActionButtonText));
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}
		_isDisposed = true;
		I18n.LanguageChanged -= _languageChangedHandler;
		PropertyChanged = null;
	}

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
