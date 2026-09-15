using System;

namespace WinPieGestures.Plugins;

/// <summary>
/// 识别失败原因码。设计原则：<b>每一个失败都必须能翻译成一句用户看得懂、并且知道该做什么的话</b>，
/// 绝不允许只抛一句「加载失败」——社区插件出错时用户无从下手，是最容易劝退的体验。
/// </summary>
internal enum PluginScanFailure
{
    None = 0,

    // ---- 清单层（G-1）----
    IdNotDeclared,
    ManifestInvalid,
    InvalidIdFormat,
    ReservedIdPrefix,

    // ---- 程序集结构层（G-2）----
    DllNotFound,
    NotDotNetAssembly,
    NotIlOnly,
    WrongArchitecture,

    // ---- 契约层（G-3）----
    TargetFrameworkMismatch,
    NoContractImplementation,
    AmbiguousContractImplementation,
    EntryTypeNotFound,
    ApiVersionMismatch,
    ContractAssemblyVersionMismatch,

    // ---- 完整性层（G-4）----
    Sha256Mismatch,

    // ---- 兼容性层 ----
    HostVersionOutOfRange,

    // ---- 依赖层 ----
    DependencyMissing,
    DependencyCycle,
}

/// <summary>原因码 → 用户可读文案与修复建议。</summary>
internal static class PluginScanFailureText
{
    public static string Title(PluginScanFailure code) => code switch
    {
        PluginScanFailure.None => "正常",

        PluginScanFailure.IdNotDeclared => "未找到插件标识",
        PluginScanFailure.ManifestInvalid => "plugin.json 格式不正确",
        PluginScanFailure.InvalidIdFormat => "插件 ID 格式非法",
        PluginScanFailure.ReservedIdPrefix => "插件 ID 使用了保留前缀",

        PluginScanFailure.DllNotFound => "找不到插件程序集",
        PluginScanFailure.NotDotNetAssembly => "不是 .NET 程序集",
        PluginScanFailure.NotIlOnly => "程序集含本机代码",
        PluginScanFailure.WrongArchitecture => "架构不匹配（需要 64 位）",

        PluginScanFailure.TargetFrameworkMismatch => "目标框架不兼容",
        PluginScanFailure.NoContractImplementation => "不是 StarPie 插件",
        PluginScanFailure.AmbiguousContractImplementation => "入口类型不唯一",
        PluginScanFailure.EntryTypeNotFound => "清单声明的入口类型不存在",
        PluginScanFailure.ApiVersionMismatch => "插件 SDK 契约版本不兼容",
        PluginScanFailure.ContractAssemblyVersionMismatch => "SDK 程序集版本身份不一致",

        PluginScanFailure.Sha256Mismatch => "文件已损坏或被修改",
        PluginScanFailure.HostVersionOutOfRange => "宿主版本超出插件声明区间",

        PluginScanFailure.DependencyMissing => "缺少依赖插件",
        PluginScanFailure.DependencyCycle => "插件依赖存在环",

        _ => "未知原因",
    };

