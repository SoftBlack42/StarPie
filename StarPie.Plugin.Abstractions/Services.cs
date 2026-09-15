namespace StarPie.Plugin;

/// <summary>
/// 契约违约异常。由宿主在插件调用注册 API 不合法时抛出。
/// <para>
/// 抛出后宿主会把这个插件整体标记为加载失败并卸载 —— 不做「部分注册」，
/// 避免留下一个状态半残、用户无法解释的插件。
/// </para>
/// </summary>
public sealed class PluginContractException : Exception
{
    public PluginContractException(string message) : base(message) { }
    public PluginContractException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>插件专属日志。宿主会自动加上 <c>[plugin:&lt;id&gt;]</c> 前缀并落盘到独立文件，同时做限流。</summary>
public interface IPluginLogger
{
    void Debug(string message);
    void Info(string message);
    void Warn(string message);
    void Error(string message, Exception? exception = null);

    /// <summary>该插件日志文件路径（便于在加载失败时提示用户去查看）。</summary>
    string LogFilePath { get; }
}

/// <summary>
/// 插件私有配置。宿主读写 <c>plugins\&lt;id&gt;\settings.json</c>，与主 <c>config.json</c> 完全隔离。
/// <para>键值一律用字符串，避免插件自定义类型进入持久化层。修改后需调用 <see cref="Save"/>。</para>
/// </summary>
public interface IPluginSettings
{
    string? Get(string key);
    void Set(string key, string? value);

    bool GetBool(string key, bool defaultValue = false);
    int GetInt(string key, int defaultValue = 0);
    double GetDouble(string key, double defaultValue = 0);

    /// <summary>把所有未保存的修改落盘（原子写）。</summary>
    void Save();
}

/// <summary>非侵入式通知服务。宿主优先走托盘气泡，没有托盘时降级为日志。</summary>
public interface INotificationService
{
    /// <summary>提示一条信息。<paramref name="message"/> 建议不超过 80 字。</summary>
    void Notify(string title, string message);
}

/// <summary>宿主环境信息（只读）。</summary>
public interface IHostInfo
{
    /// <summary>StarPie 主程序版本，例如 <c>1.7.4</c>。</summary>
    string HostVersion { get; }

    /// <summary>SDK 契约版本，例如 <c>1.0</c>。</summary>
    string ApiVersion { get; }

    /// <summary>当前界面语言代码，例如 <c>zh-CN</c> / <c>en</c>。</summary>
    string LanguageCode { get; }

    /// <summary>StarPie 是否以管理员权限运行。</summary>
    bool IsElevated { get; }

    /// <summary>是否为便携模式（插件根目录位于程序目录下）。</summary>
    bool IsPortable { get; }

    /// <summary>主程序可执行文件路径。单文件发布形态下可能为空。</summary>
    string HostExecutablePath { get; }
}

/// <summary>
/// 宿主事件订阅。<b>所有订阅都必须持有返回的 token 并在 <c>Shutdown</c> 中释放</b> ——
/// 这是可回收 ALC 能否真正卸载的决定性因素。
/// </summary>
public interface IPluginEvents
{
    /// <summary>界面语言切换。回调在 UI 线程触发，参数为新语言代码。</summary>
    IDisposable OnLanguageChanged(Action<string> handler);

    /// <summary>
    /// 轮盘即将呈现。回调<b>保证不在钩子线程</b>（宿主已切到 UI 线程），
    /// 但仍在呼出路径上，因此必须极快，禁止 IO。
    /// </summary>
    IDisposable OnWheelOpening(Action<ActionContext> handler);

    /// <summary>轮盘关闭后。回调在 UI 线程触发。</summary>
    IDisposable OnWheelClosed(Action handler);
}

/// <summary>UI 线程调度门面。插件若持有后台线程并需要触碰 UI，必须经此切回。</summary>
public interface IDispatcherFacade
{
    bool IsOnUiThread { get; }

    /// <summary>投递到 UI 线程（不等结果）。UI 线程不可用时静默忽略。</summary>
    void Post(Action action);

    /// <summary>在 UI 线程执行并等待完成。</summary>
    Task InvokeAsync(Action action);
}

/// <summary>
/// 宿主已验证的动作能力。插件做「发快捷键 / 启程序 / 开文件夹 / 操作剪贴板 / 开网址」时
/// <b>必须</b>走这里，禁止自己 P/Invoke <c>SendInput</c> 或 <c>Process.Start</c>。
/// <para>
/// 原因：主程序内部已解决硬件扫描码映射、修饰键 10~15ms 时延保持、扩展键标志、
/// Unicode 字符流注入、提权降权令牌等一堆坑。插件自己重写一遍不仅会踩坑，
/// 还可能因为与主程序的全局钩子互相干扰而形成死循环。
/// </para>
/// <para>
/// <b>线程约束</b>：只有 <see cref="ActionKind.Sequential"/> 类动作可以调用这些方法；
/// 后台类动作调用会造成输入序列与前台动作交叉，宿主不为此负责。
/// </para>
/// </summary>
public interface IHostActionInvoker
{
    /// <summary>发送组合键，写法如 <c>"Ctrl+Shift+G"</c>、<c>"Win+D"</c>、<c>"F5"</c>。返回是否成功下发。</summary>
    bool SendHotkey(string hotkey);

    /// <summary>以 Unicode 字符流逐字输入文本，规避输入法阻断。</summary>
    bool SendText(string text);

    /// <summary>启动程序或打开文档。<paramref name="runAsStandardUser"/> 为 true 时通过 Shell 令牌降权启动。</summary>
    bool Launch(string path, string arguments = "", bool runAsStandardUser = false);

    /// <summary>在资源管理器中打开文件夹（不存在则尝试创建）。</summary>
    bool OpenFolder(string folderPath);

    /// <summary>用指定浏览器打开网址。<paramref name="browserChoice"/> 取值 Default/Chrome/Edge/Firefox/Custom。</summary>
    bool OpenUrl(string url, string browserChoice = "Default", string? customBrowserPath = null);

    /// <summary>写入剪贴板（带回退重试）。</summary>
    bool SetClipboardText(string text);

    /// <summary>读取剪贴板文本；无文本内容时返回 null。</summary>
    string? GetClipboardText();
}
