using System.Collections.Generic;

namespace WinPieGestures;

/// <summary>
/// 主配置里的插件偏好段（<c>config.json</c> 的 <c>Plugins</c> 节点）。
/// <para>
/// <b>这里只放「用户偏好」</b>。插件的启用状态、版本、哈希、能力确认一律在
/// <c>plugin-data\registry.json</c>；失败计数与安全模式在 <c>plugin-data\health.json</c>。理由：
/// ① 主配置被钩子线程高频读取且写盘无锁，插件状态属于相对高频变更，混进去只会放大竞态；
/// ② 插件状态本质是「宿主运维数据」而非「用户偏好数据」；
/// ③ 独立文件才能单独备份或一键重置（重置插件系统 = 删 registry.json）。
/// </para>
/// <para>
/// <c>plugin-data</c> 是<b>可写宿主区</b>，默认在 <c>%LOCALAPPDATA%\StarPie\</c> 下；
/// 它与随包分发的<b>只读来源区</b>（<c>程序目录\plugin\</c>，只放待安装的 .dll）是两回事，
/// 详见 <see cref="WinPieGestures.Plugins.PluginPaths"/>。
/// </para>
/// </summary>
public class PluginsPreference
{
    /// <summary>插件系统总开关。关闭后不扫描、不加载，等于回到没有插件系统的状态。</summary>
    public bool EnablePluginSystem { get; set; } = true;

    /// <summary>
    /// 便携模式：<b>可写宿主区</b>改用「主程序目录\plugin-data」。存在 portable.flag 时自动开启。
    /// <para>注意它<b>不</b>影响只读来源区 —— <c>程序目录\plugin\</c> 永远固定。</para>
    /// </summary>
    public bool PortableMode { get; set; }

    /// <summary>
    /// 开发者模式：允许「只登记外部路径不复制文件」安装，便于附加调试器与快速迭代。
    /// 开启时插件页应常驻风险横幅。
    /// </summary>
    public bool DeveloperMode { get; set; }

    /// <summary>
    /// 附加扫描目录（0..N）。
    /// <para>
    /// <b>⚠️ 预留字段，宿主尚未接入任何消费方</b>：写进 <c>config.json</c> 不会有任何效果。
    /// 之所以留着而不删，是为了避免旧配置里已存在的字段被静默丢弃（改了名/删了字段，
    /// 用户回退旧版本时那份配置就成了未知字段）。真正接入时要一并补上来源区候选列表的
    /// 呈现方式与「同一 ID 多来源」的优先级规则，不能只是多读几个目录。
    /// </para>
    /// </summary>
    public List<string> ExtraScanDirectories { get; set; } = new List<string>();

    /// <summary>启动完成后是否预加载标了 <c>Preload</c> 的插件。默认关闭以守住内存红线。</summary>
    public bool PreloadOnStartup { get; set; }

    /// <summary>串行类插件动作的默认超时（秒）。单个插件可在描述符里覆盖。</summary>
    public int ActionTimeoutSeconds { get; set; } = 5;

    /// <summary>渲染类插件扩展点的单帧时间预算（毫秒）。P1 渲染形态使用。</summary>
    public int RenderFrameBudgetMs { get; set; } = 4;

    /// <summary>单个插件内存附加量超过该值时在插件页给出警示（MB）。</summary>
    public int MaxPluginMemoryWarningMb { get; set; } = 80;

    /// <summary>是否在列表里显示识别不通过的插件（关闭则只显示可用的）。</summary>
    public bool ShowIncompatiblePlugins { get; set; } = true;
}
