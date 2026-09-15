using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>安装选项。由安装确认卡收集，体现「用户手动选择启用」的产品语义。</summary>
internal sealed class PluginInstallOptions
{
    /// <summary>安装后立即启用。默认 false —— 安装与启用是两个动作。</summary>
    public bool EnableAfterInstall { get; set; }

    /// <summary>目标插件已存在时是否覆盖。</summary>
    public bool OverwriteExisting { get; set; }

    /// <summary>用户是否勾选了「我已了解此插件将以 StarPie 当前权限在进程内运行」。</summary>
    public bool Acknowledged { get; set; }

    /// <summary>用户确认过的能力集合（写入 registry，用于升级时比对是否新增了高风险能力）。</summary>
    public List<string> AcknowledgedCapabilities { get; set; } = new();

    /// <summary>开发者模式：只登记外部路径，不复制文件（便于附加调试器与热重载）。</summary>
    public bool DeveloperExternalPath { get; set; }

    /// <summary>
    /// 写进 <c>registry.json</c> 的安装来源：<c>UserSelectedFile</c>（文件对话框）/
    /// <c>ScanDirectory</c>（只读扫描目录）。
    /// <para>用途只有一个：日后排查「这个插件是怎么进来的」。不做任何逻辑分支。</para>
    /// </summary>
    public string SourceKind { get; set; } = "UserSelectedFile";
}

internal sealed class PluginInstallResult
{
    public bool Success { get; init; }
    public string PluginId { get; init; } = "";
    public string Error { get; init; } = "";
    public bool Enabled { get; init; }
}

/// <summary>
/// 插件系统门面 —— 主程序与插件世界之间<b>唯一</b>的对外入口。
/// <para>
/// 除 <see cref="PluginHost"/> 之外的宿主模块（Scanner / Loader / Catalog / Invoker / RegistryStore）
/// 全部是 <c>internal</c> 且不对外暴露。这样做的目的是把「主程序需要改动的面」压到最小：
/// <c>ActionExecutor</c> 只需要认识这一个类型的一个方法。
/// </para>
/// </summary>
internal static class PluginHost
{
    /// <summary>贡献点注册表。全局唯一实例。</summary>
    public static readonly PluginCatalog Catalog = new();

    private static readonly object Gate = new();
    private static readonly Dictionary<string, PluginInstance> Instances = new(StringComparer.OrdinalIgnoreCase);

    private static bool _initialized;
    private static bool _enabled = true;
    private static bool _developerMode;
    private static PluginsPreference _preferences = new();

    /// <summary>安全模式：启动时若判定上次是插件导致的崩溃，本次不加载任何插件。</summary>
    private static bool _safeModeActive;

    /// <summary>
    /// 无界面模式（<c>--plugin-selftest</c> / <c>--plugin-paths</c>）：跑完即退，不参与
    /// 启动健康记账。必须在 <see cref="Initialize"/> 之前置位。
    /// </summary>
    public static bool HeadlessMode { get; set; }

    /// <summary>托盘气泡注入点。UI 层设置后插件通知即可显示为气泡。</summary>
    public static Action<string, string>? NotificationSink
    {
        get => PluginNotificationHub.Sink;
        set => PluginNotificationHub.Sink = value;
    }

    public static bool IsInitialized => _initialized;
    public static bool IsEnabled => _enabled;
    public static bool IsSafeModeActive => _safeModeActive;
    public static bool IsDeveloperMode => _developerMode;

    /// <summary>已安装插件数量（不含被忽略的目录）。</summary>
    public static int InstalledCount
    {
        get { lock (Gate) return Instances.Count; }
    }

    // ------------------------------------------------------------------ 生命周期

    /// <summary>
    /// 初始化插件系统。必须在主程序启动早期、且**不阻塞首帧**的前提下调用。
    /// <para>
    /// 这里只做三件廉价的事：解析路径、清理残留、纯静态扫描清单。<b>不加载任何程序集</b>，
    /// 因此零插件用户的启动开销与接入插件系统之前完全一致（守住 R1 内存与启动红线）。
    /// </para>
    /// </summary>
    public static void Initialize()
    {
        if (_initialized) return;
        _initialized = true;

        try
        {
            _preferences = ConfigManager.CurrentConfig?.Plugins ?? new PluginsPreference();
            _enabled = _preferences.EnablePluginSystem;
            _developerMode = _preferences.DeveloperMode;

            PluginPaths.Configure(_preferences.PortableMode);

            if (!_enabled)
            {
                AppLogger.LogInfo("[plugin] 插件系统已在设置中关闭，跳过初始化。");
                return;
            }

            if (!PluginPaths.EnsureDirectories())
            {
                AppLogger.LogWarn($"[plugin] 插件目录创建失败，插件系统将不可用：{PluginPaths.Root}");
            }

            PluginLogger.CleanOldPluginLogs();
            CleanupPendingDeletions();

            CheckSafeMode();

            int discovered = SyncFromDisk();
            AppLogger.LogInfo(
                $"[plugin] 插件系统就绪：宿主区={PluginPaths.Root}，扫描目录={PluginPaths.ScanRoot}" +
                $"（存在={PluginPaths.ScanRootExists}），已登记 {Instances.Count} 个插件" +
                $"（本次扫描新发现 {discovered} 个），安全模式={_safeModeActive}");

            if (!_safeModeActive && _preferences.PreloadOnStartup)
            {
                SchedulePreload();
            }

            if (!HeadlessMode)
            {
                ScheduleStartupHealthCheck();
            }
        }
        catch (Exception ex)
        {
            // 插件系统初始化失败绝不能影响主程序启动
            AppLogger.LogError("[plugin] 插件系统初始化失败（已降级为「无插件」运行）", ex);
            _enabled = false;
        }
    }

    /// <summary>宿主退出前的收尾：停用全部插件并落盘健康度。</summary>
    public static void ShutdownAll()
    {
        if (!_initialized || !_enabled) return;

        List<PluginInstance> snapshot;
        lock (Gate)
        {
            snapshot = new List<PluginInstance>(Instances.Values);
        }

        foreach (PluginInstance instance in snapshot)
        {
            try
            {
                if (!instance.IsLoaded) continue;
                instance.Unload();
                instance.FlushHealth();
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 退出时停用 {instance.PluginId} 失败", ex);
            }
        }
    }

    // ------------------------------------------------------------------ 安装

    /// <summary>
    /// 第一步：识别用户手动选择的 <c>.dll</c>。只读元数据，不执行任何插件代码。
    /// 返回结果即是「安装确认卡」要展示的全部内容。
    /// </summary>
    public static PluginScanResult PrepareInstall(string dllPath)
    {
        try
        {
            return PluginScanner.ScanSelectedDll(dllPath);
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 识别所选文件时发生未预期异常", ex);
            var result = new PluginScanResult { DllPath = dllPath, Accepted = false };
            return result;
        }
    }

