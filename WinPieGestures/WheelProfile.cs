using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace WinPieGestures;

public class WheelProfile : INotifyPropertyChanged
{
	private string _processName = "Global";
	private string? _displayName;
	private string? _boundProcesses;

	public string ProcessName
	{
		get => _processName;
		set
		{
			if (_processName != value)
			{
				_processName = value;
				OnPropertyChanged(nameof(ProcessName));
				OnPropertyChanged(nameof(DisplayName));
				OnPropertyChanged(nameof(DisplayTitle));
			}
		}
	}

	/// <summary>方案友好显示名称（如：Photoshop 图像处理、剪映专业版、代码开发）</summary>
	public string? DisplayName
	{
		get => _displayName;
		set
		{
			if (_displayName != value)
			{
				_displayName = value;
				OnPropertyChanged(nameof(DisplayName));
				OnPropertyChanged(nameof(DisplayTitle));
			}
		}
	}

	/// <summary>绑定的目标程序进程名称列表（支持逗号或分号分隔，如 "photoshop.exe" 或 "chrome.exe, msedge.exe"）</summary>
	public string? BoundProcesses
	{
		get => _boundProcesses;
		set
		{
			if (_boundProcesses != value)
			{
				_boundProcesses = value;
				OnPropertyChanged(nameof(BoundProcesses));
				OnPropertyChanged(nameof(DisplayTitle));
			}
		}
	}

