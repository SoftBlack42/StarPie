using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using WinPieGestures.Plugins;

namespace WinPieGestures;

/// <summary>手势映射编辑行视图模型。</summary>
public class GestureMappingViewModel : INotifyPropertyChanged
{
	public GestureMapping Mapping { get; }

	public GestureMappingViewModel(GestureMapping mapping)
	{
		Mapping = mapping;
	}

	/// <summary>可选图样清单（单段 8 + 常用双段/三段，多段以 "-" 分隔避免歧义）。</summary>
	public List<ActionTypeOption> PatternChoices => PatternOptions;

	public static List<ActionTypeOption> PatternOptions { get; } = new List<ActionTypeOption>
	{
		new ActionTypeOption { Tag = "U", DisplayText = "↑ 上" },
		new ActionTypeOption { Tag = "D", DisplayText = "↓ 下" },
		new ActionTypeOption { Tag = "L", DisplayText = "← 左" },
		new ActionTypeOption { Tag = "R", DisplayText = "→ 右" },
		new ActionTypeOption { Tag = "UL", DisplayText = "↖ 左上" },
		new ActionTypeOption { Tag = "UR", DisplayText = "↗ 右上" },
		new ActionTypeOption { Tag = "DL", DisplayText = "↙ 左下" },
		new ActionTypeOption { Tag = "DR", DisplayText = "↘ 右下" },
		new ActionTypeOption { Tag = "D-R", DisplayText = "下→右 (L)" },
		new ActionTypeOption { Tag = "R-D", DisplayText = "右→下 (Γ)" },
		new ActionTypeOption { Tag = "D-L", DisplayText = "下→左 (反L)" },
		new ActionTypeOption { Tag = "L-D", DisplayText = "左→下" },
		new ActionTypeOption { Tag = "U-R", DisplayText = "上→右" },
		new ActionTypeOption { Tag = "R-U", DisplayText = "右→上 (┘)" },
		new ActionTypeOption { Tag = "U-L", DisplayText = "上→左" },
		new ActionTypeOption { Tag = "L-U", DisplayText = "左→上 (└)" },
		new ActionTypeOption { Tag = "U-D", DisplayText = "上→下" },
		new ActionTypeOption { Tag = "D-U", DisplayText = "下→上" },
		new ActionTypeOption { Tag = "L-R", DisplayText = "左→右" },
		new ActionTypeOption { Tag = "R-L", DisplayText = "右→左" },
		new ActionTypeOption { Tag = "UL-R", DisplayText = "左上→右" },
		new ActionTypeOption { Tag = "UR-L", DisplayText = "右上→左" },
		new ActionTypeOption { Tag = "DL-R", DisplayText = "左下→右" },
		new ActionTypeOption { Tag = "DR-L", DisplayText = "右下→左" },
		new ActionTypeOption { Tag = "D-R-D", DisplayText = "下→右→下 (Z)" },
		new ActionTypeOption { Tag = "R-D-R", DisplayText = "右→下→右" },
		new ActionTypeOption { Tag = "D-L-D", DisplayText = "下→左→下 (反Z)" },
		new ActionTypeOption { Tag = "U-R-U", DisplayText = "上→右→上" },
		new ActionTypeOption { Tag = "U-L-U", DisplayText = "上→左→上" },
		new ActionTypeOption { Tag = "D-R-U", DisplayText = "下→右→上 (S)" },
		new ActionTypeOption { Tag = "D-L-U", DisplayText = "下→左→上 (反S)" },
		new ActionTypeOption { Tag = "L-D-L", DisplayText = "左→下→左" }
	};

	public List<ActionTypeItem> ActionTypes => SlotViewModel.AggregatedActionTypes;

	public List<ActionTypeItem> AggregatedActionTypes => SlotViewModel.AggregatedActionTypes;

