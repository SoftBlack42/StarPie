namespace StarPie.Plugin;

/// <summary>
/// 插件声明需要使用的宿主能力。
/// <para>
/// 语义是「<b>声明</b>」而非「<b>授权</b>」：.NET 进程内插件不存在真正的权限门（CAS 已废弃），
/// 宿主无法拦截插件直接调用 BCL。这个枚举的作用是让用户在安装确认页看到插件要做什么，
/// 并让审核者能对照源码检查声明是否诚实。请如实声明，不要为了少一次风险提示而漏报。
/// </para>
/// </summary>
[Flags]
public enum PluginCapability
{
    /// <summary>不使用任何需要声明的能力（纯计算 / 纯 UI 扩展）。</summary>
    None = 0,

    /// <summary>启动进程、执行命令。</summary>
    Process = 1 << 0,

    /// <summary>读写用户文件（插件私有目录之外）。</summary>
    FileSystem = 1 << 1,

    /// <summary>发起网络请求。</summary>
    Network = 1 << 2,

    /// <summary>读写系统剪贴板。</summary>
    Clipboard = 1 << 3,

    /// <summary>读写注册表。</summary>
    Registry = 1 << 4,

    /// <summary>注册全局输入钩子。首版禁止（请改用 IPluginEvents 由宿主代发事件）。</summary>
    GlobalHook = 1 << 5,

    /// <summary>打开自己的窗口 / 弹窗。</summary>
    Ui = 1 << 6,

    /// <summary>需要管理员权限才能正常工作（宿主已提权时插件会继承该权限）。</summary>
    Admin = 1 << 7,
}

/// <summary>宿主持有的插件元数据。由宿主从 manifest 解析后经 <see cref="IPluginContext.Me"/> 提供给插件。</summary>
public sealed class PluginMetadata
{
    /// <summary>全局唯一 ID，反向域名风格，例如 <c>com.example.pomodoro</c>。</summary>
    public string Id { get; init; } = "";

    /// <summary>显示名。</summary>
    public string Name { get; init; } = "";

    /// <summary>插件版本（语义化）。</summary>
    public string Version { get; init; } = "";

    /// <summary>作者或组织。</summary>
    public string Author { get; init; } = "";

    /// <summary>一句话简介。</summary>
    public string Description { get; init; } = "";

    /// <summary>项目主页，可为空。</summary>
    public string? Homepage { get; init; }

    /// <summary>SPDX 许可证标识。</summary>
    public string License { get; init; } = "";

    /// <summary>插件编译所依赖的 SDK 契约版本，例如 <c>1.0</c>。</summary>
    public string ApiVersion { get; init; } = "";

    /// <summary>插件声明的能力集合。</summary>
    public PluginCapability Capabilities { get; init; } = PluginCapability.None;

    /// <summary>插件安装目录（只读，宿主管理）。</summary>
    public string InstallDirectory { get; init; } = "";

    /// <summary>插件私有可写数据目录。</summary>
    public string DataDirectory { get; init; } = "";

    public override string ToString() => $"{Name} ({Id}) v{Version}";
}

/// <summary>
/// 插件动作在 <c>ActionItem</c> 中的引用地址。
/// <para>
/// 持久化形态与内置动作完全一致，宿主通过它把一条轮盘槽位指向某个插件的某个贡献点。
/// 保留为独立 POCO 而不是两个裸字符串，是为了让旧版本宿主反序列化后能整体识别为「不认识的对象」并安全降级。
/// </para>
/// </summary>
public sealed class PluginActionRef
{
    /// <summary>插件 ID。</summary>
    public string PluginId { get; set; } = "";

    /// <summary>贡献点短 ID（不含插件 ID 前缀），由 <see cref="ActionDescriptor.Id"/> 声明。</summary>
    public string ContributionId { get; set; } = "";

    /// <summary>宿主归一化后的完整贡献 ID，形如 <c>&lt;pluginId&gt;.&lt;contributionId&gt;</c>。</summary>
    public string FullId => string.IsNullOrEmpty(PluginId) ? ContributionId : $"{PluginId}.{ContributionId}";

    public bool IsValid => !string.IsNullOrWhiteSpace(PluginId) && !string.IsNullOrWhiteSpace(ContributionId);

    public PluginActionRef Clone() => new() { PluginId = PluginId, ContributionId = ContributionId };

    public override string ToString() => FullId;
}