	/// <summary>下拉框或列表呈现标题（如 "剪映 (jianyingpro.exe)" 或 "Global (全局默认)"）</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public string DisplayTitle
	{
		get
		{
			if (string.Equals(ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
			{
				return "🌐 Global (全局默认)";
			}
			string name = !string.IsNullOrWhiteSpace(DisplayName) ? DisplayName.Trim() : ProcessName.Trim();
			string procs = !string.IsNullOrWhiteSpace(BoundProcesses) ? BoundProcesses.Trim() : ProcessName.Trim();
			if (string.Equals(name, procs, StringComparison.OrdinalIgnoreCase))
			{
				return $"🖥️ {name}";
			}
			return $"🖥️ {name} ({procs})";
		}
	}

	public int SectorCount { get; set; } = 8;

	public List<ActionItem> Actions { get; set; } = new List<ActionItem>();

	/// <summary>中心核心圆死区动作（在外甩脱离取消开启时，松开光标于中心死区触发）</summary>
	public ActionItem? CenterAction { get; set; }

	/// <summary>是否启用中心核心圆动作</summary>
	public bool EnableCenterAction { get; set; } = false;

	/// <summary>多层轮盘层级列表（支持无限多层独立配置）</summary>
	public List<WheelLayer> Layers { get; set; } = new List<WheelLayer>();

	/// <summary>当前活跃的轮盘层级索引（默认 0 = 第 1 层）</summary>
	public int ActiveLayerIndex { get; set; } = 0;

	/// <summary>本方案专属的唤醒触发键；为 null 表示跟随全局 AppConfig.Trigger。仅当前台进程命中本 profile 且此字段非 null 时生效。</summary>
	public TriggerConfig? Trigger { get; set; } = null;

	public event PropertyChangedEventHandler? PropertyChanged;

	protected void OnPropertyChanged(string propertyName)
	{
		PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
	}

	/// <summary>
	/// 确保 Layers 至少包含一个有效层；若旧配置未包含 Layers，自动无损迁移根属性为第 1 层；
	/// 校验并自愈各层动作完整性，防止因反序列化空列表导致层级覆盖与动作丢失。
	/// </summary>
	public void EnsureLayers()
	{
		if (Layers == null)
		{
			Layers = new List<WheelLayer>();
		}
		if (Layers.Count == 0)
		{
			WheelLayer layer0 = new WheelLayer
			{
				Name = "第 1 层",
				SectorCount = this.SectorCount > 0 ? this.SectorCount : 8,
				CenterAction = this.CenterAction?.Clone(),
				EnableCenterAction = this.EnableCenterAction,
				Actions = new List<ActionItem>()
			};
			if (this.Actions != null && this.Actions.Count > 0)
			{
				int sCount = layer0.SectorCount;
				int takeCount = Math.Min(this.Actions.Count, sCount);
				for (int i = 0; i < takeCount; i++)
				{
					layer0.Actions.Add(this.Actions[i]?.Clone() ?? new ActionItem { Type = "Hotkey", Name = $"动作 {i + 1}", Parameter = "" });
				}
				while (layer0.Actions.Count < sCount)
				{
					layer0.Actions.Add(new ActionItem { Type = "Hotkey", Name = $"动作 {layer0.Actions.Count + 1}", Parameter = "" });
				}
			}
			else
			{
				for (int i = 0; i < layer0.SectorCount; i++)
				{
					layer0.Actions.Add(new ActionItem { Type = "Hotkey", Name = $"动作 {i + 1}", Parameter = "" });
				}
			}
			Layers.Add(layer0);
		}
		else if (Layers.Count == 1 && (Layers[0].Actions == null || Layers[0].Actions.Count == 0 || Layers[0].Actions.All(a => string.IsNullOrEmpty(a.Type) || (a.Type == "Hotkey" && string.IsNullOrEmpty(a.Parameter) && string.IsNullOrEmpty(a.InheritAppIconPath)))))
		{
			// 自愈保护：若仅有第 1 层且层内动作全空，但根属性 Actions 中存在真实自定义配置，则无损同步给第 1 层
			if (this.Actions != null && this.Actions.Count > 0 && this.Actions.Any(a => !string.IsNullOrEmpty(a.Parameter) || !string.IsNullOrEmpty(a.InheritAppIconPath) || (a.SubActions != null && a.SubActions.Count > 0)))
			{
				Layers[0].Actions.Clear();
				int sCount = Layers[0].SectorCount;
				int takeCount = Math.Min(this.Actions.Count, sCount);
				for (int i = 0; i < takeCount; i++)
				{
					Layers[0].Actions.Add(this.Actions[i]?.Clone() ?? new ActionItem { Type = "Hotkey", Name = $"动作 {i + 1}", Parameter = "" });
				}
				while (Layers[0].Actions.Count < sCount)
				{
					Layers[0].Actions.Add(new ActionItem { Type = "Hotkey", Name = $"动作 {Layers[0].Actions.Count + 1}", Parameter = "" });
				}
			}
		}

		// 确保每一层均具备合法结构、有效扇区数与非空动作列表
		for (int l = 0; l < Layers.Count; l++)
		{
			var layer = Layers[l];
			if (string.IsNullOrWhiteSpace(layer.Name))
			{
				layer.Name = $"第 {l + 1} 层";
			}
			if (layer.SectorCount != 4 && layer.SectorCount != 8 && layer.SectorCount != 12)
			{
				layer.SectorCount = (this.SectorCount is 4 or 8 or 12) ? this.SectorCount : 8;
			}
			if (layer.Actions == null)
			{
				layer.Actions = new List<ActionItem>();
			}
			if (layer.Actions.Count > layer.SectorCount)
			{
				layer.Actions = layer.Actions.Take(layer.SectorCount).ToList();
			}
			while (layer.Actions.Count < layer.SectorCount)
			{
				layer.Actions.Add(new ActionItem
				{
					Type = "Hotkey",
					Name = $"动作 {layer.Actions.Count + 1}",
					Parameter = ""
				});
			}
			for (int a = 0; a < layer.Actions.Count; a++)
			{
				if (layer.Actions[a] == null)
				{
					layer.Actions[a] = new ActionItem { Type = "Hotkey", Name = $"动作 {a + 1}", Parameter = "" };
				}
				else
				{
					if (string.IsNullOrEmpty(layer.Actions[a].Type))
					{
						layer.Actions[a].Type = "Hotkey";
						if (string.IsNullOrEmpty(layer.Actions[a].Name))
						{
							layer.Actions[a].Name = $"动作 {a + 1}";
						}
					}
					layer.Actions[a].SubActions ??= new List<ActionItem>();
				}
			}
		}

		if (ActiveLayerIndex < 0 || ActiveLayerIndex >= Layers.Count)
		{
			ActiveLayerIndex = 0;
		}
		SyncRootPropertiesFromActiveLayer();
	}

	/// <summary>将当前活跃层的数据同步到根属性（保证旧有读取逻辑 100% 兼容）</summary>
	public void SyncRootPropertiesFromActiveLayer()
	{
		if (Layers != null && ActiveLayerIndex >= 0 && ActiveLayerIndex < Layers.Count)
		{
			WheelLayer current = Layers[ActiveLayerIndex];
			this.SectorCount = current.SectorCount;
			this.Actions = current.Actions;
			this.CenterAction = current.CenterAction;
			this.EnableCenterAction = current.EnableCenterAction;
		}
	}

	/// <summary>将根属性的变更同步写回当前活跃层</summary>
	public void SyncActiveLayerFromRootProperties()
	{
		if (Layers != null && ActiveLayerIndex >= 0 && ActiveLayerIndex < Layers.Count)
		{
			WheelLayer current = Layers[ActiveLayerIndex];
			current.SectorCount = this.SectorCount;
			current.Actions = this.Actions;
			current.CenterAction = this.CenterAction;
			current.EnableCenterAction = this.EnableCenterAction;
		}
	}

	public WheelLayer GetActiveLayer()
	{
		EnsureLayers();
		return Layers[ActiveLayerIndex];
	}

	public WheelProfile Clone(string newProcessName, string? newDisplayName = null, string? newBoundProcesses = null)
	{
		EnsureLayers();
		WheelProfile clone = new WheelProfile
		{
			ProcessName = newProcessName,
			DisplayName = newDisplayName ?? this.DisplayName,
			BoundProcesses = newBoundProcesses ?? (!string.IsNullOrWhiteSpace(this.BoundProcesses) ? this.BoundProcesses : (string.Equals(this.ProcessName, "Global", StringComparison.OrdinalIgnoreCase) ? "" : this.ProcessName)),
			SectorCount = this.SectorCount,
			EnableCenterAction = this.EnableCenterAction,
			CenterAction = this.CenterAction?.Clone(),
			ActiveLayerIndex = this.ActiveLayerIndex,
			Actions = new List<ActionItem>(),
			Layers = new List<WheelLayer>(),
			Trigger = this.Trigger?.Clone()
		};
		foreach (var layer in this.Layers)
		{
			clone.Layers.Add(layer.Clone());
		}
		clone.SyncRootPropertiesFromActiveLayer();
		return clone;
	}

	/// <summary>
	/// 判断某个动作项是否被用户真实配置过（非留空、非默认未命名占位符、非继承类型、非 None 禁用）
	/// </summary>
	public static bool IsActionConfigured(ActionItem? action)
	{
		if (action == null) return false;
		if (string.IsNullOrEmpty(action.Type)) return false;
		if (string.Equals(action.Type, "Inherit", StringComparison.OrdinalIgnoreCase)) return false;
		if (string.Equals(action.Type, "None", StringComparison.OrdinalIgnoreCase)) return false;

		// 检查是否仅为默认占位未配置热键
		if (string.Equals(action.Type, "Hotkey", StringComparison.OrdinalIgnoreCase))
		{
			bool hasParam = !string.IsNullOrWhiteSpace(action.Parameter);
			bool hasAppIcon = !string.IsNullOrWhiteSpace(action.InheritAppIconPath);
			bool hasSvg = !string.IsNullOrWhiteSpace(action.CustomIconSvg);
			bool hasIconKey = !string.IsNullOrWhiteSpace(action.IconKey);
			bool hasSub = action.SubActions != null && action.SubActions.Any(s => IsActionConfigured(s));
			bool hasCustomName = !string.IsNullOrWhiteSpace(action.Name) &&
				!action.Name.StartsWith("动作 ") &&
				!action.Name.Equals("快捷动作", StringComparison.OrdinalIgnoreCase) &&
				!action.Name.Equals("未命名动作", StringComparison.OrdinalIgnoreCase);
			if (!hasParam && !hasAppIcon && !hasSvg && !hasIconKey && !hasSub && !hasCustomName)
			{
				return false;
			}
		}

		return true;
	}

	/// <summary>
	/// 获取当前方案在指定槽位及子槽位上的最终生效动作（若当前专属方案未配置且开启了全局继承，则级联继承 Global 对应方位的动作）
	/// </summary>
	public ActionItem? GetEffectiveAction(int sectorIndex, int subSectorIndex = -1, WheelProfile? globalProfile = null)
	{
		if (sectorIndex < 0) return null;

		ActionItem? localAction = (Actions != null && sectorIndex < Actions.Count) ? Actions[sectorIndex] : null;

		// 1. 如果是 Global 方案自身，直接读取本地动作（只要不是 None 禁用即返回）
		if (string.Equals(ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
		{
			if (localAction == null) return null;
			if (string.Equals(localAction.Type, "None", StringComparison.OrdinalIgnoreCase)) return null;
			if (subSectorIndex >= 0)
			{
				if (localAction.SubActions != null && subSectorIndex < localAction.SubActions.Count)
				{
					var sub = localAction.SubActions[subSectorIndex];
					if (sub != null && !string.Equals(sub.Type, "None", StringComparison.OrdinalIgnoreCase))
					{
						return sub;
					}
				}
				return null;
			}
			return localAction;
		}

		// 2. 如果本地专属槽位配置了有效动作
		if (IsActionConfigured(localAction))
		{
			if (subSectorIndex >= 0)
			{
				if (localAction!.SubActions != null && subSectorIndex < localAction.SubActions.Count)
				{
					var sub = localAction.SubActions[subSectorIndex];
					if (IsActionConfigured(sub))
					{
						return sub;
					}
				}
				return null; // 本地专属已明确配置有效主动作，未配置的子槽位不串挂全局无关动作
			}
			else
			{
				return localAction;
			}
		}

		// 3. 如果本地明确指定为 None (禁用)，则直接返回 null，不继承全局
		if (localAction != null && string.Equals(localAction.Type, "None", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		// 4. 若开启了全局继承，且当前槽位留空，尝试回退继承 Global 方案对应槽位
		if (ConfigManager.CurrentConfig?.EnableGlobalInheritance == true)
		{
			globalProfile ??= ConfigManager.GetGlobalProfile();
			if (globalProfile != null && !ReferenceEquals(this, globalProfile) && globalProfile.Actions != null)
			{
				int targetGlobalIndex = sectorIndex;
				if (this.SectorCount != globalProfile.SectorCount && this.SectorCount > 0 && globalProfile.SectorCount > 0)
				{
					// 跨扇区数映射（如 4 键映射到 8 键十字正交方位）
					double myAngle = sectorIndex * (360.0 / this.SectorCount);
					targetGlobalIndex = (int)Math.Round(myAngle / (360.0 / globalProfile.SectorCount)) % globalProfile.SectorCount;
				}

				if (targetGlobalIndex >= 0 && targetGlobalIndex < globalProfile.Actions.Count)
				{
					ActionItem? globalAction = globalProfile.Actions[targetGlobalIndex];
					if (IsActionConfigured(globalAction))
					{
						if (subSectorIndex >= 0 && globalAction!.SubActions != null && subSectorIndex < globalAction.SubActions.Count)
						{
							var globalSub = globalAction.SubActions[subSectorIndex];
							if (IsActionConfigured(globalSub))
							{
								var clonedSub = globalSub.Clone();
								clonedSub.IsInherited = true;
								return clonedSub;
							}
						}
						else if (subSectorIndex < 0)
						{
							var cloned = globalAction!.Clone();
							cloned.IsInherited = true;
							return cloned;
						}
					}
				}
			}
		}

		return IsActionConfigured(localAction) ? localAction : null;
	}

	/// <summary>
	/// 获取中心死区生效动作（支持从 Global 继承）
	/// </summary>
	public ActionItem? GetEffectiveCenterAction(WheelProfile? globalProfile = null)
	{
		if (this.EnableCenterAction && this.CenterAction != null && !string.Equals(this.CenterAction.Type, "None", StringComparison.OrdinalIgnoreCase))
		{
			return this.CenterAction;
		}

		if (string.Equals(ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
		{
			return (this.EnableCenterAction && this.CenterAction != null && !string.Equals(this.CenterAction.Type, "None", StringComparison.OrdinalIgnoreCase)) ? this.CenterAction : null;
		}

		if (ConfigManager.CurrentConfig?.EnableGlobalInheritance == true)
		{
			globalProfile ??= ConfigManager.GetGlobalProfile();
			if (globalProfile != null && globalProfile.EnableCenterAction && globalProfile.CenterAction != null && !string.Equals(globalProfile.CenterAction.Type, "None", StringComparison.OrdinalIgnoreCase))
			{
				var cloned = globalProfile.CenterAction!.Clone();
				cloned.IsInherited = true;
				return cloned;
			}
		}

		return null;
	}

	public override string ToString()
	{
		return DisplayTitle;
	}
}