	public string AggregatedType
	{
		get
		{
			string t = Mapping.Action.Type ?? "Hotkey";
			if (t == PluginActionBinding.TypeName)
			{
				// 类型下拉里插件动作只有一项，类型即自身。
				// 具体是哪一个动作由子下拉承载，不必再把贡献点 ID 编码进 Tag。
				return PluginActionBinding.TypeName;
			}
			if (t == "Tile" || t == "ToggleTopmost" || t == "MoveMonitor" || t == "WindowOpacity" || t == "SwitchWindow" || t == "WindowManager")
			{
				return "WindowManager";
			}
			return t;
		}
		set
		{
			if (!string.IsNullOrEmpty(value))
			{
				if (value == PluginActionBinding.TypeName)
				{
					// 只切类型，**刻意不清插件引用**：用户在内置类型与插件动作之间来回切换时，
					// 已配好的插件动作不应被清掉（改选具体动作是子下拉的事）。
					// 引用为空只表示「还没选过」，由子下拉的空状态提示去引导。
					Type = PluginActionBinding.TypeName;
				}
				else if (value == "WindowManager")
				{
					if (!IsWindowManagerType)
					{
						PluginActionBinding.Clear(Mapping.Action);
						Type = "Tile";
						if (string.IsNullOrEmpty(Mapping.Action.Parameter))
						{
							Mapping.Action.Parameter = "2L";
						}
					}
				}
				else
				{
					// 切回内置动作类型时必须清掉插件引用，否则会残留一个
					// 「Type 是内置类型、却还挂着插件引用」的混合状态。
					PluginActionBinding.Clear(Mapping.Action);
					Type = value;
				}
				OnPropertyChanged(nameof(AggregatedType));
				OnPropertyChanged(nameof(IsWindowManagerType));
				OnPropertyChanged(nameof(IsPluginType));
				OnPropertyChanged(nameof(PluginActionOptions));
				NotifyAllPropertiesChanged();
			}
		}
	}

	/// <summary>当前动作是否为插件动作（用于界面显示对应的编辑面板与子下拉）。</summary>
	public bool IsPluginType => Type == PluginActionBinding.TypeName;

	/// <summary>
	/// 子下拉的候选插件动作，已按插件分组（分组头即插件显示名，本身不可选中）。
	/// <para>每次求值都重建，以便新启用的插件立刻出现在自己的分组里。</para>
	/// </summary>
	public ICollectionView? PluginActionOptions => PluginActionBinding.BuildPluginActionView();

	/// <summary>
	/// 子下拉当前选中的插件动作全 ID。
	/// </summary>
	public string? SelectedPluginActionFullId
	{
		get => PluginActionBinding.ProjectSelectedAction(Mapping.Action);
		set
		{
			// ItemsSource 重建时下拉框会把 SelectedValue 置空 —— 那不是用户的意图。
			// 不忽略的话，每次刷新都会把用户配好的动作清掉。
			if (string.IsNullOrEmpty(value)) return;

			if (PluginActionBinding.ProjectSelectedAction(Mapping.Action) == value) return;

			if (PluginActionBinding.Apply(Mapping.Action, value))
			{
				OnPropertyChanged(nameof(SelectedPluginActionFullId));
				OnPropertyChanged(nameof(IsPluginActionBroken));
				NotifyAllPropertiesChanged();
			}
		}
	}

	/// <summary>
	/// 所引用的插件动作是否已失效（插件被停用或卸载）。
	/// <para>与「尚未选定」严格区分：这种情况必须显式提示，否则用户会以为自己的配置丢了。</para>
	/// </summary>
	public bool IsPluginActionBroken => PluginActionBinding.IsReferenceBroken(Mapping.Action);

	public bool IsWindowManagerType => 
		Type == "Tile" || Type == "ToggleTopmost" || Type == "MoveMonitor" || 
		Type == "WindowOpacity" || Type == "SwitchWindow" || Type == "WindowManager";

