using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>插件运行时状态机。</summary>
internal enum PluginRuntimeState
{
    /// <summary>已安装但未启用。程序集未加载。</summary>
    Installed,

    /// <summary>正在加载。</summary>
    Loading,

    /// <summary>已启用且贡献点已生效。</summary>
    Active,

    /// <summary>运行期出错（尚未熔断）。</summary>
    Faulted,

    /// <summary>连续失败达到阈值，被自动禁用。</summary>
    Quarantined,

    /// <summary>启用失败。程序集已卸载。</summary>
    Failed,

    /// <summary>识别不通过。</summary>
    Incompatible,

    /// <summary>已停用，但 ALC 未能真正卸载，需要重启才能彻底生效。</summary>
    RequiresRestart,
}

/// <summary>
/// 单个插件的运行时句柄 —— 它把「磁盘上的一个插件目录」变成「一个可调用、可监控、可撤销的活体」。
/// <para>
/// <b>职责边界</b>：只负责自己这一份生命周期（加载 → 注册 → 运行 → 停用 → 卸载）与状态记录，
/// 不负责扫描全目录、不负责持久化、不负责界面。这三件事分别属于 Scanner / RegistryStore / UI。
/// </para>
/// <para>
/// <b>卸载能否成功，取决于这个类有没有把自己的引用摘干净。</b>实测结论：只要宿主仍持有插件实例，
/// 卸载 100% 失败；摘净后 5 个 ALC 仅需 1 轮 GC（1.6 ms）即可全部真正卸载。
/// 所以 <see cref="Unload"/> 里所有「置 null」都不是可选的清理工作，而是卸载的前提条件。
/// </para>
/// </summary>
internal sealed class PluginInstance
{
    private readonly object _gate = new();

    public PluginInstance(string pluginId, PluginRegistryEntry entry, PluginScanResult scan)
    {
        PluginId = pluginId;
        Entry = entry;
        Scan = scan;
        Logger = new PluginLogger(pluginId);
        Settings = new PluginSettings(pluginId);
    }

    public string PluginId { get; }

    public PluginRegistryEntry Entry { get; set; }

    public PluginScanResult Scan { get; set; }

    public PluginRuntimeState State { get; private set; } = PluginRuntimeState.Installed;

    public PluginLogger Logger { get; }

    public PluginSettings Settings { get; }

    /// <summary>最近一次错误（用于插件页与日志）。</summary>
    public string? LastError { get; private set; }

    /// <summary>停用后 ALC 未能卸载、需要重启才能彻底释放。</summary>
    public bool RequiresRestart { get; private set; }

    /// <summary>上一次加载耗时（毫秒），用于性能计数。</summary>
    public double LastLoadMs { get; private set; }

    /// <summary>本插件注册的动作数量（加载后有效）。</summary>
    public int ActionCount { get; private set; }

    /// <summary>
    /// 该插件是否为「外部路径登记」（开发者模式）：文件留在原处，宿主<b>不拥有</b>它的文件。
    /// </summary>
    public bool IsExternal => !string.IsNullOrWhiteSpace(Entry.ExternalPath);

    /// <summary>
    /// <b>宿主拥有</b>的安装目录（可安全删除、可改名挂起）。外部路径登记时返回空串。
    /// <para>
    /// 凡是「删除 / 改名 / 写入」这类会动到磁盘的操作，都必须走这个属性而<b>不能</b>走
    /// <see cref="Directory"/>：外部登记指向的是开发者自己的工程输出目录，
    /// 按 <see cref="Directory"/> 去删会把开发者的源码目录整棵删掉。
    /// </para>
    /// </summary>
    public string ManagedDirectory => IsExternal
        ? ""
        : Path.Combine(
            PluginPaths.Root,
            string.IsNullOrWhiteSpace(Entry.InstallPath) ? PluginId : Entry.InstallPath);

    /// <summary>
    /// 插件所在目录，<b>仅供展示</b>。外部路径登记时返回该 .dll 所在的目录。
    /// <para>
    /// 注意：这个属性历史上曾被拿去当「可删除的安装目录」用，而外部登记分支返回的其实是
    /// <b>dll 文件路径</b> —— 当时只是靠 <c>Directory.Exists(文件路径)</c> 恒为 false
    /// 才「恰好」没删错东西。现在语义已经拆开：展示用 <see cref="Directory"/>，
    /// 落盘操作用 <see cref="ManagedDirectory"/>。
    /// </para>
    /// </summary>
    public string Directory => IsExternal
        ? (Path.GetDirectoryName(Entry.ExternalPath!) ?? "")
        : ManagedDirectory;

