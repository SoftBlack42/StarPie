using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 清单（<c>plugin.json</c>）的读取与校验 —— 识别第一道闸门 G-1。
/// <para>
/// 它只做两件事：**读文件** + **按规范校验字段**。不触碰 DLL，不加载任何程序集，
/// 因此这一层永远不可能执行不可信代码。
/// </para>
/// </summary>
internal static class PluginManifestReader
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// 写清单时的选项。缩进写出，方便用户直接打开 <c>plugin-data\&lt;id&gt;\plugin.json</c>
    /// 看清「宿主观测到的这个插件到底是什么」；属性名保持 PascalCase，与规范一致。
    /// </summary>
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
    };

    /// <summary>宿主自身的目标框架。插件 TFM 不得高于它。</summary>
    public const string HostTargetFramework = "net8.0-windows10.0.19041.0";

    private static string? _hostVersion;

    /// <summary>宿主版本字符串（取自 <c>AssemblyInformationalVersion</c>，形如 <c>1.7.4-beta.2</c>）。</summary>
    public static string HostVersion
    {
        get
        {
            if (_hostVersion != null) return _hostVersion;
            try
            {
                Assembly asm = typeof(PluginManifestReader).Assembly;
                _hostVersion = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                               ?? asm.GetName().Version?.ToString(3)
                               ?? "0.0.0";
                // 去掉可能附带的 +build 后缀
                int plus = _hostVersion.IndexOf('+');
                if (plus >= 0) _hostVersion = _hostVersion.Substring(0, plus);
            }
            catch
            {
                _hostVersion = "0.0.0";
            }
            return _hostVersion;
        }
    }

    public static SimpleVersion HostVersionParsed =>
        SimpleVersion.TryParse(HostVersion, out SimpleVersion v) ? v : new SimpleVersion();

    /// <summary>读取并校验一份清单文件。</summary>
    public static bool TryLoad(
        string manifestPath,
        out PluginManifest manifest,
        out PluginScanFailure failure,
        out string error)
    {
        manifest = new PluginManifest();
        failure = PluginScanFailure.ManifestInvalid;
        error = "";

        try
        {
            string json = File.ReadAllText(manifestPath);
            PluginManifest? parsed = JsonSerializer.Deserialize<PluginManifest>(json, ReadOptions);
            if (parsed == null)
            {
                error = "清单内容为空或无法解析为对象。";
                return false;
            }
            manifest = parsed;
        }
        catch (JsonException ex)
        {
            error = $"JSON 解析失败（第 {ex.LineNumber + 1} 行）：{ex.Message}";
            return false;
        }
        catch (Exception ex)
        {
            error = $"读取失败：{ex.Message}";
            return false;
        }

        return Validate(manifest, out failure, out error);
    }

    /// <summary>
    /// 按规范校验清单。顺序刻意从「最基础」到「最兼容」，
    /// 保证用户拿到的是<b>第一个真正的问题</b>，而不是一串无关报错。
    /// </summary>
    public static bool Validate(
        PluginManifest manifest,
        out PluginScanFailure failure,
        out string error)
    {
        failure = PluginScanFailure.ManifestInvalid;
        error = "";

        // ---- schemaVersion ----
        if (manifest.SchemaVersion <= 0 || manifest.SchemaVersion > PluginApi.ManifestSchemaVersion)
        {
            failure = PluginScanFailure.ManifestInvalid;
            error = $"schemaVersion={manifest.SchemaVersion} 不被本版本支持（支持 1 ~ {PluginApi.ManifestSchemaVersion}）。请升级 StarPie。";
            return false;
        }

        // ---- id ----
        if (string.IsNullOrWhiteSpace(manifest.Id))
        {
            failure = PluginScanFailure.InvalidIdFormat;
            error = "缺少必填字段 id。";
            return false;
        }
        if (!PluginPaths.IsValidPluginId(manifest.Id))
        {
            failure = PluginScanFailure.InvalidIdFormat;
            error = $"id=\"{manifest.Id}\" 不是合法的反向域名格式（全小写，至少两级，如 com.example.mytool）。";
            return false;
        }
        if (PluginPaths.IsReservedPluginId(manifest.Id))
        {
            failure = PluginScanFailure.ReservedIdPrefix;
            error = $"id=\"{manifest.Id}\" 占用了保留前缀（{string.Join(" / ", PluginApi.ReservedIdPrefixes)}）。";
            return false;
        }

        // ---- 基础展示字段 ----
        if (string.IsNullOrWhiteSpace(manifest.Name))
        {
            error = "缺少必填字段 name。";
            return false;
        }
        if (manifest.Name.Length > 40)
        {
            error = $"name 超过 40 字符（当前 {manifest.Name.Length}）。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(manifest.Author))
        {
            error = "缺少必填字段 author。";
            return false;
        }
        if (manifest.Author.Length > 60)
        {
            error = $"author 超过 60 字符（当前 {manifest.Author.Length}）。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(manifest.Description))
        {
            error = "缺少必填字段 description。请写一句话说明插件做什么。";
            return false;
        }
        if (manifest.Description.Length > 200)
        {
            error = $"description 超过 200 字符（当前 {manifest.Description.Length}）。";
            return false;
        }
        if (string.IsNullOrWhiteSpace(manifest.License))
        {
            error = "缺少必填字段 license（SPDX 标识，如 MIT）。";
            return false;
        }
        if (manifest.Tags != null && manifest.Tags.Count > 8)
        {
            error = $"tags 超过 8 个（当前 {manifest.Tags.Count}）。";
            return false;
        }

        // ---- 版本 ----
        if (!SimpleVersion.TryParse(manifest.Version, out SimpleVersion pluginVersion))
        {
            error = $"version=\"{manifest.Version}\" 不是合法的语义化版本。";
            return false;
        }
        _ = pluginVersion;

        if (!SimpleVersion.TryParse(manifest.ApiVersion, out SimpleVersion apiVersion))
        {
            failure = PluginScanFailure.ApiVersionMismatch;
            error = $"apiVersion=\"{manifest.ApiVersion}\" 不是合法版本号。";
            return false;
        }
        if (apiVersion.Major != PluginApi.ApiVersionMajor)
        {
            failure = PluginScanFailure.ApiVersionMismatch;
            error = $"插件依赖 SDK 契约 {apiVersion.Major}.x，当前 StarPie 提供 {PluginApi.ApiVersionMajor}.x。";
            return false;
        }

        if (!SimpleVersion.TryParse(manifest.MinHostVersion, out SimpleVersion minHost))
        {
            error = $"minHostVersion=\"{manifest.MinHostVersion}\" 不是合法版本号。";
            return false;
        }

        if (!string.IsNullOrWhiteSpace(manifest.MaxHostVersion))
        {
            if (!SimpleVersion.TryParse(manifest.MaxHostVersion, out SimpleVersion maxHost))
            {
                error = $"maxHostVersion=\"{manifest.MaxHostVersion}\" 不是合法版本号。";
                return false;
            }
            if (maxHost.CompareTo(minHost) < 0)
            {
                error = $"maxHostVersion({manifest.MaxHostVersion}) 小于 minHostVersion({manifest.MinHostVersion})。";
                return false;
            }
        }

        // ---- 宿主版本区间 ----
        SimpleVersion host = HostVersionParsed;
        if (!SimpleVersion.SatisfiesMinimum(host, minHost))
        {
            failure = PluginScanFailure.HostVersionOutOfRange;
            error = $"插件要求宿主 ≥ {manifest.MinHostVersion}，当前为 {HostVersion}。请升级 StarPie。";
            return false;
        }
        if (!string.IsNullOrWhiteSpace(manifest.MaxHostVersion)
            && SimpleVersion.TryParse(manifest.MaxHostVersion, out SimpleVersion maxBound)
            && host.CompareTo(maxBound) > 0)
        {
            failure = PluginScanFailure.HostVersionOutOfRange;
            error = $"插件声明的最高宿主版本为 {manifest.MaxHostVersion}，当前为 {HostVersion}。请更新插件。";
            return false;
        }

        // ---- 平台 ----
        if (!string.Equals(manifest.Platform?.Trim(), "win-x64", StringComparison.OrdinalIgnoreCase))
        {
            error = $"platform=\"{manifest.Platform}\" 不受支持，必须为 win-x64。";
            return false;
        }

        // ---- 目标框架 ----
        if (!TargetFrameworkInfo.TryParse(manifest.TargetFramework, out TargetFrameworkInfo pluginTfm))
        {
            failure = PluginScanFailure.TargetFrameworkMismatch;
            error = $"targetFramework=\"{manifest.TargetFramework}\" 不是可识别的 TFM。";
            return false;
        }
        if (!TargetFrameworkInfo.TryParse(HostTargetFramework, out TargetFrameworkInfo hostTfm)
            || !TargetFrameworkInfo.IsCompatible(pluginTfm, hostTfm))
        {
            failure = PluginScanFailure.TargetFrameworkMismatch;
            error = $"插件目标框架 {manifest.TargetFramework} 高于宿主 {HostTargetFramework}。";
            return false;
        }

        // ---- 未知能力声明（仅提示，不阻断）----
        List<string> unknownCapabilities = manifest.GetUnknownCapabilities();
        if (unknownCapabilities.Count > 0)
        {
            AppLogger.LogWarn(
                $"[plugin] 清单 {manifest.Id} 含未知能力声明：{string.Join(", ", unknownCapabilities)}（已忽略）");
        }

        failure = PluginScanFailure.None;
        return true;
    }

    /// <summary>
    /// 供「裸 DLL」兜底路径使用的最小清单。字段全部来自程序集静态元数据，不执行任何代码。
    /// </summary>
    public static PluginManifest CreateFromAssemblyMetadata(
        string pluginId,
        string? name,
        string? version,
        string? author,
        string? description,
        string? homepage,
        string? license,
        IReadOnlyList<string> capabilities,
        string targetFramework,
        string? entryType)
    {
        return new PluginManifest
        {
            SchemaVersion = PluginApi.ManifestSchemaVersion,
            Id = pluginId,
            Name = string.IsNullOrWhiteSpace(name) ? pluginId : Trim(name!, 40),
            Author = string.IsNullOrWhiteSpace(author) ? "（未署名）" : Trim(author!, 60),
            Description = string.IsNullOrWhiteSpace(description)
                ? "该插件只提供了程序集元数据，未附带 plugin.json。"
                : Trim(description!, 200),
            Homepage = homepage,
            License = string.IsNullOrWhiteSpace(license) ? "未声明" : license!,
            Version = SimpleVersion.TryParse(version, out SimpleVersion v) ? v.ToString() : "0.0.1",
            ApiVersion = PluginApi.ApiVersion,
            MinHostVersion = "0.0.0",
            TargetFramework = string.IsNullOrWhiteSpace(targetFramework) ? HostTargetFramework : targetFramework,
            Platform = "win-x64",
            EntryType = entryType,
            Capabilities = new List<string>(capabilities),
            Contributions = new PluginContributions { Actions = true },
        };
    }

    private static string Trim(string raw, int max) => raw.Length <= max ? raw : raw.Substring(0, max);

    /// <summary>
    /// 把一份清单写进插件目录（<c>plugin.json</c>）。
    /// <para>
    /// 目前只有一个用途：<b>裸 DLL 安装后回填一份自描述清单</b>。这一步是必需的，不是锦上添花 ——
    /// 安装目录的识别走的是 <see cref="PluginScanner.ScanInstalledPlugin"/>，而它要求目录里有清单；
    /// 不写这份文件，裸 dll 会「装得上但永远启用不了」，报错是「插件目录里缺少 plugin.json」。
    /// </para>
    /// <para>
    /// 写出的清单是<b>纯数据</b>，不执行任何插件代码；<c>Sha256</c> 刻意留空，
    /// 让日后手工替换程序集不会被误报成「文件已损坏」。哈希比对交给
    /// <see cref="PluginRegistryEntry.EntrySha256"/>（候选列表据此判断「内容已变」）。
    /// </para>
    /// </summary>
    public static bool TryWrite(string pluginDirectory, PluginManifest manifest, out string error)
    {
        error = "";
        try
        {
            if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
            {
                error = $"插件目录不存在：{pluginDirectory}";
                return false;
            }

            string json = JsonSerializer.Serialize(manifest, WriteOptions);
            File.WriteAllText(PluginPaths.GetManifestPath(pluginDirectory), json);
            return true;
        }
        catch (Exception ex)
        {
            error = $"写入插件清单失败：{ex.Message}";
            return false;
        }
    }
}
