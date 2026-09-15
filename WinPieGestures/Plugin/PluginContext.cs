using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// <see cref="IPluginContext"/> 的宿主实现 —— 插件能看到的「整个世界」。
/// <para>
/// 它刻意只暴露接口，不暴露任何宿主具体类型。这是「插件禁止引用 StarPie.dll」的落地方式：
/// 插件作者只能针对 SDK 编程，宿主内部怎么重构都不影响他们。
/// </para>
/// </summary>
internal sealed class PluginContext : IPluginContext
{
    public PluginContext(
        PluginMetadata metadata,
        string pluginDirectory,
        string dataDirectory,
        PluginRegistrationSession session,
        PluginLogger logger,
        PluginSettings settings,
        PluginEventService events)
    {
        Me = metadata;
        PluginDirectory = pluginDirectory;
        DataDirectory = dataDirectory;
        Log = logger;
        Settings = settings;
        Events = events;

        Actions = new PluginActionRegistry(session, metadata.Id);
        I18n = new PluginI18nRegistry(session, metadata.Id);
        Icons = new PluginIconRegistry(session, metadata.Id);
        Host = new PluginHostActionInvoker(metadata.Id);
        Info = new PluginHostInfo();
        Notify = new PluginNotificationService(metadata.Id);
        Dispatcher = new PluginDispatcherFacade();
    }

    public PluginMetadata Me { get; }

    public string PluginDirectory { get; }

    public string DataDirectory { get; }

    public IPluginLogger Log { get; }

    public IPluginSettings Settings { get; }

    public IActionRegistry Actions { get; }

    public II18nRegistry I18n { get; }

    public IIconRegistry Icons { get; }

    public IHostActionInvoker Host { get; }

    public IHostInfo Info { get; }

    public INotificationService Notify { get; }

    public IPluginEvents Events { get; }

    public IDispatcherFacade Dispatcher { get; }
}

/// <summary>
/// 动作贡献点注册表实现。
/// <para>
/// 契约要求每个 <c>Register</c> 都返回可释放 token：这是把 ALC 卸载从「靠插件自觉」
/// 变成「宿主可控」的关键 —— 实测证明只要宿主仍持有插件实例，卸载<b>必然失败</b>；
/// 而 token 让宿主永远有能力把引用摘干净。
/// </para>
/// </summary>
internal sealed class PluginActionRegistry : IActionRegistry
{
    private static readonly Regex IdPattern = new(
        "^[A-Za-z][A-Za-z0-9_]{0,63}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PluginRegistrationSession _session;
    private readonly string _pluginId;
    private readonly PluginCatalog _catalog;
    private readonly List<PluginActionRegistration> _owned = new();

    public PluginActionRegistry(PluginRegistrationSession session, string pluginId)
    {
        _session = session;
        _pluginId = pluginId;
        _catalog = PluginHost.Catalog;
    }

    /// <summary>本插件注册过的全部动作（供宿主展示与兜底撤销）。</summary>
    public IReadOnlyList<PluginActionRegistration> Owned => _owned;

    public IDisposable Register(IActionContribution contribution)
    {
        if (contribution == null)
        {
            throw new PluginContractException("Register(contribution) 传入了 null。");
        }

        ActionDescriptor? descriptor;
        try
        {
            descriptor = contribution.Descriptor;
        }
        catch (Exception ex)
        {
            throw new PluginContractException("读取 Descriptor 时插件抛出了异常。", ex);
        }

        if (descriptor == null)
        {
            throw new PluginContractException("Descriptor 返回了 null。");
        }

        string shortId = (descriptor.Id ?? "").Trim();
        if (shortId.Length == 0)
        {
            throw new PluginContractException("ActionDescriptor.Id 不能为空。");
        }
        if (!IdPattern.IsMatch(shortId))
        {
            throw new PluginContractException(
                $"ActionDescriptor.Id=\"{shortId}\" 非法：必须以字母开头，只允许字母、数字、下划线，最长 64 字符。");
        }

        string fullId = $"{_pluginId}.{shortId}";
        foreach (PluginActionRegistration staged in _session.StagedActions)
        {
            if (string.Equals(staged.FullId, fullId, StringComparison.OrdinalIgnoreCase))
            {
                throw new PluginContractException($"动作 ID 重复注册：{fullId}。");
            }
        }

        if (descriptor.TimeoutSeconds < 0 || descriptor.TimeoutSeconds > 300)
        {
            throw new PluginContractException(
                $"ActionDescriptor.TimeoutSeconds={descriptor.TimeoutSeconds} 超出范围（0 表示默认，可显式指定 1~300）。");
        }

        IReadOnlyList<ParameterField> parameters;
        try
        {
            parameters = contribution.Parameters ?? Array.Empty<ParameterField>();
        }
        catch (Exception ex)
        {
            throw new PluginContractException("读取 Parameters 时插件抛出了异常。", ex);
        }

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ParameterField field in parameters)
        {
            if (field == null)
            {
                throw new PluginContractException($"{fullId} 的 Parameters 里含有 null 项。");
            }
            if (string.IsNullOrWhiteSpace(field.Key))
            {
                throw new PluginContractException($"{fullId} 的参数缺少 Key。");
            }
            if (!seenKeys.Add(field.Key))
            {
                throw new PluginContractException($"{fullId} 的参数 Key 重复：{field.Key}。");
            }
            if (field.Type == ParameterFieldType.Enum && (field.Options == null || field.Options.Count == 0))
            {
                throw new PluginContractException($"{fullId} 的参数 {field.Key} 是 Enum 类型，但没有提供 Options。");
            }
        }

        string displayName = ResolveDisplayName(_pluginId, descriptor, shortId, out bool displayNameFromI18n);

        var registration = new PluginActionRegistration
        {
            PluginId = _pluginId,
            ShortId = shortId,
            FullId = fullId,
            Contribution = contribution,
            DisplayName = displayName,
            DisplayNameKey = string.IsNullOrWhiteSpace(descriptor.DisplayNameKey) ? null : descriptor.DisplayNameKey!.Trim(),
            DisplayNameFromI18n = displayNameFromI18n,
            Description = descriptor.Description ?? "",
            Category = string.IsNullOrWhiteSpace(descriptor.Category) ? "插件" : descriptor.Category!.Trim(),
            IconKey = descriptor.IconKey,
            Kind = descriptor.Kind,
            TimeoutSeconds = descriptor.TimeoutSeconds,
            Parameters = parameters,
        };

        _session.StageAction(registration);
        _owned.Add(registration);

        return new RegistrationToken(() =>
        {
            _owned.Remove(registration);
            _catalog.RemoveAction(registration.FullId);
        });
    }

