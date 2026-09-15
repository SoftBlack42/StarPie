using System;
using System.IO;
using System.Text.RegularExpressions;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件系统所有磁盘路径的<b>唯一来源</b>。
/// <para>
/// 一旦有第二处代码用 <c>Path.Combine</c> 拼插件路径，迟早会出现「设置页看到的目录」与
/// 「加载器实际用的目录」不一致的幽灵问题。所有模块一律从这里取路径。
/// </para>
/// <para>
/// <b>两个目录的职责必须分清</b>：
/// </para>
/// <list type="bullet">
/// <item><see cref="ScanRoot"/>（<c>程序目录\plugin</c>）：<b>只读来源区</b>。随发行包一起分发，
/// 里面只放 <c>.dll</c>。宿主永远只读、只扫描，<b>绝不创建、绝不写入、绝不删除</b> ——
/// 这样即使 StarPie 装在 Program Files 这种只读位置也不会出问题。</item>
/// <item><see cref="Root"/>（<c>%LOCALAPPDATA%\StarPie\plugin-data</c>）：<b>可写宿主区</b>。
/// 安装副本、<c>registry.json</c>、<c>health.json</c>、插件私有 <c>data\</c> 全在这里。
/// 宿主拥有这一整个目录，卸载时可以安全删除。</item>
/// </list>
/// <para>
/// 便携模式只改变<b>可写宿主区</b>的落点（挪到程序目录下的 <c>plugin-data</c>），
/// 不影响 <see cref="ScanRoot"/> —— 只读来源区永远固定在程序目录。
/// </para>
/// </summary>
internal static class PluginPaths
{
    private static string? _rootOverride;
    private static string? _scanRootOverride;
    private static bool _rootsPinned;

    /// <summary>便携模式的标志文件名，放在主程序目录下即可把可写宿主区挪到程序目录。</summary>
    public const string PortableFlagFileName = "portable.flag";

    /// <summary>插件清单文件名。插件包（带 <c>plugin.json</c>）的识别入口。</summary>
    public const string ManifestFileName = "plugin.json";

    /// <summary>插件包扩展名（本质是 zip）。</summary>
    public const string PackageExtension = ".spkg";

    /// <summary>只读扫描目录名（主程序目录下，固定）。</summary>
    public const string ScanDirectoryName = "plugin";

    /// <summary>可写宿主区目录名。</summary>
    public const string HostDirectoryName = "plugin-data";

    /// <summary>旧的可写宿主区目录名。仅用于一次性搬迁，禁止在别处引用。</summary>
    private const string LegacyHostDirectoryName = "plugins";

    /// <summary>
    /// <b>只读</b>扫描目录：<c>程序目录\plugin</c>。
    /// <para>
    /// 永远固定、与便携模式无关。里面的 <c>.dll</c> 只是<b>待安装候选</b>，
    /// 用户点了安装才会被复制进 <see cref="Root"/> 并启用。
    /// </para>
    /// </summary>
    public static string ScanRoot => _scanRootOverride ?? Path.Combine(AppContext.BaseDirectory, ScanDirectoryName);

    /// <summary>扫描目录是否存在。不存在时宿主<b>什么都不做</b>（绝不创建）。</summary>
    public static bool ScanRootExists
    {
        get
        {
            try { return Directory.Exists(ScanRoot); }
            catch { return false; }
        }
    }

    /// <summary>可写宿主区根目录（默认为 <c>%LOCALAPPDATA%\StarPie\plugin-data</c>）。</summary>
    public static string Root => _rootOverride ?? DefaultRoot;

    /// <summary>宿主运维数据：启用状态 / 版本 / 哈希 / 能力确认。</summary>
    public static string RegistryFile => Path.Combine(Root, "registry.json");

    /// <summary>宿主运维数据：失败计数 / 安全模式标记。</summary>
    public static string HealthFile => Path.Combine(Root, "health.json");

    /// <summary>插件日志目录（与主日志同根，按插件分文件）。</summary>
    public static string LogDirectory => Path.Combine(AppLogger.GetLogFolderPath(), "plugins");

