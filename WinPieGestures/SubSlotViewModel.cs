using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Media;
using WinPieGestures.Plugins;

namespace WinPieGestures;

public class SubSlotViewModel : INotifyPropertyChanged
{
	public int IndexNumber { get; set; }

	public ActionItem Action { get; set; } = new ActionItem();

	private bool _isExpanded = true;
	public bool IsExpanded
	{
		get => _isExpanded;
		set
		{
			if (_isExpanded != value)
			{
				_isExpanded = value;
				OnPropertyChanged(nameof(IsExpanded));
				OnPropertyChanged(nameof(ExpandToggleText));
				OnPropertyChanged(nameof(ExpandToggleArrow));
			}
		}
	}

	public string ExpandToggleText => IsExpanded ? "收起配置" : "展开配置";
	public string ExpandToggleArrow => IsExpanded ? "▲" : "▼";

	private bool _canMoveUp;
	public bool CanMoveUp
	{
		get => _canMoveUp;
		set
		{
			if (_canMoveUp != value)
			{
				_canMoveUp = value;
				OnPropertyChanged(nameof(CanMoveUp));
			}
		}
	}

	private bool _canMoveDown;
	public bool CanMoveDown
	{
		get => _canMoveDown;
		set
		{
			if (_canMoveDown != value)
			{
				_canMoveDown = value;
				OnPropertyChanged(nameof(CanMoveDown));
			}
		}
	}

	public string Name
	{
		get => Action.Name ?? "";
		set
		{
			if (Action.Name != value)
			{
				Action.Name = value;
				OnPropertyChanged(nameof(Name));
			}
		}
	}

	public string Type
	{
		get => !string.IsNullOrEmpty(Action.Type) ? Action.Type : "Hotkey";
		set
		{
			if (Action.Type != value && !string.IsNullOrEmpty(value))
			{
				Action.Type = value;
				if ((value == "Folder" || value == "OpenFolder") && string.IsNullOrEmpty(IconKey))
				{
					IconKey = "Folder";
					if (string.IsNullOrEmpty(Name) || Name.StartsWith("子动作"))
					{
						Name = "打开文件夹";
					}
				}
				if ((value == "WebUrl" || value == "Url") && string.IsNullOrEmpty(IconKey))
				{
					IconKey = "Globe";
					if (string.IsNullOrEmpty(Name) || Name.StartsWith("子动作"))
					{
						Name = "打开网址";
					}
				}
				if (value == "SwitchWindow" && (string.IsNullOrEmpty(Name) || Name.StartsWith("子动作")))
				{
					Name = "切换窗口";
				}
				if (value == "SwitchWindow" && string.IsNullOrWhiteSpace(Action.Parameter))
				{
					NthWindowIndex = "1";
				}
				if (value == "Tile" && string.IsNullOrEmpty(IconKey) && string.IsNullOrEmpty(InheritAppIconPath))
				{
					IconKey = "Tile";
				}
				NotifyAllPropertiesChanged();

				// 类型切换是子下拉唯一需要重建候选集的时机（见 NotifyAllPropertiesChanged 的说明）。
				OnPropertyChanged(nameof(PluginActionOptions));
			}
		}
	}

	public List<ActionTypeItem> AggregatedActionTypes => SlotViewModel.AggregatedActionTypes;