    public static string Hint(PluginScanFailure code) => code switch
    {
        PluginScanFailure.IdNotDeclared =>
            "这个 .dll 既没有同级的 plugin.json，也没有在程序集里声明 StarPiePluginId 元数据。" +
            "让作者按文档在 csproj 里补上 AssemblyMetadata 是推荐做法（分发时只需一枚 .dll）；" +
            "带 plugin.json 的完整插件包同样可以安装。",

        PluginScanFailure.ManifestInvalid =>
            "请检查 plugin.json 的字段名与类型是否与规范一致（可对照 plugin.schema.json）。",

        PluginScanFailure.InvalidIdFormat =>
            "插件 ID 需要是反向域名风格，全小写，例如 com.example.mytool。",

        PluginScanFailure.ReservedIdPrefix =>
            "starpie / windows / microsoft / system / builtin 前缀保留给官方，请换一个前缀。",

        PluginScanFailure.DllNotFound =>
            "清单里声明的程序集文件不在插件目录中，请确认打包时没有漏掉 .dll。",

        PluginScanFailure.NotDotNetAssembly =>
            "这是一枚原生 C++ DLL 或非托管库，StarPie 插件必须是 .NET 程序集。你可能选错了文件。",

        PluginScanFailure.NotIlOnly =>
            "程序集混合了本机代码（C++/CLI）。StarPie 只接受纯托管（ILOnly）程序集。",

        PluginScanFailure.WrongArchitecture =>
            "程序集被编译为仅 32 位（Requires32Bit）。请把插件的平台目标改为 x64 或 AnyCPU 后重新发布。",

        PluginScanFailure.TargetFrameworkMismatch =>
            "插件的目标框架高于当前 StarPie。请升级 StarPie，或联系作者改用更低的 net8.0-windows 目标。",

        PluginScanFailure.NoContractImplementation =>
            "程序集里找不到 IStarPiePlugin 的实现类，说明它不是一个 StarPie 插件。",

        PluginScanFailure.AmbiguousContractImplementation =>
            "程序集里有多个 IStarPiePlugin 实现。请在 plugin.json 的 entryType 里明确指定入口类全名。",

        PluginScanFailure.EntryTypeNotFound =>
            "plugin.json 里 entryType 写的类型名在程序集中不存在，请核对命名空间与类型名拼写。",

        PluginScanFailure.ApiVersionMismatch =>
            "插件编译时使用的 SDK 契约主版本与当前 StarPie 不一致。请更新插件，或升级 StarPie。",

        PluginScanFailure.ContractAssemblyVersionMismatch =>
            "插件自带了 StarPie.Plugin.Abstractions.dll 且版本与宿主不一致。请删除插件目录里的这个文件，它会由 StarPie 统一提供。",

        PluginScanFailure.Sha256Mismatch =>
            "文件内容与清单声明的哈希不一致，可能下载不完整或被第三方修改过。请从官方渠道重新获取。",

        PluginScanFailure.HostVersionOutOfRange =>
            "当前 StarPie 版本不在插件声明的可运行区间内。请升级 StarPie，或联系作者放宽版本区间。",

        PluginScanFailure.DependencyMissing =>
            "插件依赖的另一个插件没有安装或未启用。请先安装并启用依赖项。",

        PluginScanFailure.DependencyCycle =>
            "插件之间形成了循环依赖，无法确定加载顺序。请联系作者修复依赖声明。",

        _ => "请查看 StarPie 日志获取详细信息。",
    };
}

/// <summary>静态识别的完整结果。这是「安装确认卡」与「插件列表」的数据来源。</summary>
internal sealed class PluginScanResult
{
    public bool Accepted { get; set; }
    public PluginScanFailure Failure { get; set; } = PluginScanFailure.None;
    public string ErrorDetail { get; set; } = "";

    /// <summary>用户手动选择的那个 .dll 的完整路径。</summary>
    public string DllPath { get; set; } = "";

    /// <summary>清单来源目录（用于安装时整目录复制）。</summary>
    public string SourceDirectory { get; set; } = "";

    /// <summary>解析出的清单。可为 null（未通过 G-1 时）。</summary>
    public StarPie.Plugin.PluginManifest? Manifest { get; set; }

    /// <summary>清单来源：<c>Manifest</c>（plugin.json）或 <c>AssemblyMetadata</c>（裸 DLL 兜底）。</summary>
    public string ManifestSource { get; set; } = "";

    // ---- 展示用派生信息 ----
    public string FileSizeText { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public string MachineText { get; set; } = "";
    public string TargetFramework { get; set; } = "";
    public string? EntryTypeFullName { get; set; }
    public bool IsSigned { get; set; }
    public string? SignerSubject { get; set; }
    public string? SignerThumbprint { get; set; }
    public bool HasDependencyFile { get; set; }

    /// <summary>摘要哈希前 12 位，用于界面展示。</summary>
    public string Sha256Short => Sha256.Length >= 12 ? Sha256.Substring(0, 12) : Sha256;

    public string DescribeFailure() =>
        Failure == PluginScanFailure.None
            ? "正常"
            : $"{PluginScanFailureText.Title(Failure)}：{ErrorDetail}";
}