    /// <param name="fromI18n">是否命中插件词条。调用方据此区分「词条生效」与「退回字面文案」。</param>
    private static string ResolveDisplayName(
        string pluginId,
        ActionDescriptor descriptor,
        string fallback,
        out bool fromI18n)
    {
        // 优先用插件自己的词条；没有词条就用它给的显示名；都没有才退回短 ID。
        // 键的换算交给 PluginI18n —— 这里过去只补「plugin.」前缀，与登记侧
        // 「plugin.<pluginId>.」的归一化结果不相等，导致本地化名永远查不到。
        string? translated = PluginI18n.Resolve(pluginId, descriptor.DisplayNameKey);
        if (translated != null)
        {
            fromI18n = true;
            return translated;
        }

        fromI18n = false;
        return string.IsNullOrWhiteSpace(descriptor.DisplayName) ? fallback : descriptor.DisplayName!.Trim();
    }
}

/// <summary>多语言词条注册表实现。插件只写短键，前缀归一化在这里统一完成。</summary>
internal sealed class PluginI18nRegistry : II18nRegistry
{
    private static readonly Regex KeyPattern = new(
        "^[A-Za-z0-9_.-]{1,96}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly PluginRegistrationSession _session;
    private readonly string _pluginId;

    public PluginI18nRegistry(PluginRegistrationSession session, string pluginId)
    {
        _session = session;
        _pluginId = pluginId;
    }

    private string Normalize(string key) =>
        $"{PluginApi.I18nKeyPrefix}{_pluginId}.{key.Trim()}";

    public void Register(string key, string zhCn, string? en = null)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PluginContractException("I18n.Register 的 key 不能为空。");
        }
        if (!KeyPattern.IsMatch(key.Trim()))
        {
            throw new PluginContractException(
                $"I18n.Register 的 key=\"{key}\" 非法：只允许字母、数字、下划线、点、连字符，最长 96 字符。");
        }
        if (string.IsNullOrEmpty(zhCn))
        {
            throw new PluginContractException($"I18n.Register(\"{key}\") 缺少简体中文文案（它是兜底语言，必填）。");
        }

        var values = new Dictionary<LanguageCode, string>
        {
            [LanguageCode.ZhCn] = zhCn,
        };

        if (!string.IsNullOrEmpty(en))
        {
            values[LanguageCode.En] = en!;
        }
        else
        {
            // 没给英文就让英文环境回退中文，而不是把 key 显示给用户
            values[LanguageCode.En] = zhCn;
        }