	public string AggregatedType
	{
		get
		{
			string t = Type;
			if (t == PluginActionBinding.TypeName)
			{
				// 类型下拉里插件动作只有一项，类型即自身；具体动作由子下拉承载。
				return PluginActionBinding.TypeName;
			}
			if (t == "Tile" || t == "ToggleTopmost" || t == "MoveMonitor" || t == "WindowOpacity" || t == "SwitchWindow" || t == "WindowManager")
			{
				return "WindowManager";
			}
			if (t == "ScreenOcr") return "Ocr";
			if (t == "App") return "Launch";
			if (t == "Url") return "WebUrl";
			if (t == "OpenFolder") return "Folder";
			return t;
		}
		set
		{
			if (string.IsNullOrEmpty(value)) return;
			if (value == PluginActionBinding.TypeName)
			{
				// 只切类型，刻意不清插件引用：来回切换类型不该把已配好的动作弄丢。
				Type = PluginActionBinding.TypeName;
			}
			else if (value == "WindowManager")
			{
				if (!IsWindowManagerType)
				{
					PluginActionBinding.Clear(Action);
					Type = "Tile";
					if (string.IsNullOrEmpty(Parameter)) Parameter = "2L";
				}
			}
			else
			{
				// 切回内置类型时清掉插件引用，避免「内置类型 + 残留插件引用」的混合状态。
				PluginActionBinding.Clear(Action);
				Type = value;
			}
			NotifyAllPropertiesChanged();
		}
	}

	/// <summary>当前子动作是否为插件动作（用于界面显示对应的编辑面板与子下拉）。</summary>
	public bool IsPluginType => Type == PluginActionBinding.TypeName;

	/// <summary>子下拉的候选插件动作，已按插件分组（分组头即插件显示名，本身不可选中）。</summary>
	public ICollectionView? PluginActionOptions => PluginActionBinding.BuildPluginActionView();

	/// <summary>子下拉当前选中的插件动作全 ID。</summary>
	public string? SelectedPluginActionFullId
	{
		get => PluginActionBinding.ProjectSelectedAction(Action);
		set
		{
			// 下拉框重建时会把 SelectedValue 置空，那不是用户的意图 —— 忽略即可。
			if (string.IsNullOrEmpty(value)) return;

			if (PluginActionBinding.ProjectSelectedAction(Action) == value) return;

			if (PluginActionBinding.Apply(Action, value))
			{
				OnPropertyChanged(nameof(SelectedPluginActionFullId));
				OnPropertyChanged(nameof(IsPluginActionBroken));
				NotifyAllPropertiesChanged();
			}
		}
	}

	/// <summary>所引用的插件动作是否已失效（插件被停用或卸载）。</summary>
	public bool IsPluginActionBroken => PluginActionBinding.IsReferenceBroken(Action);

	public bool IsHotkeyType => Type == "Hotkey";

	public bool IsLaunchType => Type == "Launch" || Type == "App";

	public bool IsWebUrlType => Type == "WebUrl" || Type == "Url";

	public bool IsFolderType => Type == "Folder" || Type == "OpenFolder";

	public bool IsCommandType => Type == "Command";

	public bool IsSystemType => Type == "System";

	public bool IsSwitchWindowType => Type == "SwitchWindow";

	public bool IsTileType => Type == "Tile";

	public bool IsOcrType => Type == "Ocr" || Type == "ScreenOcr";

	public bool IsShellToolType => Type == "ShellTool";

	public bool IsWindowManagerType =>
		Type == "Tile" || Type == "ToggleTopmost" || Type == "MoveMonitor" ||
		Type == "WindowOpacity" || Type == "SwitchWindow" || Type == "WindowManager";

	public List<ActionTypeOption> WindowManagerSubModes => new List<ActionTypeOption>
	{
		new ActionTypeOption { Tag = "Tile", DisplayText = "🔲 平铺窗口排布" },
		new ActionTypeOption { Tag = "TileCycle", DisplayText = "🔄 循环切换平铺" },
		new ActionTypeOption { Tag = "TileCycleBack", DisplayText = "⬅️ 反向循环平铺" },
		new ActionTypeOption { Tag = "TileRestore", DisplayText = "⏪ 还原平铺快照" },
		new ActionTypeOption { Tag = "ToggleTopmost", DisplayText = "📌 窗口置顶 / 取消置顶" },
		new ActionTypeOption { Tag = "MoveMonitor", DisplayText = "🖥️ 移到下一显示器" },
		new ActionTypeOption { Tag = "WindowOpacity", DisplayText = "👁️ 窗口透明度调节" },
		new ActionTypeOption { Tag = "SwitchWindow", DisplayText = "🗂️ 任务栏切换 (Win+N)" }
	};

