namespace WinPieGestures.Plugins;

/// <summary>
/// 「插件动作」子下拉里的一个候选项。
/// <para>
/// <b>为什么与 <see cref="ActionTypeItem"/> 分开</b>：动作类型下拉与插件动作下拉虽然都是
/// <c>ComboBox</c>，但承载的东西完全不同 —— 前者传递的是「动作类型」（一个稳定的短字符串），
/// 后者传递的是「某个插件的某个贡献点」（全 ID），而且后者需要<b>按插件分组显示</b>。
/// 硬塞进同一个 DTO 会让两边都长出用不到的字段。
/// </para>
/// </summary>
public sealed class PluginActionItem
{
    /// <summary>贡献点全 ID（<c>&lt;插件ID&gt;.&lt;短ID&gt;</c>）。这是下拉框的 <c>SelectedValue</c>。</summary>
    public string FullId { get; init; } = "";

    /// <summary>动作显示名（已按当前语言解析）。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>提供该动作的插件 ID。</summary>
    public string PluginId { get; init; } = "";

    /// <summary>
    /// 分组键 —— 下拉框按此字段切分区块，取值是<b>插件显示名</b>。
    /// <para>
    /// 用插件显示名而不是插件 ID：用户认得的是「屏幕亮度调节」，不是
    /// <c>com.example.screenbrightness</c>。两个插件恰好同名时才会退化成带 ID 的形式
    /// （见 <see cref="PluginActionBinding.BuildPluginActionItems"/>），
    /// 否则同名插件的动作会被合并进同一个区块里，用户无从分辨。
    /// </para>
    /// </summary>
    public string GroupName { get; init; } = "";
}
