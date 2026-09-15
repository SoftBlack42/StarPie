using System;
using System.Collections.Generic;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>已注册的动作贡献点。宿主运行期只认这一份数据，不再回查插件。</summary>
internal sealed class PluginActionRegistration
{
    public string PluginId { get; init; } = "";

    /// <summary>插件内部短 ID。</summary>
    public string ShortId { get; init; } = "";

    /// <summary>全局唯一 ID：<c>&lt;pluginId&gt;.&lt;shortId&gt;</c>。写入 <c>ActionItem.PluginActionRef</c>。</summary>
    public string FullId { get; init; } = "";

    public IActionContribution Contribution { get; init; } = null!;

    /// <summary>显示名。会在提交阶段被重新解析一次，故可写。</summary>
    public string DisplayName { get; internal set; } = "";

    /// <summary>插件声明的词条短键，原样保留。用于回答「我给的 key 为什么没生效」。</summary>
    public string? DisplayNameKey { get; init; }

    /// <summary>
    /// <see cref="DisplayName"/> 是否来自插件词条。
    /// <para>
    /// 需要这个标志是因为：命中的译文与退回的字面文案在界面上可能<b>一模一样</b>，
    /// 肉眼完全分辨不出来。没有它，插件作者就无法判断自己的词条究竟有没有接上，
    /// 而这种「以为接上了其实没有」的状态会在换语言时才突然暴露。
    /// </para>
    /// </summary>
    public bool DisplayNameFromI18n { get; internal set; }

    public string Description { get; init; } = "";
    public string Category { get; init; } = "";
    public string? IconKey { get; init; }
    public ActionKind Kind { get; init; }
    public int TimeoutSeconds { get; init; }
    public IReadOnlyList<ParameterField> Parameters { get; init; } = Array.Empty<ParameterField>();

    /// <summary>动作下拉里显示的文案（带图标前缀）。</summary>
    public string MenuText => string.IsNullOrEmpty(IconKey) ? DisplayName : $"{DisplayName}";

    public override string ToString() => FullId;
}

/// <summary>已注册的图标。</summary>
internal sealed class PluginIconRegistration
{
    public string PluginId { get; init; } = "";

    /// <summary>完整 key：<c>plugin:&lt;pluginId&gt;:&lt;shortKey&gt;</c>。</summary>
    public string FullKey { get; init; } = "";

    /// <summary>SVG path 的 d 属性内容。</summary>
    public string SvgPathData { get; init; } = "";
}

/// <summary>
/// 贡献点注册表 —— 插件系统与主程序之间**唯一**的接缝。
/// <para>
/// 设计要点：
/// ① 全部操作加锁（注册发生在 UI 线程，调用发生在动作线程）；
/// ② <b>冲突即整体拒绝</b>，不做部分注册（见 <see cref="PluginContractException"/> 的说明）；
/// ③ 快照式读取：调用路径拿到的是一份已展开的注册对象，避免每次都要回查插件；
/// ④ <see cref="RevokeAll"/> 支持宿主兜底撤销（不依赖插件是否老实调用 Shutdown）。
/// </para>
/// </summary>
internal sealed class PluginCatalog
{
    private readonly object _gate = new();

    private readonly Dictionary<string, PluginActionRegistration> _actions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginIconRegistration> _icons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, PluginI18nRegistration> _i18n = new(StringComparer.Ordinal);

    /// <summary>注册阶段收集到的冲突描述。非空即表示整个插件注册失败。</summary>
    private readonly List<string> _stagedErrors = new();

    public sealed class PluginI18nRegistration
    {
        public string PluginId { get; init; } = "";
        public string FullKey { get; init; } = "";
        public Dictionary<LanguageCode, string> Values { get; init; } = new();
    }

    // ---------------------------------------------------------------- 事务式注册