	public string WindowManagerSubMode
	{
		get
		{
			if (Type == "ToggleTopmost") return "ToggleTopmost";
			if (Type == "MoveMonitor") return "MoveMonitor";
			if (Type == "WindowOpacity") return "WindowOpacity";
			if (Type == "SwitchWindow") return "SwitchWindow";
			if (Type == "Tile")
			{
				if (Parameter == WindowTiler.CycleParam) return "TileCycle";
				if (Parameter == WindowTiler.CycleBackParam) return "TileCycleBack";
				if (Parameter == WindowTiler.RestoreParam) return "TileRestore";
				return "Tile";
			}
			return "Tile";
		}
		set
		{
			if (value == "ToggleTopmost") { Type = "ToggleTopmost"; Parameter = ""; }
			else if (value == "MoveMonitor") { Type = "MoveMonitor"; Parameter = ""; }
			else if (value == "WindowOpacity") { Type = "WindowOpacity"; if (string.IsNullOrEmpty(Parameter)) Parameter = "80"; }
			else if (value == "SwitchWindow") { Type = "SwitchWindow"; if (string.IsNullOrEmpty(Parameter)) Parameter = "1"; }
			else if (value == "TileCycle") { Type = "Tile"; Parameter = WindowTiler.CycleParam; }
			else if (value == "TileCycleBack") { Type = "Tile"; Parameter = WindowTiler.CycleBackParam; }
			else if (value == "TileRestore") { Type = "Tile"; Parameter = WindowTiler.RestoreParam; }
			else if (value == "Tile") { Type = "Tile"; if (string.IsNullOrEmpty(Parameter) || Parameter.StartsWith("__")) Parameter = "2L"; }
			NotifyAllPropertiesChanged();
		}
	}

	public bool IsTileSubMode => Type == "Tile" && Parameter != WindowTiler.CycleParam && Parameter != WindowTiler.CycleBackParam && Parameter != WindowTiler.RestoreParam;
	public bool IsCycleSubMode => Type == "Tile" && (Parameter == WindowTiler.CycleParam || Parameter == WindowTiler.CycleBackParam);
	public bool IsRestoreSubMode => Type == "Tile" && Parameter == WindowTiler.RestoreParam;
	public bool IsTopmostSubMode => Type == "ToggleTopmost";
	public bool IsMoveMonitorSubMode => Type == "MoveMonitor";
	public bool IsOpacitySubMode => Type == "WindowOpacity";
	public bool IsSwitchWindowSubMode => Type == "SwitchWindow";

	public double WindowOpacityValue
	{
		get
		{
			if (double.TryParse(Parameter, out double v)) return Math.Clamp(v, 30, 100);
			return 80;
		}
		set
		{
			Parameter = Math.Round(value).ToString();
			OnPropertyChanged(nameof(WindowOpacityValue));
			OnPropertyChanged(nameof(WindowOpacityLabel));
			OnPropertyChanged(nameof(SummaryText));
		}
	}
	public string WindowOpacityLabel => $"{Math.Round(WindowOpacityValue)}%";

	public bool RunAsStandardUser
	{
		get => Action.RunAsStandardUser;
		set
		{
			if (Action.RunAsStandardUser != value)
			{
				Action.RunAsStandardUser = value;
				OnPropertyChanged(nameof(RunAsStandardUser));
			}
		}
	}

	public string BrowserChoice
	{
		get => Action.BrowserChoice ?? "Default";
		set
		{
			if (Action.BrowserChoice != value)
			{
				Action.BrowserChoice = value;
				OnPropertyChanged(nameof(BrowserChoice));
				OnPropertyChanged(nameof(IsCustomBrowser));
			}
		}
	}