    // ---- 加载后短暂持有的引用。停用时必须全部清空，否则 ALC 无法回收 ----
    private PluginLoadContext? _loadContext;
    private IStarPiePlugin? _plugin;
    private PluginContext? _pluginContext;
    private PluginEventService? _events;
    private PluginRegistrationSession? _session;
    private readonly List<IDisposable> _tokens = new();

    /// <summary>延迟卸载判定用的弱引用（见 <see cref="ScheduleDeferredUnloadProbe"/>）。</summary>
    private WeakReference? _unloadProbe;

    /// <summary>延迟判定是否已经跑过（无论结论是「已回收」还是「仍需重启」）。</summary>
    private volatile bool _unloadProbeDone = true;

    /// <summary>延迟判定已进行的轮次。</summary>
    private int _unloadProbeAttempt;

    /// <summary>本轮卸载是否已经给出过最终结论（保证回调只触发一次）。</summary>
    private bool _verdictFired;

    /// <summary>
    /// 卸载判定得出<b>最终</b>结论时回调，参数为「是否确认已回收」。
    /// <para>
    /// 宿主必须用这个回调来写日志、提示用户，<b>而不能在 <see cref="Unload"/> 返回时就下结论</b>——
    /// 那一刻调用栈往往还没展开，结论多半是错的（会把「已成功释放」误报成「需要重启」）。
    /// 回调可能运行在线程池的延迟判定线程上。
    /// </para>
    /// </summary>
    internal Action<bool>? UnloadVerdictFinalized { get; set; }

    /// <summary>延迟判定的一次性计时器。</summary>
    private System.Threading.Timer? _unloadProbeTimer;

    public bool IsLoaded => _plugin != null;

    public PluginActionRegistration[] OwnedActions { get; private set; } = Array.Empty<PluginActionRegistration>();

    private void SetState(PluginRuntimeState state)
    {
        lock (_gate)
        {
            State = state;
        }
    }

    // ------------------------------------------------------------------ 加载