    /// <summary>
    /// 开始一次「事务式注册」。插件 <c>Initialize</c> 期间的所有 <c>Register*</c> 调用
    /// 都会先落在暂存区，直到 <see cref="Commit"/> 才整体可见 —— 这样才谈得上「冲突即整体拒绝」。
    /// </summary>
    public PluginRegistrationSession BeginSession(string pluginId)
    {
        lock (_gate)
        {
            _stagedErrors.Clear();
        }
        return new PluginRegistrationSession(this, pluginId);
    }

    /// <summary>提交暂存注册。返回 false 表示存在冲突，此时<b>一条都不会生效</b>。</summary>
    public bool Commit(PluginRegistrationSession session, out string error)
    {
        lock (_gate)
        {
            var conflicts = new List<string>(session.Errors);

            foreach (PluginActionRegistration action in session.StagedActions)
            {
                if (_actions.TryGetValue(action.FullId, out PluginActionRegistration? existing))
                {
                    conflicts.Add($"动作 ID 冲突：{action.FullId} 已被 {existing.PluginId} 注册。");
                }
            }

            foreach (PluginIconRegistration icon in session.StagedIcons)
            {
                if (_icons.ContainsKey(icon.FullKey))
                {
                    conflicts.Add($"图标 key 冲突：{icon.FullKey} 已被注册。");
                }
            }

            foreach (PluginI18nRegistration i18n in session.StagedI18n)
            {
                if (_i18n.ContainsKey(i18n.FullKey))
                {
                    conflicts.Add($"语言 key 冲突：{i18n.FullKey} 已被注册。");
                }
            }

            if (conflicts.Count > 0)
            {
                error = string.Join("；", conflicts);
                return false;
            }

            foreach (PluginActionRegistration action in session.StagedActions)
            {
                _actions[action.FullId] = action;
            }

            foreach (PluginIconRegistration icon in session.StagedIcons)
            {
                _icons[icon.FullKey] = icon;
            }

            foreach (PluginI18nRegistration i18n in session.StagedI18n)
            {
                _i18n[i18n.FullKey] = i18n;
                I18n.RegisterExternal(i18n.FullKey, i18n.Values);
            }

            ResolveStagedDisplayNames(session);

            error = "";
            return true;
        }
    }

    /// <summary>
    /// 词条落地后，为之前查不到译文的动作补一次显示名解析。
    /// <para>
    /// <b>为什么需要这一步</b>：插件在 <c>Initialize</c> 里通常「先注册词条、再注册动作」，
    /// 但两者都是暂存的，要等 <c>Initialize</c> 成功后才一起提交。于是在动作注册的那一刻，
    /// 词条还没进 <see cref="I18n"/> —— 带 <c>DisplayNameKey</c> 的显示名会<b>全部落空</b>，
    /// 静默退回字面文案。而字面文案与译文常常一模一样，所以这个缺陷不会在中文环境下露面，
    /// 要等到用户切成英文、发现名字没变才会暴露。
    /// </para>
    /// <para>
    /// 放在提交之后补，而不是让解析去读暂存表：这样既不破坏
    /// 「Initialize 失败则整体不生效」的原子性，也不要求插件遵守
    /// 「词条必须写在动作之前」这种没人会记得的顺序约定。
    /// </para>
    /// </summary>
    private static void ResolveStagedDisplayNames(PluginRegistrationSession session)
    {
        foreach (PluginActionRegistration action in session.StagedActions)
        {
            // 注册当时就命中的不必重算；没声明 key 的本来就用字面文案。
            if (action.DisplayNameFromI18n) continue;
            if (string.IsNullOrEmpty(action.DisplayNameKey)) continue;

            string? translated = PluginI18n.Resolve(action.PluginId, action.DisplayNameKey);
            if (translated == null) continue;

            action.DisplayName = translated;
            action.DisplayNameFromI18n = true;
        }
    }

    /// <summary>丢弃暂存内容（注册失败或插件停用）。</summary>
    public void Discard(PluginRegistrationSession session)
    {
        lock (_gate)
        {
            session.StagedActions.Clear();
            session.StagedIcons.Clear();
            session.StagedI18n.Clear();
        }
    }