	public bool IsCustomBrowser => string.Equals(BrowserChoice, "Custom", StringComparison.OrdinalIgnoreCase);

	public string BrowserPath
	{
		get => Action.BrowserPath ?? "";
		set
		{
			if (Action.BrowserPath != value)
			{
				Action.BrowserPath = value;
				OnPropertyChanged(nameof(BrowserPath));
			}
		}
	}

	public string Parameter
	{
		get => Action.Parameter ?? "";
		set
		{
			if (Action.Parameter != value)
			{
				Action.Parameter = value;
				OnPropertyChanged(nameof(Parameter));
				OnPropertyChanged(nameof(SummaryText));
				OnPropertyChanged(nameof(SelectedSystemPreset));
				OnPropertyChanged(nameof(TileLayout));
				OnPropertyChanged(nameof(NthWindowIndex));
				OnPropertyChanged(nameof(WindowOpacityValue));
				OnPropertyChanged(nameof(WindowOpacityLabel));
				OnPropertyChanged(nameof(ShellToolTitle));
			}
		}
	}

	public string Arguments
	{
		get => Action.Arguments ?? "";
		set
		{
			if (Action.Arguments != value)
			{
				Action.Arguments = value;
				OnPropertyChanged(nameof(Arguments));
			}
		}
	}

	public string IconKey
	{
		get => Action.IconKey ?? "";
		set
		{
			if (Action.IconKey != value)
			{
				Action.IconKey = value;
				OnPropertyChanged(nameof(IconKey));
				OnPropertyChanged(nameof(IconDisplayText));
				OnPropertyChanged(nameof(HasVectorIcon));
				OnPropertyChanged(nameof(ShowVectorIcon));
				OnPropertyChanged(nameof(VectorIconData));
			}
		}
	}

