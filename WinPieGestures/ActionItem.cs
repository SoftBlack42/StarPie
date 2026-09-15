using System.Collections.Generic;

namespace WinPieGestures;

public class ActionItem
{
	public string Type { get; set; } = "Hotkey";

	public string Name { get; set; } = "快捷动作";

	public string Parameter { get; set; } = "";

	public string Arguments { get; set; } = "";

	public string IconKey { get; set; } = "";

	public string CustomIconSvg { get; set; } = "";

	/// <summary>Terminal used to run a "Command" action: "cmd", "powershell", "wsl" or "direct".</summary>
	public string CommandTerminal { get; set; } = "cmd";

	/// <summary>用于 "WebUrl" 动作的目标浏览器："Default" (系统默认), "Chrome", "Edge", "Firefox", "Custom"</summary>
	public string BrowserChoice { get; set; } = "Default";

	/// <summary>自定义浏览器可执行文件完整路径（当 BrowserChoice 为 "Custom" 时使用）</summary>
	public string BrowserPath { get; set; } = "";

	/// <summary>继承图标的本地程序路径（解耦执行动作与视觉图标，如快捷键继承 QQ.exe 图标）</summary>
	public string? InheritAppIconPath { get; set; } = "";

	/// <summary>是否以普通桌面用户常规权限启动（通过 Shell 令牌降权，解决高权限下外部文件无法拖入目标软件的问题）</summary>
	public bool RunAsStandardUser { get; set; } = false;

	/// <summary>独立排版模式覆盖："Inherit" (继承全局), "IconAndText", "IconOnly", "TextOnly"</summary>
	public string? LayoutMode { get; set; } = "Inherit";

	/// <summary>独立文字颜色覆盖：null 或 "" 表示继承全局，支持 "#RRGGBB" 或 "#AARRGGBB"</summary>
	public string? CustomTextColor { get; set; } = "";

	/// <summary>独立字体覆盖：null 或 "" 表示继承全局</summary>
	public string? CustomFontFamily { get; set; } = "";

	/// <summary>独立图标大小覆盖：null 或 <=0 表示继承全局</summary>
	public double? CustomIconSize { get; set; } = null;

	/// <summary>独立文字字号覆盖：null 或 <=0 表示继承全局</summary>
	public double? CustomFontSize { get; set; } = null;

	/// <summary>独立文字相对位置覆盖：null 或 "Inherit" 表示继承全局，"Below", "Above"</summary>
	public string? CustomTextPlacement { get; set; } = "Inherit";

	/// <summary>独立文字水平偏移覆盖：null 表示继承全局</summary>
	public double? CustomTextOffsetX { get; set; } = null;

	/// <summary>独立文字垂直偏移覆盖：null 表示继承全局</summary>
	public double? CustomTextOffsetY { get; set; } = null;

	/// <summary>运行时标记：是否继承自 Global 全局方案动作（不持久化到 JSON）</summary>
	[System.Text.Json.Serialization.JsonIgnore]
	public bool IsInherited { get; set; }

	/// <summary>
	/// 插件动作引用。当 <see cref="Type"/> 为 <c>"Plugin"</c> 时使用。
	/// <para>
	/// 插件动作统一持久化为 <c>Type="Plugin"</c> 而不是占用内置 type 字符串空间：
	/// 旧版主程序读到它会命中 <c>switch</c> 的无匹配分支 → 静默无操作，
	/// 而<b>不会</b>崩溃。这是选择 "Plugin" 而非复用内置 type 的核心原因。
	/// </para>
	/// </summary>
	public StarPie.Plugin.PluginActionRef? PluginActionRef { get; set; }

	/// <summary>
	/// 插件动作的参数（键值一律为字符串）。
	/// <para>
	/// 为什么不用插件自定义类型：① 配置由主程序用 System.Text.Json 序列化，插件类型不可序列化；
	/// ② 字符串 KV 能被任意未来版本安全读写；③ 强制插件在边界处做类型转换与校验，
	/// 天然形成「对外契约 vs 内部实现」的解耦；④ 规避巨型字符串进入 config.json 造成的 LOH 膨胀。
	/// </para>
	/// </summary>
	public Dictionary<string, string>? ExtensionData { get; set; }

	/// <summary>
	/// 未知字段兜底容器。让本字段所在的整个 ActionItem 在「旧版读出 → 保存」过程中
	/// 不会丢掉读不懂的键（详细理由见 AppConfig.Extras）。
	/// </summary>
	[System.Text.Json.Serialization.JsonExtensionData]
	public Dictionary<string, System.Text.Json.JsonElement>? Extras { get; set; }

	public List<ActionItem> SubActions { get; set; } = new List<ActionItem>();

	public ActionItem Clone()
	{
		ActionItem clone = new ActionItem
		{
			IsInherited = this.IsInherited,
			Type = this.Type,
			Name = this.Name,
			Parameter = this.Parameter,
			Arguments = this.Arguments,
			IconKey = this.IconKey,
			CustomIconSvg = this.CustomIconSvg,
			CommandTerminal = this.CommandTerminal,
			BrowserChoice = this.BrowserChoice,
			BrowserPath = this.BrowserPath,
			InheritAppIconPath = this.InheritAppIconPath,
			RunAsStandardUser = this.RunAsStandardUser,
			LayoutMode = this.LayoutMode,
			CustomTextColor = this.CustomTextColor,
			CustomFontFamily = this.CustomFontFamily,
			CustomIconSize = this.CustomIconSize,
			CustomFontSize = this.CustomFontSize,
			CustomTextPlacement = this.CustomTextPlacement,
			CustomTextOffsetX = this.CustomTextOffsetX,
			CustomTextOffsetY = this.CustomTextOffsetY,
			// 深拷贝插件引用与参数：浅拷贝会让「复制动作后改参数」连带改掉原动作
			PluginActionRef = this.PluginActionRef?.Clone(),
			ExtensionData = this.ExtensionData == null
				? null
				: new Dictionary<string, string>(this.ExtensionData, StringComparer.OrdinalIgnoreCase),
			SubActions = new List<ActionItem>()
		};
		if (this.SubActions != null)
		{
			foreach (var sub in this.SubActions)
			{
				clone.SubActions.Add(sub.Clone());
			}
		}
		return clone;
	}

	public override string ToString()
	{
		return Name;
	}
}