    /// <summary>
    /// 第二步：用户确认后落盘。复制到插件目录并登记为 <b>Disabled</b>。
    /// <para>注意：这里<b>不会加载程序集</b> —— 「安装」与「启用」刻意分成两个动作。</para>
    /// </summary>
    public static PluginInstallResult CommitInstall(PluginScanResult scan, PluginInstallOptions options)
    {
        options ??= new PluginInstallOptions();

        if (scan?.Manifest == null || !scan.Accepted)
        {
            return new PluginInstallResult { Success = false, Error = "识别未通过，无法安装。" };
        }

        if (!options.Acknowledged)
        {
            return new PluginInstallResult { Success = false, Error = "需要先勾选风险确认才能安装。" };
        }

        PluginManifest manifest = scan.Manifest;

        lock (Gate)
        {
            try
            {
                string installPath = manifest.Id;
                string targetDirectory = Path.Combine(PluginPaths.Root, installPath);
                bool alreadyExists = Directory.Exists(targetDirectory) || PluginRegistryStore.FindEntry(manifest.Id) != null;

                if (alreadyExists && !options.OverwriteExisting)
                {
                    return new PluginInstallResult
                    {
                        Success = false,
                        PluginId = manifest.Id,
                        Error = $"已存在同 ID 的插件（{manifest.Id}）。如需替换请勾选「覆盖已有插件」。",
                    };
                }

                // 已被加载的插件不允许直接覆盖文件，否则会得到「文件被占用」这种看不懂的报错
                PluginInstance? existing = Find(manifest.Id);
                if (existing != null && existing.IsLoaded)
                {
                    return new PluginInstallResult
                    {
                        Success = false,
                        PluginId = manifest.Id,
                        Error = "该插件正在运行，请先停用再覆盖安装。",
                    };
                }

                if (!options.DeveloperExternalPath)
                {
                    if (!CopyPayload(scan, targetDirectory, options.OverwriteExisting, out string copyError))
                    {
                        return new PluginInstallResult { Success = false, PluginId = manifest.Id, Error = copyError };
                    }
                }
                else if (!_developerMode)
                {
                    return new PluginInstallResult
                    {
                        Success = false,
                        Error = "「外部路径登记」需要先在插件页开启开发者模式。",
                    };
                }

                var entry = new PluginRegistryEntry
                {
                    Id = manifest.Id,
                    Name = manifest.Name,
                    Version = manifest.Version,
                    Description = manifest.Description,
                    Author = manifest.Author,
                    License = manifest.License,
                    Homepage = manifest.Homepage,
                    InstallPath = installPath,
                    ExternalPath = options.DeveloperExternalPath ? scan.DllPath : null,
                    Enabled = false,
                    Preload = false,
                    EntrySha256 = scan.Sha256,
                    SignerThumbprint = scan.SignerThumbprint,
                    CapabilitiesAck = new List<string>(options.AcknowledgedCapabilities),
                    AckedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                    AckedHostVersion = PluginManifestReader.HostVersion,
                    Source = options.DeveloperExternalPath ? "DeveloperPath" : options.SourceKind,
                    InstalledAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                };

                PluginRegistryStore.UpsertEntry(entry);

                var instance = new PluginInstance(manifest.Id, entry, scan);
                lock (Gate)
                {
                    Instances[manifest.Id] = instance;
                }

                string source = scan.ManifestSource == "AssemblyMetadata" ? "（程序集元数据）" : "";
                AppLogger.LogInfo(
                    $"[plugin] 已安装 {manifest.Id} v{manifest.Version}{source}，" +
                    $"SHA256={scan.Sha256Short}，签名={scan.IsSigned}，能力={string.Join(",", entry.CapabilitiesAck)}");

                bool enabled = false;
                if (options.EnableAfterInstall)
                {
                    enabled = Enable(manifest.Id, out string enableError);
                    if (!enabled)
                    {
                        AppLogger.LogWarn($"[plugin] 安装后自动启用 {manifest.Id} 失败：{enableError}");
                    }
                }

                return new PluginInstallResult
                {
                    Success = true,
                    PluginId = manifest.Id,
                    Enabled = enabled,
                };
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 安装 {manifest.Id} 失败", ex);
                return new PluginInstallResult { Success = false, PluginId = manifest.Id, Error = ex.Message };
            }
        }
    }

    // ------------------------------------------------------------------ 启用 / 停用

    /// <summary>启用插件：加载 → 实例化 → Initialize → 贡献点提交。</summary>
    public static bool Enable(string pluginId, out string error)
    {
        error = "";

        if (!_enabled)
        {
            error = "插件系统已在设置中关闭。";
            return false;
        }

        PluginInstance? instance = Find(pluginId);
        if (instance == null)
        {
            error = $"插件未安装：{pluginId}";
            return false;
        }

        if (instance.IsLoaded)
        {
            return true;
        }

        lock (Gate)
        {
            if (!instance.Load(out string failure))
            {
                error = failure;
                return false;
            }

            instance.Entry.Enabled = true;
            PluginRegistryStore.UpsertEntry(instance.Entry);

            // 启用的插件需要重新注册它的事件订阅（Load 里已经通过 Events 服务登记，无需额外动作）
            foreach (PluginActionRegistration action in instance.OwnedActions)
            {
                // 占位：注册 token 由 PluginContext 内部持有，这里只做日志
                _ = action;
            }
        }

        NotifyPluginSetChanged();
        return true;
    }

    /// <summary>停用插件：撤销贡献点 → 剪断订阅 → Shutdown → 尽力卸载 ALC。</summary>
    public static bool Disable(string pluginId, out string error)
    {
        error = "";
        PluginInstance? instance = Find(pluginId);
        if (instance == null)
        {
            error = $"插件未安装：{pluginId}";
            return false;
        }

        try
        {
            // 卸载结论是**异步**得出的：同步那一瞬间调用栈往往还没展开，此时下结论多半是错的。
            // 所以这里只登记回调，等最终结论出来再决定要不要提示用户「重启」。
            instance.UnloadVerdictFinalized = collected =>
            {
                if (collected) return;

                // 回调可能跑在线程池的延迟判定线程上，日志与托盘气泡都必须回到 UI 线程
                new PluginDispatcherFacade().Post(() =>
                {
                    AppLogger.LogWarn(
                        $"[plugin] {pluginId} 已停用，但插件程序集未能释放（ALC 卸载失败）；" +
                        "请重启 StarPie 以彻底回收其内存。");
                    NotifyUser(
                        "插件已停用，但内存未释放",
                        $"{pluginId} 的程序集仍被引用，重启 StarPie 后才能彻底回收。");
                });
            };

            instance.Unload();
            instance.FlushHealth();

            instance.Entry.Enabled = false;
            PluginRegistryStore.UpsertEntry(instance.Entry);

            NotifyPluginSetChanged();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLogger.LogError($"[plugin] 停用 {pluginId} 失败", ex);
            return false;
        }
    }

