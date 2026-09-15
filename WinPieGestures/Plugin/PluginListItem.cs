namespace WinPieGestures.Plugins;

/// <summary>
/// 插件管理页的列表项视图模型。
/// <para>
/// 有意<b>不</b>实现 <c>INotifyPropertyChanged</c>：每次刷新都整体重建列表，
/// 而不是增量更新单个字段。插件状态的变化几乎总是成组的 —— 加载失败会连带改变
/// 动作数、错误文案与按钮可见性，整体重建不会出现「状态只更新了一半」的中间态。
/// 实现变更通知反而会诱导后来者去做局部更新，得不偿失。
/// </para>
/// </summary>
internal sealed class PluginListItem
{
    public string PluginId { get; init; } = "";

    /// <summary>插件自称的名称。清单缺失时退化为插件 ID。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>形如 <c>v1.2.0</c>；未知版本时为空串，列表中不占位。</summary>
    public string VersionText { get; init; } = "";

    /// <summary>一行摘要：作者、动作数、声明的高风险能力。</summary>
    public string SummaryText { get; init; } = "";

    /// <summary>次级细节：安装路径、目标框架、摘要哈希、签名状态。</summary>
    public string DetailText { get; init; } = "";

    /// <summary>状态中文名，如「运行中」「已隔离」。</summary>
    public string StateText { get; init; } = "";

    /// <summary>状态图标，用于在列表里快速扫读。</summary>
    public string StatusGlyph { get; init; } = "";

    /// <summary>错误详情。为空表示健康。</summary>
    public string ErrorText { get; init; } = "";

    /// <summary>供 DataTrigger 判断是否显示错误行。</summary>
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);

    /// <summary>是否已启用（登记态，不是运行态）。</summary>
    public bool IsEnabled { get; init; }
}
