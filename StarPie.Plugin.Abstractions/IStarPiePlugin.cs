namespace StarPie.Plugin;

/// <summary>
/// 插件唯一入口。
/// <para>
/// <b>契约要点（务必遵守，否则会被宿主判定为加载失败）</b>：
/// </para>
/// <list type="number">
/// <item>必须有一个 <c>public</c> 的<b>无参构造函数</b>，且构造函数<b>不得有副作用</b>（不做 IO、不启线程、不弹窗）。</item>
/// <item>一个程序集内必须<b>恰好有一个</b>实现本接口的类型；多于一个时必须用 manifest 的 <c>entryType</c> 指定。</item>
/// <item><see cref="Initialize"/> 只做<b>注册</b>，不要做耗时操作 —— 它被调用时用户正在等界面响应。</item>
/// <item><see cref="Shutdown"/> 必须<b>幂等</b>（可能被调用两次），且必须释放全部订阅 token 与自建线程。</item>
/// </list>
/// <example>
/// 最小实现：
/// <code>
/// public sealed class MyPlugin : IStarPiePlugin
/// {
///     private IPluginContext? _ctx;
///
///     public void Initialize(IPluginContext context)
///     {
///         _ctx = context;
///         context.I18n.Register("greet.hello", "你好，世界", "Hello, world");
///         context.Actions.Register(new HelloAction(context));
///     }
///
///     public void Shutdown() => _ctx = null;
/// }
/// </code>
/// </example>
/// </summary>
public interface IStarPiePlugin
{
    /// <summary>
    /// 初始化并注册贡献点。只在启用插件时被调用一次。
    /// </summary>
    /// <param name="context">宿主注入的服务门面。</param>
    void Initialize(IPluginContext context);

    /// <summary>
    /// 停用插件：释放订阅 token、停止自建线程与定时器、清空静态引用。
    /// <b>必须幂等</b>，且不得抛异常（抛出的异常会被宿主吞掉并记录，但很可能导致 ALC 无法卸载）。
    /// </summary>
    void Shutdown();
}