    /// <summary>卸载插件（停用 + 删除目录 + 移除登记）。</summary>
    public static bool Uninstall(string pluginId, bool removePluginData, out string error)
    {
        error = "";

        PluginInstance? instance = Find(pluginId);
        if (instance == null)
        {
            error = $"插件未安装：{pluginId}";
            return false;
        }

        Disable(pluginId, out _);

        try
        {
            // 外部路径登记：程序集留在开发者自己的目录里，宿主只拥有「登记」这一行数据。
            // 卸载必须只摘登记、绝不碰磁盘 —— 那条路径下往往就是开发者的编译输出目录。
            if (instance.IsExternal)
            {
                PluginRegistryStore.RemoveEntry(pluginId);
                lock (Gate)
                {
                    Instances.Remove(pluginId);
                }

                AppLogger.LogInfo(
                    $"[plugin] 已卸载 {pluginId}（外部路径登记，源文件未删除：{instance.Entry.ExternalPath}）");
                NotifyPluginSetChanged();
                return true;
            }

            string directory = instance.ManagedDirectory;
            if (removePluginData && Directory.Exists(directory))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                }
                catch (Exception deleteError)
                {
                    // 文件被占用（多为 ALC 未卸载干净）：改名挂起，下次启动时清理
                    string pending = Path.Combine(PluginPaths.Root, ".pending-delete-" + Guid.NewGuid().ToString("N"));
                    try
                    {
                        Directory.Move(directory, pending);
                        AppLogger.LogWarn(
                            $"[plugin] {pluginId} 目录被占用，已挂起删除，将于下次启动时清理：{deleteError.Message}");
                    }
                    catch
                    {
                        error = $"删除插件目录失败（文件被占用）：{deleteError.Message}。请重启 StarPie 后重试。";
                        return false;
                    }
                }
            }

            PluginRegistryStore.RemoveEntry(pluginId);
            lock (Gate)
            {
                Instances.Remove(pluginId);
            }

