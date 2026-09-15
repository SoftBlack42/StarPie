namespace WinPieGestures;

/// <summary>
/// 「动作类型」下拉里的一项。
/// <para>
/// 类型下拉只承载<b>类型</b>：内置动作是各自一个类型（<c>"Hotkey"</c> / <c>"Ocr"</c>…），
/// 而全部插件动作共享同一个类型 <c>"Plugin"</c>。具体选了哪个插件动作由子下拉决定，
/// 见 <see cref="WinPieGestures.Plugins.PluginActionItem"/>。
/// </para>
/// </summary>
public class ActionTypeItem
{
	public string Tag { get; set; } = "";

	public string DisplayText { get; set; } = "";
}
