namespace StarPie.Plugin;

/// <summary>
/// <c>plugin.json</c> 清单的内存模型。宿主与打包/校验工具共用这一份定义，避免字段语义漂移。
/// <para>
/// 所有字段采用 <c>PascalCase</c> 属性名 + <c>PropertyNameCaseInsensitive</c> 反序列化，
/// 与主程序既有的 <c>ConfigManager</c> 约定保持一致。
/// </para>
/// </summary>
public sealed class PluginManifest
{
    /// <summary>清单结构版本，必须被宿主支持（当前为 <see cref="PluginApi.ManifestSchemaVersion"/>）。</summary>
    public int SchemaVersion { get; set; } = PluginApi.ManifestSchemaVersion;

    /// <summary>全局唯一 ID，反向域名风格，如 <c>com.example.pomodoro</c>。</summary>
    public string Id { get; set; } = "";

    /// <summary>显示名。</summary>
    public string Name { get; set; } = "";

    /// <summary>一句话简介。</summary>
    public string Description { get; set; } = "";

    /// <summary>作者或组织。</summary>
    public string Author { get; set; } = "";

    /// <summary>项目主页。</summary>
    public string? Homepage { get; set; }

    /// <summary>SPDX 许可证标识，如 <c>MIT</c>。</summary>
    public string License { get; set; } = "MIT";

    /// <summary>插件版本（语义化），如 <c>1.0.0</c>。</summary>
    public string Version { get; set; } = "1.0.0";

    /// <summary>编译所依赖的 SDK 契约版本，如 <c>1.0</c>。主版本必须与宿主一致。</summary>
    public string ApiVersion { get; set; } = PluginApi.ApiVersion;

    /// <summary>最低可运行的宿主版本。</summary>
    public string MinHostVersion { get; set; } = "0.0.0";

    /// <summary>最高可运行的宿主版本；留空表示不限。</summary>
    public string? MaxHostVersion { get; set; }

    /// <summary>目标框架。宿主会做「不高于宿主」比较，如 <c>net8.0-windows</c> / <c>net8.0-windows10.0.19041.0</c>。</summary>
    public string TargetFramework { get; set; } = "net8.0-windows";

    /// <summary>目标平台，必须为 <c>win-x64</c>。</summary>
    public string Platform { get; set; } = "win-x64";

    /// <summary>
    /// 入口程序集文件名（相对插件目录）。留空时按「插件目录下唯一的 <c>StarPie.Plugin.*.dll</c>，
    /// 否则取 <c>&lt;id&gt;.dll</c>」推断。
    /// </summary>
    public string? Assembly { get; set; }

    /// <summary>
    /// <see cref="IStarPiePlugin"/> 实现类型的全名。留空时按默认命名约定推断；推断到 0 个或多个实现即拒绝加载。
    /// </summary>
    public string? EntryType { get; set; }

    /// <summary>能力声明。见 <see cref="PluginCapability"/>。</summary>
    public List<string> Capabilities { get; set; } = new();

    /// <summary>贡献点预声明（用于安装确认页展示与运行时交叉校验）。</summary>
    public PluginContributions Contributions { get; set; } = new();

    /// <summary>插件间依赖。</summary>
    public List<PluginDependency> Dependencies { get; set; } = new();

    /// <summary>插件图标（相对路径，SVG/PNG）。</summary>
    public string? Icon { get; set; }

    /// <summary>分类标签，最多 8 个。</summary>
    public List<string> Tags { get; set; } = new();

    /// <summary>入口程序集的 SHA256（发布时由打包脚本回写）。留空表示不做完整性校验。</summary>
    public string? Sha256 { get; set; }

    /// <summary>把 <see cref="Capabilities"/> 解析为标志枚举；无法识别的项会被忽略。</summary>
    public PluginCapability ResolveCapabilities()
    {
        PluginCapability result = PluginCapability.None;
        foreach (string raw in Capabilities)
        {
            if (Enum.TryParse(raw?.Trim(), ignoreCase: true, out PluginCapability one))
            {
                result |= one;
            }
        }
        return result;
    }

    /// <summary>返回 <see cref="Capabilities"/> 中无法识别的项（用于安装时提示「清单含未知能力声明」）。</summary>
    public List<string> GetUnknownCapabilities()
    {
        var unknown = new List<string>();
        foreach (string raw in Capabilities)
        {
            if (!Enum.TryParse(raw?.Trim(), ignoreCase: true, out PluginCapability _))
            {
                unknown.Add(raw ?? "");
            }
        }
        return unknown;
    }
}

/// <summary>贡献点预声明。字段存的是「数量」，用于安装确认页快速告知用户插件会往界面里加什么。</summary>
public sealed class PluginContributions
{
    /// <summary>是否提供自定义动作。</summary>
    public bool Actions { get; set; }

    /// <summary>是否提供矢量图标包。</summary>
    public bool Icons { get; set; }

    /// <summary>是否注册多语言词条。</summary>
    public bool I18n { get; set; }

    /// <summary>是否提供轮盘渲染形态（P1，首版宿主会忽略并给出提示）。</summary>
    public bool Styles { get; set; }

    /// <summary>是否提供预设方案 / 配置模板（P1）。</summary>
    public bool Presets { get; set; }

    /// <summary>人类可读的贡献点摘要，用于安装确认卡与列表徽章。</summary>
    public string Describe()
    {
        var parts = new List<string>(4);
        if (Actions) parts.Add("自定义动作");
        if (Icons) parts.Add("图标包");
        if (I18n) parts.Add("多语言");
        if (Styles) parts.Add("渲染形态");
        if (Presets) parts.Add("预设模板");
        return parts.Count == 0 ? "无声明" : string.Join(" · ", parts);
    }
}

/// <summary>插件间依赖声明。</summary>
public sealed class PluginDependency
{
    /// <summary>被依赖插件的 ID。</summary>
    public string Id { get; set; } = "";

    /// <summary>版本区间，简化语义化语法：<c>"1.2.0"</c>（≥）、<c>"[1.0,2.0)"</c>、<c>"*"</c>（任意）。</summary>
    public string VersionRange { get; set; } = "*";
}