            AppLogger.LogInfo($"[plugin] 已卸载 {pluginId}（保留数据={!removePluginData}）");
            NotifyPluginSetChanged();
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            AppLogger.LogError($"[plugin] 卸载 {pluginId} 失败", ex);
            return false;
        }
    }

    // ------------------------------------------------------------------ 参数校验接缝

    private static readonly IReadOnlyDictionary<string, string> EmptyParameters =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 插件动作的参数校验结果。
    /// <para>
    /// 刻意把「宿主发现的声明违规」与「插件自己给的说法」分成两份：
    /// 前者能精确对应到某个字段，可以就地标红；后者只是一句话，只能整体展示。
    /// 混成一个字符串会丢掉字段定位能力。
    /// </para>
    /// </summary>
    public sealed class PluginActionValidation
    {
        /// <summary>违反 <see cref="ParameterField"/> 声明约束的字段。</summary>
        public List<PluginParameterIssue> DeclaredIssues { get; init; } = new();

        /// <summary><see cref="IActionContribution.Validate"/> 返回的原因。</summary>
        public string? PluginMessage { get; init; }

        public bool IsValid => DeclaredIssues.Count == 0 && string.IsNullOrEmpty(PluginMessage);

        /// <summary>压成一句给用户看的中文。</summary>
        public string? Describe()
        {
            if (DeclaredIssues.Count > 0) return DeclaredIssues[0].ToString();
            return string.IsNullOrEmpty(PluginMessage) ? null : PluginMessage;
        }
    }

    /// <summary>
    /// <b>插件动作参数校验的唯一入口。</b>
    /// <para>
    /// <see cref="IActionContribution.Validate"/> 的注释写着「宿主会在<b>保存动作</b>与<b>执行前</b>各调用一次」，
    /// 但如果两条路径各写一遍，它们迟早会分叉 —— 用户就会遇到
    /// 「保存时一切正常、触发时却说参数不合法」这种最令人困惑的状态。
    /// 因此两处都走这里，顺序固定为：先宿主底线（声明的约束），再插件自定义。
    /// </para>
    /// <para>
    /// 本方法<b>保证不抛异常</b>。
    /// </para>
    /// </summary>
    public static PluginActionValidation ValidateActionParameters(ActionItem? action)
    {
        if (action?.PluginActionRef == null || !action.PluginActionRef.IsValid)
        {
            return new PluginActionValidation();
        }

        try
        {
            if (!Catalog.TryGetAction(action.PluginActionRef.FullId, out PluginActionRegistration registration))
            {
                // 贡献点已不在目录里时不做参数校验。
                // 真正的问题是「这个动作已经不可用」，此时报参数错误会把用户引向完全错误的方向。
                return new PluginActionValidation();
            }

            IReadOnlyDictionary<string, string> parameters = action.ExtensionData ?? EmptyParameters;

            // ① 宿主底线：只认 ParameterField 声明的约束，不依赖插件是否记得自查。
            List<PluginParameterIssue> declaredIssues =
                PluginParameterValidator.Validate(registration.Parameters, parameters);

            // ② 插件自定义：处理声明表达不了的规则（例如「起止时间不能相同」）。
            string? pluginMessage = null;
            try
            {
                string? result = registration.Contribution.Validate(parameters);
                if (!string.IsNullOrWhiteSpace(result)) pluginMessage = result!.Trim();
            }
            catch (Exception ex)
            {
                AppLogger.LogError($"[plugin] 动作 {registration.FullId} 的参数校验抛出异常", ex);
                pluginMessage = $"插件自身的校验逻辑出错：{ex.GetBaseException().Message}（这是插件的问题，请反馈给插件作者）";
            }

            return new PluginActionValidation
            {
                DeclaredIssues = declaredIssues,
                PluginMessage = pluginMessage,
            };
        }
        catch (Exception ex)
        {
            // 校验器自己坏掉时放行。宁可让插件在执行里自行拒绝，
            // 也不要因为宿主这一环出错就让用户的手势彻底点不动。
            AppLogger.LogError("[plugin] 参数校验流程异常（已放行）", ex);
            return new PluginActionValidation();
        }
    }

    // ------------------------------------------------------------------ 执行接缝

    /// <summary>
    /// <b>主程序唯一的调用入口。</b><see cref="ActionExecutor"/> 在 <c>switch</c> 未命中时调用它。
    /// <para>
    /// 这个方法<b>保证不抛异常</b>，并且绝不把插件异常冒泡给 <see cref="ActionExecutor.Execute"/> ——
    /// 因为那里的 <c>catch</c> 会弹 <c>MessageBox</c>，在无人值守时会把动作线程卡死。
    /// </para>
    /// </summary>
    public static PluginExecuteOutcome ExecutePluginAction(ActionItem action)
    {
        if (action?.PluginActionRef == null || !action.PluginActionRef.IsValid)
        {
            return PluginExecuteOutcome.NotHandled;
        }

        try
        {
            PluginActionRef reference = action.PluginActionRef;
            string fullId = reference.FullId;

            if (!Catalog.TryGetAction(fullId, out PluginActionRegistration registration))
            {
                return new PluginExecuteOutcome
                {
                    Handled = true,
                    Success = false,
                    Message = $"动作「{action.Name}」所属的插件动作未注册：{fullId}。插件可能已被禁用或卸载。",
                };
            }

            PluginInstance? instance = Find(reference.PluginId);

            // 惰性加载：Enabled 但尚未加载（内存红线的代价就是首次调用要额外等一次加载）
            if (instance == null || !instance.IsLoaded)
            {
                if (instance == null)
                {
                    return new PluginExecuteOutcome
                    {
                        Handled = true,
                        Success = false,
                        Message = $"插件「{reference.PluginId}」未安装。",
                    };
                }

                if (!instance.Entry.Enabled)
                {
                    return new PluginExecuteOutcome
                    {
                        Handled = true,
                        Success = false,
                        Message = $"插件「{instance.Entry.Name}」当前未启用，请在「插件」页启用后再试。",
                    };
                }

                AppLogger.LogInfo($"[plugin] 首次引用触发惰性加载：{reference.PluginId}");
                if (!Enable(reference.PluginId, out string loadError))
                {
                    return new PluginExecuteOutcome
                    {
                        Handled = true,
                        Success = false,
                        Message = $"插件「{instance.Entry.Name}」加载失败：{loadError}",
                    };
                }

                if (!Catalog.TryGetAction(fullId, out registration))
                {
                    return new PluginExecuteOutcome
                    {
                        Handled = true,
                        Success = false,
                        Message = $"插件已加载，但没有注册动作 {fullId}。插件版本可能已变化，请重新编辑该槽位。",
                    };
                }
            }

            if (instance.State == PluginRuntimeState.Quarantined)
            {
                return new PluginExecuteOutcome
                {
                    Handled = true,
                    Success = false,
                    Message = $"插件「{instance.Entry.Name}」因连续出错已被自动禁用，已跳过本次执行。",
                };
            }

            // 参数校验：与设置面板共用同一个入口。
            // 这样「保存时通过」与「执行时通过」永远是同一个判断，
            // 不会出现用户填好参数、存下了、触发却说不合法的情况。
            PluginActionValidation validation = ValidateActionParameters(action);
            if (!validation.IsValid)
            {
                return new PluginExecuteOutcome
                {
                    Handled = true,
                    Success = false,
                    Message = $"{registration.DisplayName} 参数不合法：{validation.Describe()}",
                };
            }

            // 交给插件的是参数的一份拷贝：即使它在 ExecuteAsync 里改写字典，
            // 也污染不到用户正在编辑的配置对象。
            Dictionary<string, string> parameters = action.ExtensionData != null
                ? new Dictionary<string, string>(action.ExtensionData, StringComparer.OrdinalIgnoreCase)
                : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            return PluginInvoker.Invoke(instance, registration, parameters);
        }
        catch (Exception ex)
        {
            // 最外层兜底：这里无论如何都不能抛出去
            AppLogger.LogError("[plugin] 执行插件动作时发生未预期异常（已拦截）", ex);
            return new PluginExecuteOutcome
            {
                Handled = true,
                Success = false,
                Message = "插件动作执行时发生内部错误，详情见日志。",
            };
        }
    }

    // ------------------------------------------------------------------ 界面数据

    /// <summary>动作下拉里的插件动作分组（供 <c>SlotViewModel</c> 聚合）。</summary>
    public static List<ActionTypeItem> GetPluginActionItems()
    {
        var items = new List<ActionTypeItem>();
        foreach (PluginActionRegistration action in Catalog.SnapshotActions())
        {
            items.Add(new ActionTypeItem
            {
                Tag = PluginApi.ActionTypeName,
                DisplayText = $"🔌 {action.DisplayName}",
            });
        }
        return items;
    }

    /// <summary>已注册的插件动作（供槽位编辑器按插件分组展示）。</summary>
    public static List<PluginActionRegistration> GetRegisteredActions() => Catalog.SnapshotActions();

    public static bool TryGetAction(string fullId, out PluginActionRegistration registration) =>
        Catalog.TryGetAction(fullId, out registration);

    /// <summary>预览文案。失败时返回空串，绝不抛异常。</summary>
    public static string PreviewAction(string fullId, IReadOnlyDictionary<string, string> parameters)
    {
        try
        {
            return Catalog.TryGetAction(fullId, out PluginActionRegistration registration)
                ? registration.Contribution.Preview(parameters) ?? ""
                : "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// 构造一条指向插件动作的 <see cref="ActionItem"/>。
    /// <para>
    /// 刻意做成静态工厂而不是让调用方自己拼字段：插件动作的持久化形态（<c>Type="Plugin"</c> +
    /// <c>PluginActionRef</c> + <c>ExtensionData</c>）是契约的一部分，散落在各处手拼迟早会写出不一致的配置。
    /// </para>
    /// </summary>
    public static ActionItem? CreateActionItem(string fullId, Dictionary<string, string>? parameters = null)
    {
        if (!Catalog.TryGetAction(fullId, out PluginActionRegistration registration))
        {
            return null;
        }

        var action = new ActionItem
        {
            Type = PluginApi.ActionTypeName,
            Name = registration.DisplayName,
            IconKey = registration.IconKey ?? "",
            PluginActionRef = new PluginActionRef
            {
                PluginId = registration.PluginId,
                ContributionId = registration.ShortId,
            },
        };

        // 用参数默认值填充，让「新建动作」后立即就是可用的
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (ParameterField field in registration.Parameters)
        {
            if (!string.IsNullOrWhiteSpace(field.DefaultValue))
            {
                merged[field.Key] = field.DefaultValue!;
            }
        }
        if (parameters != null)
        {
            foreach (KeyValuePair<string, string> pair in parameters)
            {
                merged[pair.Key] = pair.Value;
            }
        }

        action.ExtensionData = merged.Count > 0 ? merged : null;
        return action;
    }

    /// <summary>
    /// 向用户提示一条与插件有关的信息。
    /// <para>优先走托盘气泡；没有可用托盘时降级为写日志 —— <b>绝不用 MessageBox</b>，
    /// 因为它会阻塞动作线程。</para>
    /// </summary>
    public static void NotifyUser(string title, string message)
    {
        if (string.IsNullOrWhiteSpace(message)) return;

        try
        {
            Action<string, string>? sink = PluginNotificationHub.Sink;
            if (sink != null)
            {
                sink(title ?? "StarPie 插件", message);
                return;
            }
        }
        catch
        {
        }

        AppLogger.LogInfo($"[plugin] 提示：{title} - {message}");
    }

    /// <summary>插件列表快照（供插件管理页）。</summary>
    public static List<PluginInstance> ListInstances()    {
        lock (Gate)
        {
            return Instances.Values
                .OrderBy(i => i.Entry.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }

    public static PluginInstance? Find(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return null;
        lock (Gate)
        {
            return Instances.TryGetValue(pluginId, out PluginInstance? instance) ? instance : null;
        }
    }

    /// <summary>把全部插件的健康度落盘（宿主退出或界面刷新时调用）。</summary>
    public static void FlushHealth()
    {
        foreach (PluginInstance instance in ListInstances())
        {
            try
            {
                instance.FlushHealth();
            }
            catch
            {
            }
        }
    }

    /// <summary>开发者模式开关。开启时允许「只登记外部路径不复制文件」。</summary>
    public static void SetDeveloperMode(bool enabled)
    {
        _developerMode = enabled;
        _preferences.DeveloperMode = enabled;
    }

    public static void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        _preferences.EnablePluginSystem = enabled;

        if (!enabled)
        {
            ShutdownAll();
        }
    }

    // ------------------------------------------------------------------ 轮盘事件广播

    private static readonly Dictionary<string, List<Action<ActionContext>>> WheelOpeningHandlers = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, List<Action>> WheelClosedHandlers = new(StringComparer.OrdinalIgnoreCase);

    public static IDisposable RegisterWheelOpening(string pluginId, Action<ActionContext> handler)
    {
        lock (Gate)
        {
            if (!WheelOpeningHandlers.TryGetValue(pluginId, out List<Action<ActionContext>>? list))
            {
                list = new List<Action<ActionContext>>();
                WheelOpeningHandlers[pluginId] = list;
            }
            list.Add(handler);
        }

        return new RegistrationToken(() =>
        {
            lock (Gate)
            {
                if (WheelOpeningHandlers.TryGetValue(pluginId, out List<Action<ActionContext>>? list))
                {
                    list.Remove(handler);
                }
            }
        });
    }

    public static IDisposable RegisterWheelClosed(string pluginId, Action handler)
    {
        lock (Gate)
        {
            if (!WheelClosedHandlers.TryGetValue(pluginId, out List<Action>? list))
            {
                list = new List<Action>();
                WheelClosedHandlers[pluginId] = list;
            }
            list.Add(handler);
        }

        return new RegistrationToken(() =>
        {
            lock (Gate)
            {
                if (WheelClosedHandlers.TryGetValue(pluginId, out List<Action>? list))
                {
                    list.Remove(handler);
                }
            }
        });
    }

    /// <summary>
    /// 广播「轮盘即将呈现」。
    /// <para>
    /// <b>必须由 UI 线程调用</b>（调用点应使用 <c>Dispatcher.BeginInvoke</c> 投递），
    /// 因为轮盘的呈现路径直接挂在鼠标钩子之后，插件代码绝不允许出现在那条路径上（红线 R2）。
    /// </para>
    /// </summary>
    public static void RaiseWheelOpening(ActionContext context)
    {
        if (!_enabled) return;

        List<Action<ActionContext>> handlers = new();
        lock (Gate)
        {
            foreach (List<Action<ActionContext>> list in WheelOpeningHandlers.Values)
            {
                handlers.AddRange(list);
            }
        }

        foreach (Action<ActionContext> handler in handlers)
        {
            try
            {
                handler(context);
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[plugin] OnWheelOpening 回调异常（已拦截）", ex);
            }
        }
    }

    public static void RaiseWheelClosed()
    {
        if (!_enabled) return;

        List<Action> handlers = new();
        lock (Gate)
        {
            foreach (List<Action> list in WheelClosedHandlers.Values)
            {
                handlers.AddRange(list);
            }
        }

        foreach (Action handler in handlers)
        {
            try
            {
                handler();
            }
            catch (Exception ex)
            {
                AppLogger.LogError("[plugin] OnWheelClosed 回调异常（已拦截）", ex);
            }
        }
    }

    // ------------------------------------------------------------------ 磁盘同步

    /// <summary>
    /// 把磁盘上的插件目录同步到内存登记表。返回本次「新发现」的数量。
    /// <para>只读清单文件，不加载程序集；新发现的插件一律登记为 Disabled。</para>
    /// </summary>
    public static int SyncFromDisk()
    {
        int discovered = 0;

        try
        {
            var knownIds = new HashSet<string>(
                PluginRegistryStore.SnapshotEntries().Select(e => e.Id),
                StringComparer.OrdinalIgnoreCase);

            // ① 已登记（含开发者外部路径）
            foreach (PluginRegistryEntry entry in PluginRegistryStore.SnapshotEntries())
            {
                PluginScanResult scan = ScanEntry(entry);

                // 关键：已在内存里的实例必须「就地更新」，绝不能 new 一个替换掉。
                // 旧实例仍然持有可回收加载上下文与已注册的贡献点，把它从字典里摘掉
                // 就会造出「孤儿」—— 动作还挂在轮盘上，宿主却再也找不到实例来卸载它，
                // 于是程序集、文件锁和内存全部无法释放。
                // 用户第二次进入插件管理页就会踩到这个坑（那里会先与磁盘对账）。
                PluginInstance? existing;
                lock (Gate)
                {
                    Instances.TryGetValue(entry.Id, out existing);
                }

                if (existing != null)
                {
                    existing.Entry = entry;
                    existing.Scan = scan;

                    // 只允许「尚未加载」的实例因识别失败而降级；
                    // 否则会把一个正在正常运行的插件误标成不兼容。
                    if (!scan.Accepted && existing.State != PluginRuntimeState.Active)
                    {
                        existing.MarkIncompatible(scan.DescribeFailure());
                    }

                    continue;
                }

                PluginInstance instance = new(entry.Id, entry, scan);

                if (!scan.Accepted)
                {
                    instance.MarkIncompatible(scan.DescribeFailure());
                }

                lock (Gate)
                {
                    Instances[entry.Id] = instance;
                }
            }

            // ② 手工放进「可写宿主区」目录（plugin-data\）但尚未登记的。
            // 注意这里只认「子目录 + plugin.json」——它对应的是「用户已经手工安装好了」，
            // 与只读来源区 <程序目录>\plugin\ 里那些待安装候选完全不是一回事（见 ScanCandidates）。
            foreach (string directory in Directory.GetDirectories(PluginPaths.Root))
            {
                string name = Path.GetFileName(directory);
                if (name.StartsWith(".pending-delete", StringComparison.OrdinalIgnoreCase)) continue;
                if (!File.Exists(PluginPaths.GetManifestPath(directory))) continue;

                PluginScanResult scan = PluginScanner.ScanInstalledPlugin(directory);
                if (!scan.Accepted || scan.Manifest == null) continue;
                if (!knownIds.Add(scan.Manifest.Id)) continue;

                var entry = new PluginRegistryEntry
                {
                    Id = scan.Manifest.Id,
                    Name = scan.Manifest.Name,
                    Version = scan.Manifest.Version,
                    Description = scan.Manifest.Description,
                    Author = scan.Manifest.Author,
                    License = scan.Manifest.License,
                    Homepage = scan.Manifest.Homepage,
                    InstallPath = name,
                    Enabled = false,
                    EntrySha256 = scan.Sha256,
                    Source = "Discovered",
                    InstalledAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz"),
                };
                PluginRegistryStore.UpsertEntry(entry);

                var instance = new PluginInstance(entry.Id, entry, scan);
                lock (Gate)
                {
                    Instances[entry.Id] = instance;
                }
                discovered++;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 同步插件目录失败", ex);
        }

        return discovered;
    }

    // ------------------------------------------------------------------ 只读扫描目录（候选）

    private static IReadOnlyList<PluginCandidate> _candidates = Array.Empty<PluginCandidate>();

    /// <summary>最近一次扫描出的候选插件清单。UI 直接读这个，不要自己去遍历目录。</summary>
    public static IReadOnlyList<PluginCandidate> Candidates
    {
        get { lock (Gate) { return _candidates; } }
    }

    /// <summary>
    /// 扫描<b>只读</b>目录 <c>程序目录\plugin</c>，得出「待安装候选」清单。
    /// <para>
    /// 与 <see cref="SyncFromDisk"/> 的<b>根本区别</b>：这里发现的东西<b>不会</b>登记、
    /// <b>不会</b>加载、也<b>不会</b>出现在插件列表里。它只说「这里躺着这些 .dll，
    /// 你可以装」，装不装由用户点按钮决定。
    /// </para>
    /// <para>
    /// 反过来，<see cref="SyncFromDisk"/> 第 ② 段会自动登记的是<b>可写宿主区</b>里
    /// 「子目录 + plugin.json」的手工投放 —— 那已经是安装产物了，与这里的候选是两回事。
    /// </para>
    /// <para>
    /// 目录不存在时直接得到空清单，<b>绝不创建它</b>：程序目录可能是只读的，
    /// 「本机没有随包附带的插件」本来就是完全正常的状态。
    /// </para>
    /// </summary>
    /// <returns>本次识别出的候选数量（含被拒绝、重复的）。</returns>
    public static int ScanCandidates()
    {
        var list = new List<PluginCandidate>();

        try
        {
            if (!PluginPaths.ScanRootExists)
            {
                lock (Gate) { _candidates = list; }
                return 0;
            }

            // 目录名固定从 PluginPaths 取；这里不递归子目录 ——
            // 扫描目录的约定就是「扁平，只放 .dll」，子目录一律不认。
            string[] files = Directory.GetFiles(PluginPaths.ScanRoot, "*.dll", SearchOption.TopDirectoryOnly);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            var scans = new List<PluginScanResult>(files.Length);
            foreach (string file in files)
            {
                scans.Add(ScanCandidateFile(file));
            }

            // 同 ID 计数按扫描目录内部去重统计（大小写不敏感）：这是识别「两枚 dll 撞 ID」的依据。
            var idCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (PluginScanResult scan in scans)
            {
                string? id = scan.Manifest?.Id;
                if (string.IsNullOrWhiteSpace(id)) continue;
                idCounts[id!] = idCounts.TryGetValue(id!, out int n) ? n + 1 : 1;
            }

            var installed = new Dictionary<string, PluginRegistryEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (PluginRegistryEntry entry in PluginRegistryStore.SnapshotEntries())
            {
                installed[entry.Id] = entry;
            }

            foreach (PluginScanResult scan in scans)
            {
                (PluginCandidateState state, string note) = ClassifyCandidate(scan, idCounts, installed);
                list.Add(new PluginCandidate
                {
                    DllPath = scan.DllPath,
                    FileName = Path.GetFileName(scan.DllPath),
                    Scan = scan,
                    State = state,
                    Note = note,
                });
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 扫描只读插件目录失败", ex);
        }

        lock (Gate) { _candidates = list; }
        return list.Count;
    }

    /// <summary>识别扫描目录里的一枚 dll。异常一律转成「识别未通过」而不是上抛 —— 一枚坏文件不该让整页空掉。</summary>
    private static PluginScanResult ScanCandidateFile(string file)
    {
        try
        {
            return PluginScanner.ScanSelectedDll(file);
        }
        catch (Exception ex)
        {
            return new PluginScanResult
            {
                DllPath = file,
                SourceDirectory = Path.GetDirectoryName(file) ?? "",
                Accepted = false,
                Failure = PluginScanFailure.NotDotNetAssembly,
                ErrorDetail = ex.Message,
            };
        }
    }

    /// <summary>
    /// 判定一枚候选与「已装的那份」是什么关系。
    /// <para>
    /// 顺序不能换：① 先看识别过没过（没过的连 ID 都没有，谈不上比较）；
    /// ② 再看扫描目录内部有没有撞 ID（自身有歧义就不该继续比）；
    /// ③ 再看已装的那份是不是外部路径登记（那种情况下根本不该复制文件进来）；
    /// ④ 最后才比版本与哈希。
    /// </para>
    /// </summary>
    private static (PluginCandidateState State, string Note) ClassifyCandidate(
        PluginScanResult scan,
        IReadOnlyDictionary<string, int> idCounts,
        IReadOnlyDictionary<string, PluginRegistryEntry> installed)
    {
        if (!scan.Accepted || scan.Manifest == null)
        {
            return (PluginCandidateState.Rejected, $"无法安装：{scan.DescribeFailure()}");
        }

        string id = scan.Manifest.Id;

        if (idCounts.TryGetValue(id, out int sameId) && sameId > 1)
        {
            return (PluginCandidateState.Duplicate,
                $"扫描目录里有 {sameId} 枚 .dll 声明了同一个 ID（{id}），无法判断该装哪一枚。请只保留需要的那一个文件。");
        }

        if (!installed.TryGetValue(id, out PluginRegistryEntry? entry))
        {
            return (PluginCandidateState.Installable, "尚未安装，可直接安装。");
        }

        if (!string.IsNullOrWhiteSpace(entry.ExternalPath))
        {
            return (PluginCandidateState.ExternalRegistered,
                $"同一个 ID 已被开发者模式的外部路径登记占用：{entry.ExternalPath}。" +
                "如需改为安装副本，请先在列表里卸载那条登记。");
        }

        string installedVersion = entry.Version ?? "";
        string candidateVersion = scan.Manifest.Version ?? "";

        bool sameHash = !string.IsNullOrWhiteSpace(entry.EntrySha256)
            && string.Equals(entry.EntrySha256, scan.Sha256, StringComparison.OrdinalIgnoreCase);

        if (SimpleVersion.TryParse(installedVersion, out SimpleVersion oldVersion)
            && SimpleVersion.TryParse(candidateVersion, out SimpleVersion newVersion))
        {
            int compare = newVersion.CompareTo(oldVersion);

            if (compare == 0)
            {
                return sameHash
                    ? (PluginCandidateState.Installed, $"已装同一个版本（v{installedVersion}），无需重复安装。")
                    : (PluginCandidateState.Replaced,
                        $"已装的 v{installedVersion} 与这枚文件版本号相同但内容不同（哈希不一致）。" +
                        "覆盖安装会用它替换现有文件。");
            }

            if (compare > 0)
            {
                return (PluginCandidateState.Update, $"已装 v{installedVersion}，这枚是更新的 v{candidateVersion}。");
            }

            return (PluginCandidateState.Downgrade,
                $"已装 v{installedVersion}，这枚是更旧的 v{candidateVersion}。一般不建议降级。");
        }

        return (PluginCandidateState.VersionUnknown,
            $"已装版本「{installedVersion}」与候选版本「{candidateVersion}」至少有一侧解析不了，无法比较新旧。" +
            (sameHash ? "内容与已装的一致。" : "内容与已装的不同。"));
    }

    /// <summary>
    /// 把一枚候选装进可写宿主区并启用。这是候选卡片上那个按钮的全部逻辑。
    /// <para>
    /// 安装动作本身仍复用 <see cref="CommitInstall"/>，这里只负责三件事：
    /// ① 拦住不允许安装的状态；② 替用户处理「正在运行所以文件被锁」；
    /// ③ 装完立刻重扫候选，让列表刷新成「已装同版本」。
    /// </para>
    /// </summary>
    public static bool InstallCandidate(PluginCandidate candidate, out string error)
    {
        error = "";

        if (candidate == null)
        {
            error = "候选为空。";
            return false;
        }

        if (!candidate.CanInstall)
        {
            error = $"当前状态不允许安装：{candidate.StateText}。{candidate.Note}";
            return false;
        }

        PluginScanResult scan = candidate.Scan;
        if (scan.Manifest == null)
        {
            error = "识别结果里没有清单，无法安装。";
            return false;
        }

        string pluginId = scan.Manifest.Id;

        // 覆盖安装必须先让文件解锁。插件是惰性加载的（Preload 默认 false），
        // 但一旦用户已经用过它的动作，程序集就被加载、文件就被占用，
        // 此时直接覆盖只会得到一句「文件被占用」——对用户就是「更新失败，原因不明」。
        // 这里主动停用再装：对用户始终只是「一次点击」。
        PluginInstance? existing = Find(pluginId);
        if (existing is { IsLoaded: true })
        {
            AppLogger.LogInfo($"[plugin] 覆盖安装 {pluginId} 前先行停用以解除文件占用");
            Disable(pluginId, out _);
        }

        var options = new PluginInstallOptions
        {
            // 能走到这个按钮前，用户已经在候选卡片上看过说明并点了确认。
            Acknowledged = true,
            OverwriteExisting = true,
            EnableAfterInstall = true,
            SourceKind = "ScanDirectory",
            AcknowledgedCapabilities = scan.Manifest.Capabilities is { Count: > 0 } capabilities
                ? new List<string>(capabilities)
                : new List<string>(),
        };

        PluginInstallResult result = CommitInstall(scan, options);
        if (!result.Success)
        {
            error = result.Error;
            ScanCandidates();
            return false;
        }

        ScanCandidates();
        return true;
    }

    /// <summary>重新扫描单个插件（用户点了「刷新」）。</summary>
    public static PluginScanResult Rescan(string pluginId)
    {
        PluginInstance? instance = Find(pluginId);
        if (instance == null)
        {
            return new PluginScanResult { Accepted = false, Failure = PluginScanFailure.DllNotFound, ErrorDetail = "插件未登记。" };
        }

        PluginScanResult scan = ScanEntry(instance.Entry);
        instance.Scan = scan;

        if (!scan.Accepted && !instance.IsLoaded)
        {
            instance.MarkIncompatible(scan.DescribeFailure());
        }
        else if (scan.Accepted)
        {
            instance.ClearError();
        }

        return scan;
    }

    private static PluginScanResult ScanEntry(PluginRegistryEntry entry)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(entry.ExternalPath))
            {
                return PluginScanner.ScanSelectedDll(entry.ExternalPath!);
            }

            string directory = Path.Combine(
                PluginPaths.Root,
                string.IsNullOrWhiteSpace(entry.InstallPath) ? entry.Id : entry.InstallPath);

            return PluginScanner.ScanInstalledPlugin(directory);
        }
        catch (Exception ex)
        {
            return new PluginScanResult
            {
                Accepted = false,
                Failure = PluginScanFailure.ManifestInvalid,
                ErrorDetail = ex.Message,
                SourceDirectory = entry.Id,
            };
        }
    }

    // ------------------------------------------------------------------ 安全模式

    /// <summary>
    /// 启动时判定是否需要进入安全模式。
    /// <para>
    /// 判据：上一次启动时记录「已加载插件集」，并有连续 2 次在启动后 30 秒内异常退出。
    /// 触发后自动禁用那批插件，让用户至少能进得去设置页。
    /// </para>
    /// </summary>
    private static void CheckSafeMode()
    {
        try
        {
            PluginHealthFile health = PluginRegistryStore.Health;

            if (!string.IsNullOrWhiteSpace(health.SafeModeUntil)
                && DateTimeOffset.TryParse(health.SafeModeUntil, out DateTimeOffset until)
                && until > DateTimeOffset.Now)
            {
                _safeModeActive = true;
                AppLogger.LogWarn(
                    $"[plugin] 已进入安全模式（至 {until:yyyy-MM-dd HH:mm}），本次启动不加载任何插件。" +
                    "如果确认插件没有问题，可在插件页「重置插件系统」。");
                return;
            }

            if (health.ConsecutiveStartupFailures >= 2 && health.LastStartupPluginSet.Count > 0)
            {
                _safeModeActive = true;

                foreach (string pluginId in health.LastStartupPluginSet)
                {
                    PluginRegistryStore.SetEnabled(pluginId, false);
                    AppLogger.LogWarn($"[plugin] 安全模式：已自动禁用疑似导致启动失败的插件 {pluginId}");
                }

                PluginRegistryStore.MutateHealthFile(h =>
                {
                    h.SafeModeUntil = DateTimeOffset.Now.AddDays(1).ToString("yyyy-MM-ddTHH:mm:sszzz");
                    h.LastStartupPluginSet.Clear();
                    h.ConsecutiveStartupFailures = 0;
                });

                PluginNotificationHub.Sink?.Invoke(
                    "StarPie 已进入插件安全模式",
                    "检测到连续两次启动异常，已临时禁用上次加载的插件。请到「插件」页检查。");
            }

            // 标记一次「启动中」，30 秒后若仍存活则清零（见 ScheduleStartupHealthCheck）
            //
            // 无界面模式（自检 / 路径诊断）不参与记账：它们跑完立刻退出，永远活不到
            // 30 秒健康检查那一刻，于是计数只增不减。而安全模式的判据是
            // 「连续两次启动异常 **且** 上次启动加载过插件」—— 用户装好插件正常用着，
            // 连着跑两次自检就可能被判定为「启动异常」，下次打开 GUI 时插件被自动禁用。
            // 这种误伤比少记一次数严重得多。
            if (!HeadlessMode)
            {
                PluginRegistryStore.MutateHealthFile(h => h.ConsecutiveStartupFailures++);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 安全模式判定失败", ex);
        }
    }

    /// <summary>启动 30 秒后确认存活：清零失败计数并记录本次加载的插件集。</summary>
    private static void ScheduleStartupHealthCheck()
    {
        try
        {
            var timer = new System.Threading.Timer(_ =>
            {
                try
                {
                    List<string> loaded = ListInstances()
                        .Where(i => i.IsLoaded)
                        .Select(i => i.PluginId)
                        .ToList();

                    PluginRegistryStore.MutateHealthFile(h =>
                    {
                        h.ConsecutiveStartupFailures = 0;
                        h.LastStartupPluginSet = loaded;
                    });

                    FlushHealth();
                    AppLogger.LogInfo(
                        $"[plugin] 启动健康检查通过：本会话加载 {loaded.Count} 个插件" +
                        (loaded.Count > 0 ? $"（{string.Join(", ", loaded)}）" : ""));
                }
                catch
                {
                }
            }, null, TimeSpan.FromSeconds(30), Timeout.InfiniteTimeSpan);

            _ = timer;
        }
        catch
        {
        }
    }

    /// <summary>启动后台预加载（仅 Preload=true 的已启用插件），不阻塞首帧。</summary>
    private static void SchedulePreload()
    {
        try
        {
            Task.Run(async () =>
            {
                // 刻意延迟：让主程序先把首帧、托盘、钩子都装好
                await Task.Delay(TimeSpan.FromSeconds(3)).ConfigureAwait(false);

                foreach (PluginInstance instance in ListInstances())
                {
                    if (!instance.Entry.Enabled || !instance.Entry.Preload) continue;
                    if (instance.IsLoaded) continue;

                    try
                    {
                        if (!instance.Load(out string failure))
                        {
                            AppLogger.LogWarn($"[plugin] 预加载 {instance.PluginId} 失败：{failure}");
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLogger.LogError($"[plugin] 预加载 {instance.PluginId} 异常", ex);
                    }

                    await Task.Delay(200).ConfigureAwait(false);
                }
            });
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 调度预加载失败", ex);
        }
    }

    private static void NotifyPluginSetChanged()
    {
        try
        {
            ConfigManager.MarkConfigurationChanged();
        }
        catch
        {
        }
    }

    // ------------------------------------------------------------------ 工具

    /// <summary>清理上次启动挂起的删除目录。</summary>
    private static void CleanupPendingDeletions()
    {
        try
        {
            foreach (string directory in Directory.GetDirectories(PluginPaths.Root, ".pending-delete-*"))
            {
                try
                {
                    Directory.Delete(directory, recursive: true);
                    AppLogger.LogInfo($"[plugin] 已清理挂起删除的目录：{Path.GetFileName(directory)}");
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>
    /// 按识别结果决定「复制什么」。
    /// <para>
    /// 规则只有一条，但必须说清为什么：<b>有没有 <c>plugin.json</c>，就是「这个目录是不是一个插件包」的判据</b>。
    /// </para>
    /// <list type="bullet">
    /// <item><c>ManifestSource == "Manifest"</c>：用户指的那个目录里有 <c>plugin.json</c>，
    /// 也就是在声明「这个目录整体是一个插件包」（可能带依赖 dll、图标、资源）。此时<b>整目录复制</b>。</item>
    /// <item><c>ManifestSource == "AssemblyMetadata"</c>：裸 DLL，靠程序集元数据兜底。
    /// 这种情况下 <c>SourceDirectory</c> 只表示「那枚 dll 碰巧躺在哪个目录」，它<b>不是</b>插件包 ——
    /// 可能正好是「下载」文件夹，也可能就是只读扫描目录 <c>plugin/</c>。
    /// 此时<b>只复制那一枚 dll</b>。
    /// <para>
    /// 早期版本在这里无条件整目录复制，有两个真实后果：从「下载」文件夹装一枚裸 dll
    /// 会把整个下载目录搬进插件目录；从 <c>plugin/</c> 安装则会把邻居插件的 dll 一起搬走
    /// —— 于是出现「只装了 A，B 也莫名其妙出现了」。
    /// </para></item>
    /// </list>
    /// </summary>
    private static bool CopyPayload(PluginScanResult scan, string targetDirectory, bool overwrite, out string error)
    {
        if (string.Equals(scan.ManifestSource, "Manifest", StringComparison.Ordinal))
        {
            return CopyDirectory(scan.SourceDirectory, targetDirectory, overwrite, out error);
        }

        if (!CopySingleFile(scan.DllPath, targetDirectory, overwrite, out error))
        {
            return false;
        }

        // 裸 DLL 装完之后必须回填一份清单，否则安装目录「缺 plugin.json」，
        // 后续识别（进而是启用）会直接失败 —— 表现是「装上了却怎么都启不动」。
        return WriteGeneratedManifest(scan, targetDirectory, out error);
    }

    /// <summary>
    /// 为裸 DLL 安装回填 <c>plugin.json</c>：把扫描阶段已经确认过的事实固化成清单。
    /// <para>
    /// 只回填「确定的」：ID、名称、版本、作者、能力、入口程序集文件名。
    /// <b>EntryType</b> 也一并写上 —— 扫描阶段已经解析出来了，写下来能让后续加载不再依赖
    /// 「唯一实现」这种约定推断。
    /// </para>
    /// </summary>
    private static bool WriteGeneratedManifest(PluginScanResult scan, string targetDirectory, out string error)
    {
        PluginManifest source = scan.Manifest!;

        var manifest = new PluginManifest
        {
            SchemaVersion = PluginApi.ManifestSchemaVersion,
            Id = source.Id,
            Name = source.Name,
            Description = source.Description,
            Author = source.Author,
            Homepage = source.Homepage,
            License = source.License,
            Version = source.Version,
            ApiVersion = source.ApiVersion,
            MinHostVersion = source.MinHostVersion,
            MaxHostVersion = source.MaxHostVersion,
            TargetFramework = source.TargetFramework,
            Platform = source.Platform,
            Assembly = Path.GetFileName(scan.DllPath),
            EntryType = scan.EntryTypeFullName,
            Capabilities = new List<string>(source.Capabilities),
            Contributions = new PluginContributions { Actions = true },
            Tags = new List<string>(source.Tags),
        };

        return PluginManifestReader.TryWrite(targetDirectory, manifest, out error);
    }

    /// <summary>只复制一枚程序集（裸 DLL 安装用）。</summary>
    private static bool CopySingleFile(string sourceFile, string targetDirectory, bool overwrite, out string error)
    {
        error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(sourceFile) || !File.Exists(sourceFile))
            {
                error = $"源文件不存在：{sourceFile}";
                return false;
            }

            string fileName = Path.GetFileName(sourceFile);

            // 与整目录复制保持同一条规则：SDK 契约程序集由宿主统一提供，插件不该自带一份。
            if (string.Equals(fileName, PluginApi.AbstractionsAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
            {
                error = $"{fileName} 是宿主统一提供的 SDK 契约程序集，不能作为插件安装。";
                return false;
            }

            if (!Directory.Exists(targetDirectory))
            {
                Directory.CreateDirectory(targetDirectory);
            }
            else if (overwrite)
            {
                ClearPreviousPayload(targetDirectory);
            }
            else
            {
                error = $"目标目录已存在：{targetDirectory}";
                return false;
            }

            File.Copy(sourceFile, Path.Combine(targetDirectory, fileName), overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            error = $"复制插件文件失败：{ex.Message}";
            return false;
        }
    }

    /// <summary>
    /// 覆盖安装裸 DLL 前，清掉上一次的「程序集 + 清单」。
    /// <para>
    /// 不清会踩两个坑：① 目录里留下两枚业务 dll，识别时的「唯一业务 dll」约定直接失效，
    /// 插件变成「找不到程序集」；② 上一次若是带 <c>plugin.json</c> 的包，残留清单会继续
    /// 接管识别，新装的裸 dll 会被判成「清单声明的入口类型不存在」。
    /// </para>
    /// <para>
    /// 只清「载荷」，<b>保留插件私有数据</b>：<c>data\</c> 目录与 <c>settings.json</c>
    /// 都是用户的东西，更新一次版本不该把它们清空。
    /// </para>
    /// </summary>
    private static void ClearPreviousPayload(string targetDirectory)
    {
        try
        {
            foreach (string file in Directory.GetFiles(targetDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(file), "settings.json", StringComparison.OrdinalIgnoreCase)) continue;
                File.Delete(file);
            }

            foreach (string directory in Directory.GetDirectories(targetDirectory, "*", SearchOption.TopDirectoryOnly))
            {
                if (string.Equals(Path.GetFileName(directory), "data", StringComparison.OrdinalIgnoreCase)) continue;
                Directory.Delete(directory, recursive: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 覆盖安装前清理旧载荷失败（将按原样覆盖）：{ex.Message}");
        }
    }

    private static bool CopyDirectory(string source, string target, bool overwrite, out string error)
    {
        error = "";
        try
        {
            if (!Directory.Exists(source))
            {
                error = $"源目录不存在：{source}";
                return false;
            }

            if (Directory.Exists(target) && !overwrite)
            {
                error = $"目标目录已存在：{target}";
                return false;
            }

            Directory.CreateDirectory(target);

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string relative = Path.GetRelativePath(source, file);
                string destination = Path.Combine(target, relative);

                // 不复制宿主会统一提供的 SDK 程序集，避免现场出现「类型身份分裂」的隐患
                string fileName = Path.GetFileName(file);
                if (string.Equals(fileName, PluginApi.AbstractionsAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase))
                {
                    AppLogger.LogWarn($"[plugin] 已跳过安装包内的 {fileName}（由宿主统一提供）。");
                    continue;
                }

                string? destinationDirectory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(destinationDirectory) && !Directory.Exists(destinationDirectory))
                {
                    Directory.CreateDirectory(destinationDirectory);
                }

                File.Copy(file, destination, overwrite: true);
            }

            return true;
        }
        catch (Exception ex)
        {
            error = $"复制插件文件失败：{ex.Message}";
            return false;
        }
    }
}
