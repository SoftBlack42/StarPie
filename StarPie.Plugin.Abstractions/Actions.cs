using System.Globalization;

namespace StarPie.Plugin;

/// <summary>
/// 动作的调度类别。这直接决定宿主把它放在哪条线程上跑，选错会导致轮盘卡顿或键盘事件乱序。
/// </summary>
public enum ActionKind
{
    /// <summary>
    /// <b>串行类</b>（默认）。会与前台窗口、输入、剪贴板交互的动作，例如发快捷键、输入文本、切窗口。
    /// 宿主在唯一的动作线程上顺序执行，因此任何阻塞都会卡住整个动作队列 —— 请务必让实现足够短。
    /// </summary>
    Sequential = 0,

    /// <summary>
    /// <b>后台类</b>。纯计算、网络请求、文件 IO、不需要与前台窗口交互的长任务。
    /// 宿主用线程池并发执行，<b>不会占用动作线程</b>，因此可以放心做秒级耗时的事情。
    /// </summary>
    Background = 1,
}

/// <summary>参数控件类型。宿主用它把 <see cref="ParameterField"/> 渲染成对应风格的输入控件。</summary>
public enum ParameterFieldType
{
    Text = 0,
    MultilineText = 1,
    Number = 2,
    Bool = 3,
    Folder = 4,
    File = 5,
    Enum = 6,
    Hotkey = 7,
    Color = 8,
}

/// <summary><see cref="ParameterFieldType.Enum"/> 的可选项。</summary>
public sealed class ParameterOption
{
    public string Value { get; init; } = "";

    /// <summary>显示文案（可直接写中文）。</summary>
    public string Label { get; init; } = "";

    /// <summary>可选的 i18n 短键；非空时优先于 <see cref="Label"/>。</summary>
    public string? LabelKey { get; init; }
}

/// <summary>
/// 动作参数表单的一个字段。
/// <para>
/// 插件<b>不提供 XAML</b>，只声明字段，由宿主用主程序既有的控件风格渲染。
/// 这样深色模式对比度、字体、圆角、内存占用全部由宿主统一保证，主程序改版也不会让插件界面错位。
/// </para>
/// </summary>
public sealed class ParameterField
{
    /// <summary>参数键。会被写入 <c>ActionItem.ExtensionData[Key]</c>，请勿包含空格与中文。</summary>
    public string Key { get; init; } = "";

    /// <summary>显示标签（可直接写中文）。</summary>
    public string Label { get; init; } = "";

    /// <summary>可选的 i18n 短键；非空时优先于 <see cref="Label"/>。</summary>
    public string? LabelKey { get; init; }

    public ParameterFieldType Type { get; init; } = ParameterFieldType.Text;

    /// <summary>默认值（一律用字符串表示，宿主按 <see cref="Type"/> 解释）。</summary>
    public string? DefaultValue { get; init; }

    /// <summary>是否必填。宿主在「测试 / 保存」前会拦截空值。</summary>
    public bool Required { get; init; }

    /// <summary>输入框占位提示。</summary>
    public string? Placeholder { get; init; }

    /// <summary>补充说明（显示在字段下方的小字）。</summary>
    public string? HelpText { get; init; }

    /// <summary><see cref="ParameterFieldType.Enum"/> 专用选项表。</summary>
    public IReadOnlyList<ParameterOption>? Options { get; init; }

    /// <summary>可选的校验正则（作用于最终字符串值）。</summary>
    public string? ValidationRegex { get; init; }

    /// <summary>最大长度（字符）。宿主还会强制 <see cref="PluginApi.MaxParameterValueLength"/> 上限。</summary>
    public int? MaxLength { get; init; }

    /// <summary><see cref="ParameterFieldType.Number"/> 的最小值。</summary>
    public double? Min { get; init; }

    /// <summary><see cref="ParameterFieldType.Number"/> 的最大值。</summary>
    public double? Max { get; init; }
}

/// <summary>插件动作的自描述信息。</summary>
public sealed class ActionDescriptor
{
    /// <summary>
    /// 插件内<b>短</b> ID（推荐小写驼峰，如 <c>startTimer</c>）。
    /// 宿主会归一化为全局唯一的 <c>&lt;pluginId&gt;.&lt;contributionId&gt;</c>。
    /// </summary>
    public string Id { get; init; } = "";

    /// <summary>显示名（可直接写中文）。</summary>
    public string DisplayName { get; init; } = "";

    /// <summary>可选的 i18n 短键；非空时优先于 <see cref="DisplayName"/>。</summary>
    public string? DisplayNameKey { get; init; }

    /// <summary>列表里的副标题 / 说明。</summary>
    public string? Description { get; init; }

    /// <summary>分类名，用于动作下拉分组，例如「效率工具」。留空归入「插件」。</summary>
    public string Category { get; init; } = "";

    /// <summary>
    /// 图标。支持两种形态：
    /// ① 主程序内置矢量图标 key（如 <c>Timer</c>）；
    /// ② 本插件通过 <see cref="IIconRegistry.RegisterSvg"/> 注册后返回的 key。
    /// </summary>
    public string? IconKey { get; init; }

    /// <summary>调度类别，默认串行。见 <see cref="ActionKind"/>。</summary>
    public ActionKind Kind { get; init; } = ActionKind.Sequential;

    /// <summary>
    /// 超时秒数。填 0 表示使用宿主默认（串行 5 秒 / 后台 30 秒）。
    /// 宿主会把超时通过 <see cref="System.Threading.CancellationToken"/> 传给实现，请务必响应取消。
    /// </summary>
    public int TimeoutSeconds { get; init; }
}