    /// <summary>
    /// 加载并启用插件。全过程在调用线程上同步完成（UI 线程或首次被动作引用时的动作线程）。
    /// </summary>
    /// <param name="failureReason">失败原因（用户可读）。</param>
    public bool Load(out string failureReason)
    {
        failureReason = "";
        SetState(PluginRuntimeState.Loading);

        try
        {
            // ① 加载前重新静态识别一次：文件可能在上次识别之后被替换或损坏
            PluginScanResult scan = PluginScanner.ScanInstalledPlugin(Directory);
            if (!scan.Accepted)
            {
                LastError = scan.DescribeFailure();
                failureReason = LastError;
                SetState(PluginRuntimeState.Failed);
                PluginRegistryStore.MutateHealth(PluginId, h => { h.LastError = LastError; });
                return false;
            }
            Scan = scan;
            PluginManifest manifest = scan.Manifest!;

            if (string.IsNullOrWhiteSpace(scan.DllPath))
            {
                LastError = "未能定位插件主程序集。";
                failureReason = LastError;
                SetState(PluginRuntimeState.Failed);
                return false;
            }

            // ② 提交动作必须在「触碰任何插件代码」之前完成：它才是真正会执行不可信代码的那一步
            var stopwatch = Stopwatch.StartNew();

            _loadContext = new PluginLoadContext(PluginId, Directory, scan.DllPath);
            Assembly assembly = _loadContext.LoadFromAssemblyPath(Path.GetFullPath(scan.DllPath));

            // ③ 定位入口类型
            string entryTypeName = scan.EntryTypeFullName
                                   ?? manifest.EntryType
                                   ?? "";

            Type? entryType = assembly.GetType(entryTypeName, throwOnError: false, ignoreCase: false);
            if (entryType == null)
            {
                // 依次在已加载类型里模糊匹配，便于给出更有用的报错
                Type? found = null;
                try
                {
                    foreach (Type type in assembly.GetTypes())
                    {
                        if (typeof(IStarPiePlugin).IsAssignableFrom(type) && !type.IsAbstract)
                        {
                            found ??= type;
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    LastError = BuildTypeLoadError(ex);
                    failureReason = LastError;
                    Teardown();
                    SetState(PluginRuntimeState.Failed);
                    return false;
                }

                if (found == null)
                {
                    LastError = $"找不到入口类型 \"{entryTypeName}\"。";
                    failureReason = LastError;
                    Teardown();
                    SetState(PluginRuntimeState.Failed);
                    return false;
                }
                entryType = found;
            }

            // ④ 类型身份校验 —— 这一步是「共享程序集放行」是否真正生效的最终验证
            if (!typeof(IStarPiePlugin).IsAssignableFrom(entryType))
            {
                LastError =
                    $"入口类型 {entryType.FullName} 未实现 IStarPiePlugin。若插件本身确实实现了，" +
                    "则说明出现了 SDK 程序集类型身份分裂（插件目录里多半带了一份 StarPie.Plugin.Abstractions.dll）。";
                failureReason = LastError;
                Teardown();
                SetState(PluginRuntimeState.Failed);
                return false;
            }

            object? created;
            try
            {
                created = Activator.CreateInstance(entryType);
            }
            catch (Exception ex)
            {
                LastError = $"实例化入口类型失败：{ex.GetBaseException().Message}";
                failureReason = LastError;
                Teardown();
                SetState(PluginRuntimeState.Failed);
                return false;
            }

            // 运行时再次强转。若这里得到 null，就是最典型的「插件加载成功但什么都没注册」
            _plugin = created as IStarPiePlugin;
            if (_plugin == null)
            {
                LastError =
                    "入口类型无法转换为 IStarPiePlugin —— 典型的 SDK 类型身份分裂。" +
                    "请确认插件目录里没有自带 StarPie.Plugin.Abstractions.dll。";
                failureReason = LastError;
                Teardown();
                SetState(PluginRuntimeState.Failed);
                return false;
            }

            // ⑤ 构造上下文与服务
            string dataDirectory = PluginPaths.GetDataDirectory(PluginId);
            try
            {
                if (!System.IO.Directory.Exists(dataDirectory)) System.IO.Directory.CreateDirectory(dataDirectory);
            }
            catch
            {
                // 数据目录创建失败不阻断加载，插件自己写文件时会看到真实错误
            }

            var metadata = new PluginMetadata
            {
                Id = manifest.Id,
                Name = manifest.Name,
                Version = manifest.Version,
                Author = manifest.Author,
                Description = manifest.Description,
                Homepage = manifest.Homepage,
                License = manifest.License,
                ApiVersion = manifest.ApiVersion,
                Capabilities = manifest.ResolveCapabilities(),
                InstallDirectory = Directory,
                DataDirectory = dataDirectory,
            };

            _session = PluginHost.Catalog.BeginSession(PluginId);
            _events = new PluginEventService(PluginId);
            _pluginContext = new PluginContext(
                metadata, Directory, dataDirectory, _session, Logger, Settings, _events);

            // ⑥ 交给插件注册贡献点。这是唯一一次执行插件代码的初始化时机。
            try
            {
                _plugin.Initialize(_pluginContext);
            }
            catch (Exception ex)
            {
                LastError = $"Initialize 抛出异常：{ex.GetBaseException().Message}";
                failureReason = LastError;
                Logger.Error("Initialize 失败", ex);
                Teardown();
                SetState(PluginRuntimeState.Failed);
                PluginRegistryStore.MutateHealth(PluginId, h =>
                {
                    h.ConsecutiveFailures++;
                    h.LastError = LastError;
                });
                return false;
            }

            // ⑦ 原子提交贡献点：冲突即整体拒绝，不留半残状态
            if (!_session.Commit(out string conflictError))
            {
                LastError = $"贡献点注册冲突，已整体拒绝：{conflictError}";
                failureReason = LastError;
                Logger.Error(LastError);
                Teardown();
                SetState(PluginRuntimeState.Failed);
                return false;
            }

            OwnedActions = _session.StagedActions.ToArray();
            ActionCount = OwnedActions.Length;

            stopwatch.Stop();
            LastLoadMs = stopwatch.Elapsed.TotalMilliseconds;

            Logger.Info(
                $"已启用：v{manifest.Version}，入口 {entryType.FullName}，" +
                $"贡献点 动作 {_session.StagedActions.Count} / 图标 {_session.StagedIcons.Count} / 词条 {_session.StagedI18n.Count}，" +
                $"加载耗时 {LastLoadMs:F1} ms，宿主 {PluginManifestReader.HostVersion}");

            PluginRegistryStore.MutateHealth(PluginId, h =>
            {
                h.LoadCount++;
                h.ConsecutiveFailures = 0;
                h.LastError = null;
            });

            SetState(PluginRuntimeState.Active);
            return true;
        }
        catch (Exception ex)
        {
            LastError = $"加载时发生未预期异常：{ex.GetBaseException().Message}";
            failureReason = LastError;
            Logger.Error("加载失败", ex);
            Teardown();
            SetState(PluginRuntimeState.Failed);
            return false;
        }
    }

    private static string BuildTypeLoadError(ReflectionTypeLoadException ex)
    {
        var missing = new List<string>();
        foreach (Exception? loaderException in ex.LoaderExceptions)
        {
            if (loaderException?.Message is { Length: > 0 } message && !missing.Contains(message))
            {
                missing.Add(message);
            }
        }
        return "程序集类型加载失败，通常是缺少依赖：" + string.Join("；", missing);
    }

    // ------------------------------------------------------------------ 停用 / 卸载

    /// <summary>
    /// 停用插件：撤销贡献点 → 剪断订阅 → Shutdown → 卸载 ALC → 校验。
    /// <para>
    /// 顺序不能变：<b>先摘外面指向插件的引用，再让插件自己清理，最后才卸载</b>。
    /// </para>
    /// </summary>
    public void Unload()
    {
        // 每一步都刻意放进**独立的、禁止内联的**方法里，而不是写在本方法体里。
        //
        // 原因：Teardown() 会在本调用栈**仍然存活**的情况下做 GC 探测，而只要本帧里
        // 还留着任何一个插件侧对象（IStarPiePlugin / IActionContribution / 插件抛出的
        // 异常对象），它就是 GC 根，ALC 必然回收不掉，于是永远误报「需要重启」。
        // 把每步拆出去之后，这些局部变量随各自的栈帧一起消失，本帧保持干净。
        RevokeContributions();
        RevokeEventSubscriptions();
        RequestPluginShutdown();
        DisposeTokens();

        Teardown();

        OwnedActions = Array.Empty<PluginActionRegistration>();
        ActionCount = 0;

        SetState(PluginRuntimeState.Installed);
    }

    /// <summary>① 摘掉宿主侧指向插件的最大一批引用：全部贡献点。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RevokeContributions()
    {
        try
        {
            PluginHost.Catalog.RevokeAll(PluginId);
        }
        catch (Exception ex)
        {
            Logger.Error("撤销贡献点失败", ex);
        }
    }

    /// <summary>② 剪断事件订阅链（插件可能在订阅回调里闭包了整个插件对象）。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RevokeEventSubscriptions()
    {
        try
        {
            _events?.RevokeAll();
        }
        catch (Exception ex)
        {
            Logger.Error("撤销事件订阅失败", ex);
        }
    }

    /// <summary>
    /// ③ 让插件自己清理。必须幂等；抛异常也不影响后续卸载流程。
    /// <para>
    /// 注意 <c>catch (Exception ex)</c> 里的 <paramref name="ex"/> 也可能是插件自定义的
    /// 异常类型（即插件侧对象）——这正是本方法必须独立成帧、且不能内联的原因。
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void RequestPluginShutdown()
    {
        IStarPiePlugin? plugin = _plugin;
        if (plugin == null) return;

        try
        {
            plugin.Shutdown();
        }
        catch (Exception ex)
        {
            Logger.Error("Shutdown 抛出异常（已忽略，但可能影响 ALC 卸载）", ex);
        }
    }

    /// <summary>④ 释放插件拿到的注册 token（宿主兜底，避免插件忘记 Dispose）。</summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private void DisposeTokens()
    {
        foreach (IDisposable token in _tokens)
        {
            try { token.Dispose(); } catch { }
        }
        _tokens.Clear();
    }

    /// <summary>
    /// 把本实例持有的全部插件侧引用置空，并请求 ALC 卸载。
    /// <para>注意：这里必须把字段真的置 null，不能只调 Unload —— 变量本身也是根。</para>
    /// </summary>
    private void Teardown()
    {
        PluginLoadContext? context = _loadContext;

        // 先摘字段 —— 字段是根，局部变量同样也是根
        _plugin = null;
        _pluginContext = null;
        _events = null;
        _session = null;
        _loadContext = null;

        // 清掉上一次卸载留下的判定状态
        _unloadProbeTimer?.Dispose();
        _unloadProbeTimer = null;
        _unloadProbe = null;
        _unloadProbeAttempt = 0;
        _unloadProbeDone = true;
        _verdictFired = false;

        if (context == null)
        {
            // 从未加载过：没有 ALC 需要回收，直接给出最终结论
            FireUnloadVerdict(true);
            return;
        }

        var weak = new WeakReference(context);

        try
        {
            context.Unload();
        }
        catch (Exception ex)
        {
            Logger.Warn($"ALC.Unload 调用失败：{ex.Message}");
        }

        // 把本帧里最后一个指向 ALC 的局部变量置空。字段虽然已经摘了，但局部变量 context
        // 本身也是 GC 根，且在整个 Teardown 帧存活期间（含下方 try/catch 异常处理区间）
        // 都会被 JIT 保守地视为存活。不置空则 weak.IsAlive 必然为 true。
        context = null;

        if (WaitForCollection(weak))
        {
            RequiresRestart = false;
            FireUnloadVerdict(true);
            return;
        }

        // 快路径没回收 —— 但**不能就此下结论**。此刻本调用栈上可能仍有别的帧持有插件侧
        // 对象（例如插件管理页正拿着 IActionContribution 列表，用户从这个页面点的「停用」），
        // 那些引用会在栈展开后自然消失。所以这里只标记为「待定」，交给延迟判定去定论。
        RequiresRestart = true;
        _unloadProbe = weak;
        _unloadProbeDone = false;
        ScheduleDeferredUnloadProbe();
    }

    /// <summary>给出最终卸载结论（只触发一次），并同步刷新健康度落盘。</summary>
    private void FireUnloadVerdict(bool collected)
    {
        if (_verdictFired) return;
        _verdictFired = true;
        _unloadProbeDone = true;

        try
        {
            UnloadVerdictFinalized?.Invoke(collected);
        }
        catch (Exception ex)
        {
            Logger.Warn($"卸载结论回调异常：{ex.Message}");
        }

        try
        {
            FlushHealth();
        }
        catch
        {
        }
    }

    /// <summary>
    /// 在**不持有任何 ALC 引用**的独立栈帧里，验证 ALC 是否已被回收。
    /// <para>
    /// 三个约束缺一不可：
    /// </para>
    /// <list type="number">
    /// <item>参数只有 <see cref="WeakReference"/>，绝不把 ALC 本体传进来 —— 一旦传参，
    /// 这个参数就成了新栈帧里的 GC 根，探测必然失败。</item>
    /// <item><see cref="MethodImplOptions.NoInlining"/> 阻断内联 —— 若被内联回调用方，
    /// 就又会与那个方法的栈帧合并，等于白做。</item>
    /// <item>调用方自身必须干净（见 <see cref="Unload"/> 里把各步骤拆成独立方法的说明）。</item>
    /// </list>
    /// <para>
    /// 循环 3 轮是实测结论：摘净引用后 1~2 轮必定回收，给 3 轮留冗余。
    /// </para>
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool WaitForCollection(WeakReference weak)
    {
        for (int i = 0; i < 3 && weak.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
        }
        return !weak.IsAlive;
    }

    // ------------------------------------------------------------------ 延迟卸载判定

    /// <summary>
    /// 延迟判定各轮的时间点（毫秒）。
    /// <para>
    /// 第一轮刻意很短：绝大多数情况下调用栈在 <c>Disable</c> 返回后的瞬间就展开了，
    /// 没必要让用户等。后面两轮是给「调用链更长 / 中间插入过 Dispatcher 泵」的场景兜底。
    /// </para>
    /// </summary>
    private static readonly int[] DeferredProbeDelaysMs = { 250, 750, 2000 };

    /// <summary>
    /// 安排延迟判定。计时器回调跑在线程池线程上，其调用栈与触发卸载的那条调用链<b>毫无关系</b>，
    /// 因此不会被「调用方局部变量」污染 —— 这是本机制能得出正确结论的关键。
    /// </summary>
    private void ScheduleDeferredUnloadProbe()
    {
        _unloadProbeTimer = new System.Threading.Timer(
            static state => ((PluginInstance)state!).DeferredUnloadProbeTick(),
            this,
            DeferredProbeDelaysMs[0],
            Timeout.Infinite);
    }

    private void DeferredUnloadProbeTick()
    {
        bool done = false;

        try
        {
            WeakReference? weak = _unloadProbe;
            if (weak == null)
            {
                done = true;
                return;
            }

            if (WaitForCollection(weak))
            {
                // 结论被纠正：插件程序集确实已经回收，只是当初那一下调用栈还没展开完。
                _unloadProbe = null;
                RequiresRestart = false;
                Logger.Info("延迟判定：插件程序集已成功回收，无需重启即可生效。");
                FireUnloadVerdict(true);
                done = true;
                return;
            }

            _unloadProbeAttempt++;
            if (_unloadProbeAttempt < DeferredProbeDelaysMs.Length)
            {
                _unloadProbeTimer?.Change(DeferredProbeDelaysMs[_unloadProbeAttempt], Timeout.Infinite);
                return;
            }

            // 三轮都没回收 —— 这次是真的有引用残留
            RequiresRestart = true;
            Logger.Warn(
                "ALC 未能卸载（三轮延迟判定均未回收，引用确有残留）。常见原因：" +
                "插件订阅了宿主事件且未注销、创建了 WPF 视觉对象或静态缓存、" +
                "启动了未停止的线程或定时器。已标记为「重启后生效」。");
            FireUnloadVerdict(false);
            done = true;
        }
        catch (Exception ex)
        {
            Logger.Warn($"延迟卸载判定异常：{ex.Message}");
            FireUnloadVerdict(RequiresRestart == false);
            done = true;
        }
        finally
        {
            if (done)
            {
                _unloadProbeDone = true;
                _unloadProbeTimer?.Dispose();
                _unloadProbeTimer = null;
            }
        }
    }

    /// <summary>
    /// 机会式复查一次卸载结果（可在写健康度、打开插件页等时机调用）。
    /// </summary>
    public bool RecheckUnload()
    {
        WeakReference? weak = _unloadProbe;
        if (weak == null) return !RequiresRestart;

        if (WaitForCollection(weak))
        {
            _unloadProbe = null;
            _unloadProbeDone = true;
            RequiresRestart = false;
            FireUnloadVerdict(true);
        }
        return !RequiresRestart;
    }

    /// <summary>
    /// 等待延迟判定给出最终结论。供自检与诊断使用。
    /// </summary>
    /// <returns><c>true</c> 表示 ALC 已确认回收。</returns>
    public bool WaitForUnloadVerdict(int timeoutMs)
    {
        int waited = 0;
        while (!_unloadProbeDone && waited < timeoutMs)
        {
            Thread.Sleep(50);
            waited += 50;
        }
        RecheckUnload();
        return !RequiresRestart;
    }

    /// <summary>运行期标记故障（不卸载，只记状态与计数）。</summary>
    public void MarkFaulted(string error)
    {
        LastError = error;
        SetState(PluginRuntimeState.Faulted);
    }

    // ------------------------------------------------------------------ 运行期计数

    // 计数刻意留在内存里，**每次调用都写 health.json 是不可接受的**：
    // 动作最多每秒触发数十次，等于把磁盘当计数器用。
    // 落盘时机是「状态变化 + 宿主退出 + 每 50 次调用」这三种低频场景。
    private long _invokeCount;
    private long _invokeFailureCount;
    private long _totalInvokeMsBits;
    private int _consecutiveFailures;
    private int _windowFailures;
    private int _dirtyCounter;

    public long InvokeCount => Interlocked.Read(ref _invokeCount);
    public long InvokeFailureCount => Interlocked.Read(ref _invokeFailureCount);
    public int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);
    public int WindowFailures => Volatile.Read(ref _windowFailures);

    public double AverageInvokeMs
    {
        get
        {
            long count = InvokeCount;
            return count <= 0 ? 0 : BitConverter.Int64BitsToDouble(Interlocked.Read(ref _totalInvokeMsBits)) / count;
        }
    }

    /// <summary>连续失败达到该值时提示用户（尚未自动禁用）。</summary>
    public const int FaultThreshold = 3;

    /// <summary>连续失败达到该值时自动禁用（隔离）。</summary>
    public const int QuarantineThreshold = 5;

    /// <summary>
    /// 记录一次动作调用结果。返回 true 表示插件已被自动隔离，调用方应停止后续调用。
    /// </summary>
    public bool RecordInvoke(bool success, double elapsedMs, string? error)
    {
        Interlocked.Increment(ref _invokeCount);
        long previousBits;
        double accumulated;
        do
        {
            previousBits = Interlocked.Read(ref _totalInvokeMsBits);
            accumulated = BitConverter.Int64BitsToDouble(previousBits) + elapsedMs;
        }
        while (Interlocked.CompareExchange(
                   ref _totalInvokeMsBits,
                   BitConverter.DoubleToInt64Bits(accumulated),
                   previousBits) != previousBits);

        bool quarantine = false;

        if (success)
        {
            Volatile.Write(ref _consecutiveFailures, 0);
            if (LastError != null && State == PluginRuntimeState.Faulted)
            {
                SetState(PluginRuntimeState.Active);
                LastError = null;
            }
        }
        else
        {
            Interlocked.Increment(ref _invokeFailureCount);
            int consecutive = Interlocked.Increment(ref _consecutiveFailures);
            Interlocked.Increment(ref _windowFailures);
            LastError = error;

            if (consecutive >= QuarantineThreshold)
            {
                quarantine = true;
            }
            else if (consecutive >= FaultThreshold && State == PluginRuntimeState.Active)
            {
                SetState(PluginRuntimeState.Faulted);
            }
        }

        if (Interlocked.Increment(ref _dirtyCounter) >= 50)
        {
            Interlocked.Exchange(ref _dirtyCounter, 0);
            FlushHealth();
        }

        return quarantine;
    }

    /// <summary>把内存计数落到 <c>health.json</c>。低频调用，失败也不影响运行。</summary>
    public void FlushHealth()
    {
        try
        {
            long count = InvokeCount;
            long failures = InvokeFailureCount;
            double average = AverageInvokeMs;
            int consecutive = ConsecutiveFailures;
            int window = WindowFailures;
            string? lastError = LastError;
            bool requiresRestart = RequiresRestart;

            PluginRegistryStore.MutateHealth(PluginId, h =>
            {
                h.InvokeCount += count;
                h.InvokeFailureCount += failures;
                h.TotalInvokeMs += average * count;
                h.ConsecutiveFailures = consecutive;
                h.WindowFailures = window;
                h.LastError = lastError;
                h.RequiresRestart = requiresRestart;
            });

            // 已落盘的部分从内存计数里扣掉，避免重复累加
            Interlocked.Add(ref _invokeCount, -count);
            Interlocked.Add(ref _invokeFailureCount, -failures);
            Interlocked.Exchange(ref _totalInvokeMsBits, BitConverter.DoubleToInt64Bits(average * count));
        }
        catch (Exception ex)
        {
            Logger.Warn($"写入健康度数据失败：{ex.Message}");
        }
    }

    public void ResetFailureCounters()
    {
        Volatile.Write(ref _consecutiveFailures, 0);
        Volatile.Write(ref _windowFailures, 0);
    }

    /// <summary>连续失败达到 <see cref="QuarantineThreshold"/> 时的自动隔离。</summary>
    public void MarkQuarantined(string error)
    {
        LastError = error;
        SetState(PluginRuntimeState.Quarantined);
        Entry.Enabled = false;
    }

    public void MarkIncompatible(string error)
    {
        LastError = error;
        SetState(PluginRuntimeState.Incompatible);
    }

    public void RegisterToken(IDisposable token) => _tokens.Add(token);

    public void ClearError()
    {
        LastError = null;
        if (State == PluginRuntimeState.Failed || State == PluginRuntimeState.Faulted)
        {
            SetState(PluginRuntimeState.Installed);
        }
    }
}