	public string CustomIconSvg
	{
		get => Action.CustomIconSvg ?? "";
		set
		{
			if (Action.CustomIconSvg != value)
			{
				Action.CustomIconSvg = value;
				OnPropertyChanged(nameof(CustomIconSvg));
				OnPropertyChanged(nameof(IconDisplayText));
				OnPropertyChanged(nameof(HasVectorIcon));
				OnPropertyChanged(nameof(ShowVectorIcon));
				OnPropertyChanged(nameof(VectorIconData));
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
				OnPropertyChanged(nameof(InheritStatusLabel));
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

	public string InheritStatusLabel
	{
		get
		{
			if (HasInheritedAppIcon)
			{
				try
				{
					string fn = System.IO.Path.GetFileName(InheritAppIconPath);
					return !string.IsNullOrEmpty(fn) ? fn : "已关联程序图标";
				}
				catch
				{
					return "已关联程序图标";
				}
			}
			return "未关联 (显示默认动作图标)";
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
		get => Parameter;
		set
		{
			if (Parameter != value && !string.IsNullOrEmpty(value))
			{
				Parameter = value;
				SystemPresetItem systemPresetItem = SlotViewModel.SystemPresetList.FirstOrDefault((SystemPresetItem p) => p.Key == value);
				if (systemPresetItem != null)
				{
					if (string.IsNullOrEmpty(Name) || Name.StartsWith("子动作"))
					{
						Name = systemPresetItem.DefaultName;
					}
					if (string.IsNullOrEmpty(IconKey))
					{
						IconKey = systemPresetItem.DefaultIconKey;
					}
				}
				OnPropertyChanged(nameof(SelectedSystemPreset));
				OnPropertyChanged(nameof(Parameter));
				OnPropertyChanged(nameof(SummaryText));
			}
		}
	}

	public string ShellToolTitle
	{
		get
		{
			if (IsShellToolType && !string.IsNullOrEmpty(Parameter))
			{
				var tool = ShellActionPickerWindow.ShellTools?.FirstOrDefault(t => t.Id == Parameter || string.Equals(t.Verb, Parameter, StringComparison.OrdinalIgnoreCase));
				if (tool != null) return tool.Name;
			}
			return "未挑选功能 (点击挑选)";
		}
	}

	public List<ActionTypeOption> TileLayoutOptions
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

	public string TileLayout
	{
		get => Action.Parameter ?? "";
		set
		{
			if (Action.Parameter != value)
			{
				Action.Parameter = value;
				OnPropertyChanged(nameof(TileLayout));
				OnPropertyChanged(nameof(SummaryText));
			}
		}
	}

	public string NthWindowIndex
	{
		get => Action.Parameter ?? "";
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
				OnPropertyChanged(nameof(NthWindowIndex));
				OnPropertyChanged(nameof(SummaryText));
			}
		}
	}

	public List<ActionTypeItem> Terminals => SlotViewModel.LocalizedTerminals;

	public string CommandTerminal
	{
		get => Action.CommandTerminal ?? "cmd";
		set
		{
			if (Action.CommandTerminal != value && !string.IsNullOrEmpty(value))
			{
				Action.CommandTerminal = value;
				OnPropertyChanged(nameof(CommandTerminal));
			}
		}
	}

	public List<ActionTypeItem> ActionTypes => SlotViewModel.LocalizedActionTypes;

	public string TypeBadgeText
	{
		get
		{
			if (IsHotkeyType) return "⌨️ 快捷热键";
			if (IsLaunchType) return "🚀 启动程序";
			if (IsWebUrlType) return "🌐 打开网址";
			if (IsFolderType) return "📁 打开文件夹";
			if (IsCommandType) return "💻 运行命令";
			if (IsWindowManagerType)
			{
				if (Type == "ToggleTopmost") return "📌 窗口置顶";
				if (Type == "MoveMonitor") return "🖥️ 移到下一屏";
				if (Type == "WindowOpacity") return "👁️ 窗口透明度";
				if (Type == "SwitchWindow") return "🗂️ 切换任务栏";
				return "🔲 平铺窗口";
			}
			if (IsSystemType) return "⚙️ 系统控制";
			if (IsOcrType) return "✂️ 截屏识字";
			if (IsShellToolType) return "⚡ 右键工具";
			return Type;
		}
	}

	public string SummaryText
	{
		get
		{
			if (IsHotkeyType) return string.IsNullOrWhiteSpace(Parameter) ? "(未录入快捷键)" : Parameter;
			if (IsLaunchType) return string.IsNullOrWhiteSpace(Parameter) ? "(未选择程序)" : System.IO.Path.GetFileName(Parameter);
			if (IsWebUrlType) return string.IsNullOrWhiteSpace(Parameter) ? "(未输入网址)" : Parameter;
			if (IsFolderType) return string.IsNullOrWhiteSpace(Parameter) ? "(未选择文件夹)" : System.IO.Path.GetFileName(Parameter);
			if (IsCommandType) return string.IsNullOrWhiteSpace(Parameter) ? "(未输入命令)" : Parameter;
			if (IsWindowManagerType)
			{
				if (Type == "ToggleTopmost") return "置顶 / 取消置顶";
				if (Type == "MoveMonitor") return "移至下一显示器";
				if (Type == "WindowOpacity") return $"不透明度: {Parameter}%";
				if (Type == "SwitchWindow") return $"任务栏 #{Parameter} 槽位";
				if (Parameter == WindowTiler.CycleParam) return "循环切换平铺";
				if (Parameter == WindowTiler.CycleBackParam) return "反向循环平铺";
				if (Parameter == WindowTiler.RestoreParam) return "还原平铺快照";
				return WindowTiler.LayoutDisplayName(Parameter ?? "2L");
			}
			if (IsSystemType) return SlotViewModel.SystemPresetList.FirstOrDefault(p => p.Key == Parameter)?.DisplayName ?? Parameter;
			if (IsOcrType) return "Windows 本地 / 离线原生 OCR";
			if (IsShellToolType) return ShellToolTitle;
			return Parameter ?? "";
		}
	}

	public void NotifyAllPropertiesChanged()
	{
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(Type));
		OnPropertyChanged(nameof(AggregatedType));
		OnPropertyChanged(nameof(Parameter));
		OnPropertyChanged(nameof(Arguments));
		OnPropertyChanged(nameof(IconKey));
		OnPropertyChanged(nameof(CustomIconSvg));
		OnPropertyChanged(nameof(InheritAppIconPath));
		OnPropertyChanged(nameof(HasInheritedAppIcon));
		OnPropertyChanged(nameof(InheritedAppIcon));
		OnPropertyChanged(nameof(ShowVectorIcon));
		OnPropertyChanged(nameof(VectorIconData));
		OnPropertyChanged(nameof(IconDisplayText));
		OnPropertyChanged(nameof(InheritStatusLabel));
		OnPropertyChanged(nameof(IsHotkeyType));
		OnPropertyChanged(nameof(IsLaunchType));
		OnPropertyChanged(nameof(IsWebUrlType));
		OnPropertyChanged(nameof(IsFolderType));
		OnPropertyChanged(nameof(IsSystemType));
		OnPropertyChanged(nameof(IsCommandType));
		OnPropertyChanged(nameof(IsSwitchWindowType));
		OnPropertyChanged(nameof(IsTileType));
		OnPropertyChanged(nameof(IsOcrType));
		OnPropertyChanged(nameof(IsShellToolType));
		OnPropertyChanged(nameof(IsWindowManagerType));
		OnPropertyChanged(nameof(WindowManagerSubMode));
		OnPropertyChanged(nameof(IsTileSubMode));
		OnPropertyChanged(nameof(IsCycleSubMode));
		OnPropertyChanged(nameof(IsRestoreSubMode));
		OnPropertyChanged(nameof(IsTopmostSubMode));
		OnPropertyChanged(nameof(IsMoveMonitorSubMode));
		OnPropertyChanged(nameof(IsOpacitySubMode));
		OnPropertyChanged(nameof(IsSwitchWindowSubMode));
		OnPropertyChanged(nameof(WindowOpacityValue));
		OnPropertyChanged(nameof(WindowOpacityLabel));
		OnPropertyChanged(nameof(RunAsStandardUser));
		OnPropertyChanged(nameof(BrowserChoice));
		OnPropertyChanged(nameof(IsCustomBrowser));
		OnPropertyChanged(nameof(BrowserPath));
		OnPropertyChanged(nameof(SelectedSystemPreset));
		OnPropertyChanged(nameof(TileLayout));
		OnPropertyChanged(nameof(NthWindowIndex));
		OnPropertyChanged(nameof(CommandTerminal));
		OnPropertyChanged(nameof(ShellToolTitle));
		OnPropertyChanged(nameof(TypeBadgeText));
		OnPropertyChanged(nameof(SummaryText));
		OnPropertyChanged(nameof(IsExpanded));
		OnPropertyChanged(nameof(ExpandToggleText));
		OnPropertyChanged(nameof(ExpandToggleArrow));

		// 插件动作相关：让子下拉的可见性与选中值跟上类型变化。
		//
		// 这里**不通知** PluginActionOptions：它的 getter 每次求值都会重建集合视图，
		// 而本方法在一个动作被修改后会被反复调用。纳入全量通知的话，
		// 用户每选定一次动作都会立刻重建候选集并重设 ItemsSource。
		// 类型切换那处（AggregatedType 的 setter）已单独通知过它。
		OnPropertyChanged(nameof(IsPluginType));
		OnPropertyChanged(nameof(SelectedPluginActionFullId));
		OnPropertyChanged(nameof(IsPluginActionBroken));
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}