	public List<ActionTypeOption> WindowManagerSubModes => new List<ActionTypeOption>
	{
		new ActionTypeOption { Tag = "Tile", DisplayText = "平铺窗口排布" },
		new ActionTypeOption { Tag = "TileCycle", DisplayText = "🔄 循环切换平铺" },
		new ActionTypeOption { Tag = "TileRestore", DisplayText = "⏪ 还原平铺快照" },
		new ActionTypeOption { Tag = "ToggleTopmost", DisplayText = "📌 窗口置顶 / 取消置顶" },
		new ActionTypeOption { Tag = "MoveMonitor", DisplayText = "🖥️ 移动至下一显示器" },
		new ActionTypeOption { Tag = "WindowOpacity", DisplayText = "👻 窗口半透明度" },
		new ActionTypeOption { Tag = "SwitchWindow", DisplayText = "🔢 切换任务栏指定应用" }
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
				if (Mapping.Action.Parameter == WindowTiler.CycleParam) return "TileCycle";
				if (Mapping.Action.Parameter == WindowTiler.RestoreParam) return "TileRestore";
				return "Tile";
			}
			return "Tile";
		}
		set
		{
			if (value == "ToggleTopmost") { Type = "ToggleTopmost"; Mapping.Action.Parameter = ""; }
			else if (value == "MoveMonitor") { Type = "MoveMonitor"; Mapping.Action.Parameter = ""; }
			else if (value == "WindowOpacity") { Type = "WindowOpacity"; if (string.IsNullOrEmpty(Mapping.Action.Parameter)) Mapping.Action.Parameter = "80"; }
			else if (value == "SwitchWindow") { Type = "SwitchWindow"; if (string.IsNullOrEmpty(Mapping.Action.Parameter)) Mapping.Action.Parameter = "1"; }
			else if (value == "TileCycle") { Type = "Tile"; Mapping.Action.Parameter = WindowTiler.CycleParam; }
			else if (value == "TileRestore") { Type = "Tile"; Mapping.Action.Parameter = WindowTiler.RestoreParam; }
			else if (value == "Tile") { Type = "Tile"; if (string.IsNullOrEmpty(Mapping.Action.Parameter) || Mapping.Action.Parameter.StartsWith("__")) Mapping.Action.Parameter = "2L"; }
			OnPropertyChanged(nameof(WindowManagerSubMode));
			OnPropertyChanged(nameof(IsTileSubMode));
			OnPropertyChanged(nameof(IsOpacitySubMode));
			OnPropertyChanged(nameof(IsSwitchWindowSubMode));
			OnPropertyChanged(nameof(Parameter));
		}
	}

	public bool IsTileSubMode => Type == "Tile" && Mapping.Action.Parameter != WindowTiler.CycleParam && Mapping.Action.Parameter != WindowTiler.RestoreParam;
	public bool IsOpacitySubMode => Type == "WindowOpacity";
	public bool IsSwitchWindowSubMode => Type == "SwitchWindow";

	public void NotifyAllPropertiesChanged()
	{
		OnPropertyChanged(nameof(Type));
		OnPropertyChanged(nameof(AggregatedType));
		OnPropertyChanged(nameof(IsHotkeyType));
		OnPropertyChanged(nameof(IsLaunchType));
		OnPropertyChanged(nameof(IsWebUrlType));
		OnPropertyChanged(nameof(IsFolderType));
		OnPropertyChanged(nameof(IsSystemType));
		OnPropertyChanged(nameof(IsCommandType));
		OnPropertyChanged(nameof(IsSwitchWindowType));
		OnPropertyChanged(nameof(IsTileType));
		OnPropertyChanged(nameof(IsOcrType));
		OnPropertyChanged(nameof(CanInheritAppIcon));
		OnPropertyChanged(nameof(InheritAppIconPath));
		OnPropertyChanged(nameof(HasInheritedAppIcon));
		OnPropertyChanged(nameof(RunAsStandardUser));
		OnPropertyChanged(nameof(IsWindowManagerType));
		OnPropertyChanged(nameof(WindowManagerSubMode));
		OnPropertyChanged(nameof(IsTileSubMode));
		OnPropertyChanged(nameof(IsOpacitySubMode));
		OnPropertyChanged(nameof(IsSwitchWindowSubMode));
		OnPropertyChanged(nameof(Parameter));
		OnPropertyChanged(nameof(Name));
		OnPropertyChanged(nameof(SelectedSystemPreset));
		OnPropertyChanged(nameof(TileLayout));

		// 插件动作相关：让子下拉的可见性与选中值跟上类型变化。
		//
		// 注意**不要**在这里通知 PluginActionOptions：它的 getter 每次求值都会重建集合视图，
		// 而本方法在很多路径上被调用（包括用户刚在子下拉里选定动作那一下）。
		// 一旦纳入全量通知，用户选完动作就会立刻重建候选集并重设 ItemsSource。
		// 类型切换那处（AggregatedType 的 setter）已经单独通知过它了，那是唯一真正需要的时机。
		OnPropertyChanged(nameof(IsPluginType));
		OnPropertyChanged(nameof(SelectedPluginActionFullId));
		OnPropertyChanged(nameof(IsPluginActionBroken));
	}

	public string Pattern
	{
		get => Mapping.Pattern ?? "D";
		set
		{
			if (Mapping.Pattern != value && !string.IsNullOrEmpty(value))
			{
				Mapping.Pattern = value;
				OnPropertyChanged(nameof(Pattern));
			}
		}
	}

	public string Type
	{
		get => Mapping.Action.Type ?? "Hotkey";
		set
		{
			if (!string.IsNullOrEmpty(value) && Mapping.Action.Type != value)
			{
				Mapping.Action.Type = value;
				OnPropertyChanged(nameof(Type));
				OnPropertyChanged(nameof(IsHotkeyType));
				OnPropertyChanged(nameof(IsLaunchType));
				OnPropertyChanged(nameof(IsFolderType));
				OnPropertyChanged(nameof(IsSystemType));
				OnPropertyChanged(nameof(IsCommandType));
				OnPropertyChanged(nameof(IsSwitchWindowType));
				OnPropertyChanged(nameof(IsTileType));

				// 类型切换是子下拉**唯一**需要重建候选集的时机；
				// 其余路径（例如用户刚选定了一个动作）刻意不重建，见 NotifyAllPropertiesChanged 的说明。
				OnPropertyChanged(nameof(IsPluginType));
				OnPropertyChanged(nameof(PluginActionOptions));
				OnPropertyChanged(nameof(IsPluginActionBroken));
			}
		}
	}

	public bool IsHotkeyType => Type == "Hotkey";

	public bool IsLaunchType => Type == "Launch" || Type == "App";

	public bool IsWebUrlType => Type == "WebUrl" || Type == "Url";

	public bool IsFolderType => Type == "Folder" || Type == "OpenFolder";

	public bool IsSystemType => Type == "System";

	public bool IsCommandType => Type == "Command";

	public bool IsSwitchWindowType => Type == "SwitchWindow";

	public bool IsTileType => Type == "Tile";

	public bool IsOcrType => Type == "Ocr" || Type == "ScreenOcr";

	public bool CanInheritAppIcon => Type != "Launch" && Type != "App";

	public string? InheritAppIconPath
	{
		get => Mapping.Action.InheritAppIconPath;
		set
		{
			Mapping.Action.InheritAppIconPath = value;
			OnPropertyChanged(nameof(InheritAppIconPath));
			OnPropertyChanged(nameof(HasInheritedAppIcon));
		}
	}

	public bool HasInheritedAppIcon => !string.IsNullOrWhiteSpace(InheritAppIconPath);

	public bool RunAsStandardUser
	{
		get => Mapping.Action.RunAsStandardUser;
		set
		{
			Mapping.Action.RunAsStandardUser = value;
			OnPropertyChanged(nameof(RunAsStandardUser));
		}
	}

	/// <summary>平铺布局下拉（key → 显示名）。</summary>
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

	/// <summary>平铺布局（写入 Parameter）。</summary>
	public string TileLayout
	{
		get
		{
			return Mapping.Action.Parameter ?? "";
		}
		set
		{
			if (Mapping.Action.Parameter != value)
			{
				Mapping.Action.Parameter = value;
				OnPropertyChanged(nameof(TileLayout));
			}
		}
	}

	/// <summary>命令动作的终端选项。</summary>
	public List<ActionTypeItem> Terminals => SlotViewModel.LocalizedTerminals;

	public string CommandTerminal
	{
		get => Mapping.Action.CommandTerminal ?? "cmd";
		set
		{
			if (Mapping.Action.CommandTerminal != value && !string.IsNullOrEmpty(value))
			{
				Mapping.Action.CommandTerminal = value;
				OnPropertyChanged(nameof(CommandTerminal));
			}
		}
	}

	/// <summary>系统控制预设（Key ↔ Parameter）。</summary>
	public string SelectedSystemPreset
	{
		get => Parameter;
		set
		{
			if (Parameter != value && !string.IsNullOrEmpty(value))
			{
				Parameter = value;
				OnPropertyChanged(nameof(SelectedSystemPreset));
			}
		}
	}

	/// <summary>切换窗口的序号（仅数字 1~20）。</summary>
	public string NthWindowIndex
	{
		get => Parameter;
		set
		{
			string digits = string.IsNullOrEmpty(value) ? "" : new string(value.Where(char.IsDigit).ToArray());
			if (int.TryParse(digits, out int n))
			{
				n = Math.Max(1, Math.Min(20, n));
				digits = n.ToString();
			}
			if (Parameter != digits)
			{
				Parameter = digits;
				OnPropertyChanged(nameof(NthWindowIndex));
			}
		}
	}

	public string Parameter
	{
		get => Mapping.Action.Parameter ?? "";
		set
		{
			if (Mapping.Action.Parameter != value)
			{
				Mapping.Action.Parameter = value ?? "";
				OnPropertyChanged(nameof(Parameter));
			}
		}
	}

	public string Name
	{
		get => Mapping.Action.Name ?? "";
		set
		{
			if (Mapping.Action.Name != value)
			{
				Mapping.Action.Name = value ?? "";
				OnPropertyChanged(nameof(Name));
			}
		}
	}

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}
}