        StageOrMerge(Normalize(key), values);
    }

    public void RegisterTable(string languageCode, IReadOnlyDictionary<string, string> table)
    {
        if (table == null || table.Count == 0) return;

        LanguageCode language = ParseLanguage(languageCode);
        foreach (KeyValuePair<string, string> pair in table)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)) continue;
            if (!KeyPattern.IsMatch(pair.Key.Trim()))
            {
                AppLogger.LogWarn($"[plugin:{_pluginId}] 跳过非法词条 key：{pair.Key}");
                continue;
            }

            StageOrMerge(Normalize(pair.Key), new Dictionary<LanguageCode, string> { [language] = pair.Value ?? "" });
        }
    }

    public string T(string key, string? fallback = null)
    {
        if (string.IsNullOrWhiteSpace(key)) return fallback ?? "";

        string fullKey = key.Trim().StartsWith(PluginApi.I18nKeyPrefix, StringComparison.Ordinal)
            ? key.Trim()
            : Normalize(key);

        string translated = I18n.GetString(fullKey);
        if (!string.Equals(translated, fullKey, StringComparison.Ordinal))
        {
            return translated;
        }

        return fallback ?? key.Trim();
    }

    private void StageOrMerge(string fullKey, Dictionary<LanguageCode, string> values)
    {
        // 同一 key 的多语言分批注册时合并，而不是后写覆盖前写
        foreach (PluginCatalog.PluginI18nRegistration staged in _session.StagedI18n)
        {
            if (string.Equals(staged.FullKey, fullKey, StringComparison.Ordinal))
            {
                foreach (KeyValuePair<LanguageCode, string> pair in values)
                {
                    staged.Values[pair.Key] = pair.Value;
                }
                return;
            }
        }

        _session.StageI18n(new PluginCatalog.PluginI18nRegistration
        {
            PluginId = _pluginId,
            FullKey = fullKey,
            Values = values,
        });
    }

    private static LanguageCode ParseLanguage(string? code)
    {
        if (string.IsNullOrWhiteSpace(code)) return LanguageCode.ZhCn;

        return code!.Trim().ToLowerInvariant() switch
        {
            "zh-tw" or "zh-hk" or "zh-hant" => LanguageCode.ZhTw,
            "en" or "en-us" or "en-gb" => LanguageCode.En,
            "ja" or "ja-jp" => LanguageCode.Ja,
            _ => LanguageCode.ZhCn,
        };
    }
}

/// <summary>矢量图标注册表实现。</summary>
internal sealed class PluginIconRegistry : IIconRegistry
{
    private static readonly Regex KeyPattern = new(
        "^[A-Za-z0-9_-]{1,64}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>单枚图标的 SVG path 数据长度上限。SVG 是紧凑格式，64KB 已经极其夸张。</summary>
    private const int MaxSvgLength = 64 * 1024;

    private readonly PluginRegistrationSession _session;
    private readonly string _pluginId;
    private readonly Dictionary<string, string> _shortToFull = new(StringComparer.OrdinalIgnoreCase);

    public PluginIconRegistry(PluginRegistrationSession session, string pluginId)
    {
        _session = session;
        _pluginId = pluginId;
    }

    public string RegisterSvg(string key, string svgPathData)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new PluginContractException("Icons.RegisterSvg 的 key 不能为空。");
        }
        if (!KeyPattern.IsMatch(key.Trim()))
        {
            throw new PluginContractException(
                $"Icons.RegisterSvg 的 key=\"{key}\" 非法：只允许字母、数字、下划线、连字符，最长 64 字符。");
        }
        if (string.IsNullOrWhiteSpace(svgPathData))
        {
            throw new PluginContractException($"Icons.RegisterSvg(\"{key}\") 的 svgPathData 为空。");
        }
        if (svgPathData.Length > MaxSvgLength)
        {
            throw new PluginContractException(
                $"Icons.RegisterSvg(\"{key}\") 的 path 数据长度 {svgPathData.Length} 超过 {MaxSvgLength} 上限。");
        }

        string shortKey = key.Trim();
        string fullKey = $"{PluginApi.IconKeyPrefix}{_pluginId}:{shortKey}";

        _session.StageIcon(new PluginIconRegistration
        {
            PluginId = _pluginId,
            FullKey = fullKey,
            SvgPathData = svgPathData.Trim(),
        });

        _shortToFull[shortKey] = fullKey;
        return fullKey;
    }

    public string? ResolveKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;

        string raw = key.Trim();
        if (raw.StartsWith(PluginApi.IconKeyPrefix, StringComparison.Ordinal))
        {
            return _shortToFull.TryGetValue(raw, out string? direct) ? direct : raw;
        }

        return _shortToFull.TryGetValue(raw, out string? full) ? full : null;
    }
}

/// <summary>注册凭据。释放即摘除贡献点；宿主停用时也会兜底释放一次。</summary>
internal sealed class RegistrationToken : IDisposable
{
    private Action? _revoke;
    private bool _disposed;

    public RegistrationToken(Action revoke) => _revoke = revoke;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Action? action = _revoke;
        _revoke = null;
        try
        {
            action?.Invoke();
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 撤销注册时发生异常（已忽略）：{ex.Message}");
        }
    }
}