    // ------------------------------------------------------------------ 撤销

    /// <summary>撤销某个插件的全部贡献点。宿主停用插件时的兜底动作。</summary>
    public void RevokeAll(string pluginId)
    {
        lock (_gate)
        {
            var actionKeys = new List<string>();
            foreach (KeyValuePair<string, PluginActionRegistration> kv in _actions)
            {
                if (string.Equals(kv.Value.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)) actionKeys.Add(kv.Key);
            }
            foreach (string key in actionKeys) _actions.Remove(key);

            var iconKeys = new List<string>();
            foreach (KeyValuePair<string, PluginIconRegistration> kv in _icons)
            {
                if (string.Equals(kv.Value.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)) iconKeys.Add(kv.Key);
            }
            foreach (string key in iconKeys) _icons.Remove(key);

            var i18nKeys = new List<string>();
            foreach (KeyValuePair<string, PluginI18nRegistration> kv in _i18n)
            {
                if (string.Equals(kv.Value.PluginId, pluginId, StringComparison.OrdinalIgnoreCase)) i18nKeys.Add(kv.Key);
            }
            foreach (string key in i18nKeys)
            {
                _i18n.Remove(key);
                I18n.UnregisterExternal(key);
            }
        }
    }

    public bool RemoveAction(string fullId)
    {
        lock (_gate)
        {
            return _actions.Remove(fullId);
        }
    }

    // ------------------------------------------------------------------ 查询

    public bool TryGetAction(string fullId, out PluginActionRegistration registration)
    {
        lock (_gate)
        {
            return _actions.TryGetValue(fullId, out registration!);
        }
    }

    public List<PluginActionRegistration> SnapshotActions()
    {
        lock (_gate)
        {
            return new List<PluginActionRegistration>(_actions.Values);
        }
    }

    /// <summary>列出已注册动作的完整 ID 集合（用于界面提示「该动作由哪个插件提供」）。</summary>
    public HashSet<string> SnapshotActionIds()
    {
        lock (_gate)
        {
            return new HashSet<string>(_actions.Keys, StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? ResolveIcon(string fullKey)
    {
        lock (_gate)
        {
            return _icons.TryGetValue(fullKey, out PluginIconRegistration? icon) ? icon.SvgPathData : null;
        }
    }

    public bool RemoveIcon(string fullKey)
    {
        lock (_gate)
        {
            return _icons.Remove(fullKey);
        }
    }

    public int ActionCount
    {
        get { lock (_gate) return _actions.Count; }
    }

    public int IconCount
    {
        get { lock (_gate) return _icons.Count; }
    }

    public int I18nCount
    {
        get { lock (_gate) return _i18n.Count; }
    }
}

/// <summary>
/// 一次插件注册事务。所有 <c>Register*</c> 先落暂存，<see cref="PluginCatalog.Commit"/> 时整体生效。
/// </summary>
internal sealed class PluginRegistrationSession
{
    private readonly PluginCatalog _catalog;

    public string PluginId { get; }

    internal readonly List<PluginActionRegistration> StagedActions = new();
    internal readonly List<PluginIconRegistration> StagedIcons = new();
    internal readonly List<PluginCatalog.PluginI18nRegistration> StagedI18n = new();
    internal readonly List<string> Errors = new();

    internal PluginRegistrationSession(PluginCatalog catalog, string pluginId)
    {
        _catalog = catalog;
        PluginId = pluginId;
    }

    public void StageAction(PluginActionRegistration registration) => StagedActions.Add(registration);

    public void StageIcon(PluginIconRegistration registration) => StagedIcons.Add(registration);

    public void StageI18n(PluginCatalog.PluginI18nRegistration registration) => StagedI18n.Add(registration);

    public void Report(string error) => Errors.Add(error);

    public int StagedCount => StagedActions.Count + StagedIcons.Count + StagedI18n.Count;

    public bool Commit(out string error) => _catalog.Commit(this, out error);

    public void Discard() => _catalog.Discard(this);
}