/// <summary>
/// 动作执行时的只读环境信息。由宿主填充，插件不得修改。
/// 这是插件了解「用户此刻在哪个程序里、鼠标在哪」的唯一合法来源。
/// </summary>
public sealed class ActionContext
{
    /// <summary>前台进程名（不含扩展名），例如 <c>chrome</c>。</summary>
    public string ForegroundProcessName { get; init; } = "";

    /// <summary>前台窗口标题。</summary>
    public string ForegroundWindowTitle { get; init; } = "";

    /// <summary>前台窗口句柄。</summary>
    public long ForegroundWindowHandle { get; init; }

    /// <summary>触发时鼠标屏幕坐标 X。</summary>
    public int CursorX { get; init; }

    /// <summary>触发时鼠标屏幕坐标 Y。</summary>
    public int CursorY { get; init; }

    /// <summary>StarPie 当前是否以管理员权限运行（插件同样继承该权限）。</summary>
    public bool IsElevated { get; init; }

    /// <summary>当前界面语言代码，如 <c>zh-CN</c>。</summary>
    public string LanguageCode { get; init; } = "zh-CN";

    public override string ToString() => $"{ForegroundProcessName} @({CursorX},{CursorY})";
}

/// <summary>动作执行入参。</summary>
public sealed class PluginActionInput
{
    /// <summary>宿主归一化后的完整贡献 ID。</summary>
    public string ContributionId { get; init; } = "";

    /// <summary>用户在参数表单里填的值，键为 <see cref="ParameterField.Key"/>。</summary>
    public IReadOnlyDictionary<string, string> Parameters { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>只读环境信息。</summary>
    public ActionContext Context { get; init; } = new();

    /// <summary>读取一个参数；不存在时返回 null。</summary>
    public string? Parameter(string key) =>
        Parameters.TryGetValue(key, out string? value) ? value : null;

    /// <summary>读取一个布尔参数，解析失败时返回 <paramref name="fallback"/>。</summary>
    public bool Bool(string key, bool fallback = false) =>
        bool.TryParse(Parameter(key), out bool v) ? v : fallback;

    /// <summary>
    /// 读取一个整数参数，解析失败时返回 <paramref name="fallback"/>。
    /// <para>用不变文化解析：宿主写入的是 <c>0.5</c> 这样的不变文化字面量，
    /// 若按系统区域设置解析，同一份配置在德语等以逗号作小数点的机器上会静默退回默认值。</para>
    /// </summary>
    public int Int(string key, int fallback = 0) =>
        int.TryParse(Parameter(key), NumberStyles.Integer, CultureInfo.InvariantCulture, out int v) ? v : fallback;

    /// <summary>读取一个浮点参数，解析失败时返回 <paramref name="fallback"/>。同样使用不变文化，理由见 <see cref="Int"/>。</summary>
    public double Double(string key, double fallback = 0) =>
        double.TryParse(Parameter(key), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : fallback;
}

/// <summary>动作执行结果。失败时请给出用户能直接看懂的中文原因，它会显示在轮盘关闭后的提示与插件日志里。</summary>
public sealed class ActionResult
{
    public bool Success { get; init; }

    /// <summary>给用户看的一句话说明。成功时可为空。</summary>
    public string? Message { get; init; }

    /// <summary>成功时是否静默（不打扰用户）。默认 true。</summary>
    public bool Silent { get; init; } = true;

    /// <summary>成功且静默。</summary>
    public static ActionResult Ok(string? message = null, bool silent = true) =>
        new() { Success = true, Message = message, Silent = silent };

    /// <summary>失败。请务必填写 <paramref name="message"/>。</summary>
    public static ActionResult Fail(string message) =>
        new() { Success = false, Message = message, Silent = false };

    /// <summary>无操作的成功结果。</summary>
    public static readonly ActionResult Empty = new() { Success = true, Silent = true };
}

/// <summary>
/// 一个可被挂到轮盘扇区上的插件动作。
/// <para>
/// 生命周期：<see cref="IActionRegistry.Register"/> 登记 → 用户每次触发轮盘时调用
/// <see cref="ExecuteAsync"/> → 宿主停用时自动撤销登记。同一个贡献点可能被<b>并发</b>调用
/// （串行类不会，后台类会），实现需要保证线程安全。
/// </para>
/// </summary>
public interface IActionContribution
{
    /// <summary>自描述信息。</summary>
    ActionDescriptor Descriptor { get; }

    /// <summary>参数表单声明。无参数请返回空列表，不要返回 null。</summary>
    IReadOnlyList<ParameterField> Parameters { get; }

    /// <summary>
    /// 校验参数。返回 <c>null</c> 或空字符串表示通过；否则返回给用户看的错误原因。
    /// 宿主会在「保存动作」与「执行前」各调用一次。
    /// </summary>
    string? Validate(IReadOnlyDictionary<string, string> parameters);

    /// <summary>
    /// 生成列表副标题 / 测试预览文本。<b>必须极快</b>（微秒级），不得做 IO 或网络请求，
    /// 因为它可能在设置页滚动时被高频调用。
    /// </summary>
    string Preview(IReadOnlyDictionary<string, string> parameters);

    /// <summary>
    /// 执行动作。
    /// <b>禁止</b>：调用 <c>MessageBox</c>、注册全局输入钩子、直接改写 StarPie 主配置、
    /// 阻塞超过 <see cref="ActionDescriptor.TimeoutSeconds"/>。
    /// 需要发快捷键 / 启程序 / 操作窗口时，请使用宿主注入的服务，而不是自己 P/Invoke。
    /// </summary>
    Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken);
}