    private static string LocalAppData =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable("LOCALAPPDATA"))
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("LOCALAPPDATA")!;

    private static string DefaultRoot => Path.Combine(LocalAppData, "StarPie", HostDirectoryName);

    private static string PortableRoot => Path.Combine(AppContext.BaseDirectory, HostDirectoryName);

    /// <summary>迁移前的旧可写宿主区。按当前是否便携分别对应程序目录或 <c>%LOCALAPPDATA%</c>。</summary>
    private static string ResolveLegacyRoot() => IsPortable
        ? Path.Combine(AppContext.BaseDirectory, LegacyHostDirectoryName)
        : Path.Combine(LocalAppData, "StarPie", LegacyHostDirectoryName);

    /// <summary>主程序目录下是否存在 <c>portable.flag</c>。</summary>
    public static bool PortableFlagPresent
    {
        get
        {
            try { return File.Exists(Path.Combine(AppContext.BaseDirectory, PortableFlagFileName)); }
            catch { return false; }
        }
    }

    /// <summary>
    /// 解析并锁定可写宿主区目录。必须在插件系统初始化时调用一次（早于任何 <c>registry.json</c> 读取）。
    /// <para>顺带做一次历史目录搬迁，见 <see cref="MigrateLegacyHostRoot"/>。</para>
    /// <para>
    /// 已经被 <see cref="OverrideRootsForTesting"/> 钉住时直接返回：否则自检传一半就被这里的
    /// <c>Configure</c> 覆盖回真实目录，「沙箱」二字就成了摆设，插件会被装到用户真实的插件目录里。
    /// </para>
    /// </summary>
    /// <param name="portableRequested">用户配置里是否勾选了便携模式。</param>
    public static void Configure(bool portableRequested)
    {
        if (_rootsPinned) return;

        bool portable = portableRequested || PortableFlagPresent;
        _rootOverride = portable ? PortableRoot : DefaultRoot;

        MigrateLegacyHostRoot();
    }

    /// <summary>
    /// 测试与自检用：把两个根目录钉到指定位置，之后 <see cref="Configure"/> 不再改动它们。
    /// <para>
    /// 自检会真实安装、启用、卸载插件。以前它跑在<b>真实</b>插件目录上，等于每跑一次回归
    /// 就动一次用户已经装好的插件（登记表被改写、目录被删）。所以自检必须先钉住沙箱目录。
    /// </para>
    /// </summary>
    internal static void OverrideRootsForTesting(string? hostRoot, string? scanRoot)
    {
        _rootsPinned = true;
        _rootOverride = hostRoot;
        _scanRootOverride = scanRoot;
    }

    /// <summary>
    /// 把旧的 <c>plugins\</c> 整体搬到 <c>plugin-data\</c>。
    /// <para>
    /// <b>不做这一步会静默丢数据</b>：<c>registry.json</c> 里存着每个插件的启用状态、
    /// 已确认的能力、入口哈希与版本。只改目录常量而不搬迁，等于把用户的启用状态、
    /// 风险确认全部清空 —— 界面看起来「插件全没了」，实际文件还在旧目录躺得好好的。
    /// </para>
    /// <para>
    /// 搬迁规则：新目录已存在则以新目录为准（不动旧目录，避免覆盖正在使用的新数据）；
    /// <c>Move</c> 失败（被占用 / 跨卷）时退化为递归复制，**宁可多留一份拷贝也不丢状态**。
    /// </para>
    /// </summary>
    private static void MigrateLegacyHostRoot()
    {
        try
        {
            string legacy = ResolveLegacyRoot();
            string current = Root;

            if (string.Equals(
                    Path.GetFullPath(legacy).TrimEnd(Path.DirectorySeparatorChar),
                    Path.GetFullPath(current).TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (Directory.Exists(current)) return;
            if (!Directory.Exists(legacy)) return;

            try
            {
                Directory.Move(legacy, current);
                AppLogger.LogInfo($"[plugin] 插件数据目录已迁移：{legacy} → {current}");
            }
            catch (Exception moveError)
            {
                CopyDirectoryRecursive(legacy, current);
                AppLogger.LogWarn(
                    $"[plugin] 插件数据目录无法移动（{moveError.Message}），已改为复制：{legacy} → {current}。" +
                    "确认新目录内插件工作正常后可手工删除旧目录。");
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError("[plugin] 插件数据目录迁移失败，本次将以空宿主区启动", ex);
        }
    }

    /// <summary>递归复制目录（仅搬迁兜底使用）。</summary>
    private static void CopyDirectoryRecursive(string source, string target)
    {
        Directory.CreateDirectory(target);

        foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
        {
            string relative = Path.GetRelativePath(source, directory);
            Directory.CreateDirectory(Path.Combine(target, relative));
        }

        foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), overwrite: true);
        }
    }

    /// <summary>当前生效的可写宿主区是否位于程序目录下（即便携模式）。</summary>
    public static bool IsPortable =>
        string.Equals(
            Path.GetFullPath(Root).TrimEnd(Path.DirectorySeparatorChar),
            Path.GetFullPath(PortableRoot).TrimEnd(Path.DirectorySeparatorChar),
            StringComparison.OrdinalIgnoreCase);

    public static string GetPluginDirectory(string pluginId) => Path.Combine(Root, pluginId);

    public static string GetDataDirectory(string pluginId) => Path.Combine(GetPluginDirectory(pluginId), "data");

    public static string GetManifestPath(string pluginDirectory) => Path.Combine(pluginDirectory, ManifestFileName);

    public static string GetSettingsPath(string pluginId) => Path.Combine(GetPluginDirectory(pluginId), "settings.json");

    public static string GetLogFilePath(string pluginId)
    {
        string safe = SanitizeForFileName(pluginId);
        return Path.Combine(LogDirectory, $"{safe}_{DateTime.Now:yyyy-MM-dd}.log");
    }

    /// <summary>插件 ID 合法性：反向域名风格，至少两级，全小写，允许数字与连字符。</summary>
    private static readonly Regex IdPattern = new(
        @"^[a-z0-9]([a-z0-9-]*[a-z0-9])?(\.[a-z0-9]([a-z0-9-]*[a-z0-9])?)+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static bool IsValidPluginId(string? pluginId) =>
        !string.IsNullOrWhiteSpace(pluginId) && IdPattern.IsMatch(pluginId);

    /// <summary>ID 是否占用了保留前缀（官方 / 系统命名空间）。</summary>
    public static bool IsReservedPluginId(string? pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return false;
        foreach (string prefix in StarPie.Plugin.PluginApi.ReservedIdPrefixes)
        {
            if (pluginId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                // 精确等于前缀本身（如 id = "system"）也算占用
                if (pluginId.Length == prefix.Length) return true;

                // 前缀后必须紧跟 . 或 - 才算占用命名空间，避免误伤 "windowshelper" 这类合法 ID
                char next = pluginId[prefix.Length];
                if (next == '.' || next == '-') return true;
            }
        }
        return false;
    }

    /// <summary>把任意字符串净化为合法文件名片段。</summary>
    public static string SanitizeForFileName(string raw)
    {
        if (string.IsNullOrEmpty(raw)) return "plugin";
        var chars = new char[raw.Length];
        int n = 0;
        foreach (char c in raw)
        {
            chars[n++] = (char.IsLetterOrDigit(c) || c == '.' || c == '-' || c == '_') ? c : '_';
        }
        return new string(chars, 0, n);
    }

    /// <summary>
    /// 确保<b>可写</b>宿主区与日志目录存在。失败时返回 false 而不抛异常。
    /// <para>
    /// 刻意不碰 <see cref="ScanRoot"/>：只读来源区不存在就是「本机没有随包附带的插件」，
    /// 属于完全正常的状态，创建它反而会在只读的程序目录里撞权限错误。
    /// </para>
    /// </summary>
    public static bool EnsureDirectories()
    {
        bool ok = true;
        try
        {
            if (!Directory.Exists(Root)) Directory.CreateDirectory(Root);
        }
        catch { ok = false; }

        try
        {
            if (!Directory.Exists(LogDirectory)) Directory.CreateDirectory(LogDirectory);
        }
        catch { ok = false; }

        return ok;
    }
}
