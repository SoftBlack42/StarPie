using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件静态识别器 —— 四道闸门全部在**不加载、不反射、不实例化**的前提下完成。
/// <para>
/// <b>为什么必须这样</b>：实测表明 <c>Assembly.LoadFrom</c> 返回时 module initializer 尚未执行，
/// 但一旦完成「反射取类型 + 实例化」就会执行。危险边界不是「加载」那一瞬间，
/// 而是从加载开始一直到触碰模块成员的全过程。所以识别必须严格停留在 PE 元数据层面 ——
/// 这既是安全要求，也是性能要求（单插件全量扫描 50~90 µs，扫 50 个插件约 3~4.5 ms）。
/// </para>
/// <para>
/// 一个刻意的顺序选择：<b>先看程序集再看清单</b>。用户最常见的错误是「选错了文件」，
/// 此时告诉他「这不是 .NET 程序集」远比「plugin.json 字段写错了」有用。
/// </para>
/// </summary>
internal static class PluginScanner
{
    /// <summary>契约接口全名。扫描时靠它判定「这是不是一个 StarPie 插件」。</summary>
    public const string ContractInterfaceFullName = "StarPie.Plugin.IStarPiePlugin";

    /// <summary>宿主自身声明的 Windows 平台版本上界。</summary>
    public const string HostPlatformVersion = "10.0.19041.0";

    /// <summary>宿主自身声明的 .NET 主版本上界。</summary>
    public const int HostNetMajor = 8;

    private const string TargetFrameworkAttributeName = "System.Runtime.Versioning.TargetFrameworkAttribute";
    private const string TargetPlatformAttributeName = "System.Runtime.Versioning.TargetPlatformAttribute";
    private const string AssemblyMetadataAttributeName = "System.Reflection.AssemblyMetadataAttribute";
    private const string AssemblyCompanyAttributeName = "System.Reflection.AssemblyCompanyAttribute";
    private const string AssemblyProductAttributeName = "System.Reflection.AssemblyProductAttribute";
    private const string AssemblyTitleAttributeName = "System.Reflection.AssemblyTitleAttribute";
    private const string AssemblyDescriptionAttributeName = "System.Reflection.AssemblyDescriptionAttribute";
    private const string AssemblyInformationalVersionAttributeName = "System.Reflection.AssemblyInformationalVersionAttribute";

    // 裸 DLL 兜底路径读取的元数据键（由 <AssemblyMetadata Include="..." Value="..." /> 生成）
    private const string MetaId = "StarPiePluginId";
    private const string MetaName = "StarPiePluginName";
    private const string MetaCapabilities = "StarPiePluginCapabilities";
    private const string MetaLicense = "StarPiePluginLicense";
    private const string MetaHomepage = "StarPiePluginHomepage";

    // ------------------------------------------------------------------ 公开入口

    /// <summary>
    /// 识别用户在文件对话框里手动选择的那一枚 <c>.dll</c>。
    /// 这是「手动选 .dll」产品语义的入口，必须能处理两种分发形态：
    /// ① 目录里带 <c>plugin.json</c>（推荐）；② 只有一枚裸 DLL（靠程序集元数据兜底）。
    /// </summary>
    public static PluginScanResult ScanSelectedDll(string dllPath)
    {
        var result = new PluginScanResult
        {
            DllPath = dllPath ?? "",
            SourceDirectory = string.IsNullOrEmpty(dllPath) ? "" : (Path.GetDirectoryName(dllPath) ?? ""),
        };

        if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
        {
            return Fail(result, PluginScanFailure.DllNotFound, $"文件不存在：{dllPath}");
        }

        if (!string.Equals(Path.GetExtension(dllPath), ".dll", StringComparison.OrdinalIgnoreCase))
        {
            return Fail(result, PluginScanFailure.NotDotNetAssembly,
                $"只接受 .dll 文件（当前：{Path.GetFileName(dllPath)}）。");
        }

        // ---- G-2 程序集结构 ----
        if (!ReadAssemblyFacts(result, dllPath, out AssemblyFacts facts))
        {
            return result;
        }

        result.MachineText = DescribeMachine(facts);
        result.TargetFramework = facts.TargetFrameworkRaw ?? "";
        result.FileSizeText = DescribeSize(dllPath);
        result.HasDependencyFile = HasDependencyFile(dllPath);

        // ---- G-1 清单 ----
        string manifestPath = PluginPaths.GetManifestPath(result.SourceDirectory);
        PluginManifest manifest;

        if (File.Exists(manifestPath))
        {
            if (!PluginManifestReader.TryLoad(manifestPath, out manifest, out PluginScanFailure mf, out string me))
            {
                return Fail(result, mf, me);
            }
            result.ManifestSource = "Manifest";
        }
        else
        {
            // 裸 DLL 兜底：不执行任何代码，只读程序集级自定义特性
            if (string.IsNullOrWhiteSpace(facts.MetadataId))
            {
                return Fail(result, PluginScanFailure.IdNotDeclared,
                    $"{Path.GetFileName(dllPath)} 旁边没有 plugin.json，程序集里也没有 {MetaId} 元数据，无法确定插件标识。" +
                    "请让作者按文档在 csproj 里补上 AssemblyMetadata（推荐，分发时只需一枚 .dll），" +
                    "或改用带 plugin.json 的完整插件包。");
            }

            manifest = PluginManifestReader.CreateFromAssemblyMetadata(
                pluginId: facts.MetadataId!.Trim(),
                name: facts.MetadataName ?? facts.AssemblyTitle ?? facts.AssemblyProduct,
                version: facts.AssemblyInformationalVersion ?? facts.AssemblyVersion,
                author: facts.AssemblyCompany,
                description: facts.AssemblyDescription,
                homepage: facts.MetadataHomepage,
                license: facts.MetadataLicense,
                capabilities: SplitCapabilities(facts.MetadataCapabilities),
                targetFramework: facts.TargetFrameworkRaw ?? PluginManifestReader.HostTargetFramework,
                entryType: null);

            if (!PluginManifestReader.Validate(manifest, out PluginScanFailure mf2, out string me2))
            {
                return Fail(result, mf2, me2);
            }
            result.ManifestSource = "AssemblyMetadata";
        }

        result.Manifest = manifest;

        // ---- G-3 契约符合性 ----
        if (!CheckContract(result, manifest, facts)) return result;

        // ---- G-4 完整性 / 来源 ----
        if (!CheckIntegrity(result, manifest, dllPath)) return result;

        result.Accepted = true;
        result.Failure = PluginScanFailure.None;
        return result;
    }

    /// <summary>
    /// 识别一个<b>已安装</b>的插件目录。与 <see cref="ScanSelectedDll"/> 的区别是：
    /// 主程序集由清单的 <c>assembly</c> 字段（或命名约定）决定，而不是由用户选择。
    /// </summary>
    public static PluginScanResult ScanInstalledPlugin(string pluginDirectory)
    {
        var result = new PluginScanResult
        {
            SourceDirectory = pluginDirectory ?? "",
            DllPath = "",
        };

        if (string.IsNullOrWhiteSpace(pluginDirectory) || !Directory.Exists(pluginDirectory))
        {
            return Fail(result, PluginScanFailure.DllNotFound, $"插件目录不存在：{pluginDirectory}");
        }

        string manifestPath = PluginPaths.GetManifestPath(pluginDirectory);
        if (!File.Exists(manifestPath))
        {
            return Fail(result, PluginScanFailure.ManifestInvalid,
                "插件目录里缺少 plugin.json。若这是手工放入的插件，请重新通过「从 .dll 安装」导入。");
        }

        if (!PluginManifestReader.TryLoad(manifestPath, out PluginManifest manifest, out PluginScanFailure mf, out string me))
        {
            return Fail(result, mf, me);
        }
        result.Manifest = manifest;
        result.ManifestSource = "Manifest";

        string? dllPath = ResolveEntryAssemblyPath(pluginDirectory, manifest);
        if (dllPath == null)
        {
            return Fail(result, PluginScanFailure.DllNotFound,
                string.IsNullOrWhiteSpace(manifest.Assembly)
                    ? $"插件目录下找不到与插件 ID 同名的程序集（{manifest.Id}.dll），请在 manifest 里显式声明 assembly 字段。"
                    : $"清单声明的程序集不存在：{manifest.Assembly}");
        }
        result.DllPath = dllPath;

        if (!ReadAssemblyFacts(result, dllPath, out AssemblyFacts facts)) return result;

        result.MachineText = DescribeMachine(facts);
        result.TargetFramework = facts.TargetFrameworkRaw ?? "";
        result.FileSizeText = DescribeSize(dllPath);
        result.HasDependencyFile = HasDependencyFile(dllPath);

        if (!CheckContract(result, manifest, facts)) return result;
        if (!CheckIntegrity(result, manifest, dllPath)) return result;

        result.Accepted = true;
        result.Failure = PluginScanFailure.None;
        return result;
    }

    /// <summary>按清单的 assembly 字段（或命名约定）定位入口程序集。</summary>
    public static string? ResolveEntryAssemblyPath(string pluginDirectory, PluginManifest manifest)
    {
        if (!string.IsNullOrWhiteSpace(manifest.Assembly))
        {
            string candidate = Path.Combine(pluginDirectory, manifest.Assembly!);
            return File.Exists(candidate) ? candidate : null;
        }

        // 约定一：与插件 ID 同名
        string byId = Path.Combine(pluginDirectory, manifest.Id + ".dll");
        if (File.Exists(byId)) return byId;

        try
        {
            // 约定二：目录下唯一的 StarPie.Plugin.*.dll
            string[] branded = Directory.GetFiles(pluginDirectory, "StarPie.Plugin.*.dll", SearchOption.TopDirectoryOnly);
            if (branded.Length == 1) return branded[0];

            // 约定三：目录下唯一的业务 .dll（排除 SDK 自身与框架程序集）
            string[] candidates = Directory.GetFiles(pluginDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                .Where(p =>
                {
                    string n = Path.GetFileName(p);
                    return !n.Equals(PluginApi.AbstractionsAssemblyName + ".dll", StringComparison.OrdinalIgnoreCase)
                        && !n.StartsWith("System.", StringComparison.OrdinalIgnoreCase)
                        && !n.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase);
                })
                .ToArray();
            if (candidates.Length == 1) return candidates[0];
        }
        catch
        {
            // 目录读取失败按「找不到」处理
        }

        return null;
    }

    // ------------------------------------------------------------------ G-2 结构

    /// <summary>只读 PE 头与元数据根，判定「这是一个能用的 .NET 程序集吗」。</summary>
    private static bool ReadAssemblyFacts(PluginScanResult result, string dllPath, out AssemblyFacts facts)
    {
        facts = new AssemblyFacts();
        try
        {
            using var stream = new FileStream(dllPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);

            if (!pe.HasMetadata || pe.PEHeaders.CorHeader == null)
            {
                Fail(result, PluginScanFailure.NotDotNetAssembly,
                    "文件里没有 CLR 元数据头，它不是托管程序集（可能是原生 DLL 或损坏文件）。");
                return false;
            }

            CorFlags flags = pe.PEHeaders.CorHeader.Flags;
            facts.IlOnly = (flags & CorFlags.ILOnly) != 0;
            facts.Requires32Bit = (flags & CorFlags.Requires32Bit) != 0;
            facts.Machine = pe.PEHeaders.CoffHeader.Machine;

            if (!facts.IlOnly)
            {
                Fail(result, PluginScanFailure.NotIlOnly,
                    $"CorFlags={flags}（缺少 ILOnly）。StarPie 只接受纯托管程序集。");
                return false;
            }
            if (facts.Requires32Bit)
            {
                Fail(result, PluginScanFailure.WrongArchitecture,
                    $"CorFlags={flags} 标记为 Requires32Bit。请把插件平台目标改为 x64 或 AnyCPU 后重新发布。");
                return false;
            }

            // 刻意**不要求** Machine == Amd64：AnyCPU 纯 IL 程序集在 x64 宿主中完全安全，
            // 强制要求反而会误伤 Visual Studio 的默认模板（它默认生成 AnyCPU）。
            MetadataReader mr = pe.GetMetadataReader();
            ReadAssemblyLevelFacts(mr, facts);
            ReadContractTypes(mr, facts);
            ReadAssemblyReferences(mr, facts);
        }
        catch (BadImageFormatException)
        {
            // 这里只给出面向用户的话术：真正的原因（PE 头、元数据表结构异常）对用户没有意义，
            // 能引导他「换个文件重试」才是有效信息。细节留给宿主日志。
            Fail(result, PluginScanFailure.NotDotNetAssembly,
                "该文件的 PE 头或元数据表结构不符合 .NET 程序集规范，可能是原生 DLL 或已损坏。");
            return false;
        }
        catch (Exception ex)
        {
            Fail(result, PluginScanFailure.NotDotNetAssembly, $"读取程序集失败：{ex.Message}");
            return false;
        }

        return true;
    }

    private static void ReadAssemblyLevelFacts(MetadataReader mr, AssemblyFacts facts)
    {
        AssemblyDefinition asm = mr.GetAssemblyDefinition();

        facts.AssemblyName = mr.GetString(asm.Name);
        facts.AssemblyVersion = asm.Version.Build < 0
            ? $"{asm.Version.Major}.{asm.Version.Minor}.0"
            : $"{asm.Version.Major}.{asm.Version.Minor}.{asm.Version.Build}";

        foreach (CustomAttributeHandle handle in asm.GetCustomAttributes())
        {
            CustomAttribute ca = mr.GetCustomAttribute(handle);
            string? owner = GetAttributeConstructorOwner(mr, ca.Constructor);
            if (owner == null) continue;

            switch (owner)
            {
                case TargetFrameworkAttributeName:
                    facts.DotNetCoreAppVersion = ReadSingleStringArgument(mr, ca);
                    break;

                case TargetPlatformAttributeName:
                    facts.TargetPlatformVersion = ReadSingleStringArgument(mr, ca);
                    break;

                case AssemblyInformationalVersionAttributeName:
                    facts.AssemblyInformationalVersion = TrimBuildMetadata(ReadSingleStringArgument(mr, ca));
                    break;

                case AssemblyCompanyAttributeName:
                    facts.AssemblyCompany = ReadSingleStringArgument(mr, ca);
                    break;

                case AssemblyProductAttributeName:
                    facts.AssemblyProduct = ReadSingleStringArgument(mr, ca);
                    break;

                case AssemblyTitleAttributeName:
                    facts.AssemblyTitle = ReadSingleStringArgument(mr, ca);
                    break;

                case AssemblyDescriptionAttributeName:
                    facts.AssemblyDescription = ReadSingleStringArgument(mr, ca);
                    break;

                case AssemblyMetadataAttributeName:
                    if (TryReadStringPair(mr, ca, out string key, out string value))
                    {
                        switch (key)
                        {
                            case MetaId: facts.MetadataId = value; break;
                            case MetaName: facts.MetadataName = value; break;
                            case MetaCapabilities: facts.MetadataCapabilities = value; break;
                            case MetaLicense: facts.MetadataLicense = value; break;
                            case MetaHomepage: facts.MetadataHomepage = value; break;
                        }
                    }
                    break;
            }
        }

        ParseNetVersion(facts);
        facts.TargetFrameworkRaw = BuildTargetFrameworkString(facts.NetMajor, facts.NetMinor, facts.TargetPlatformVersion);
    }

    /// <summary>从 <c>TargetFrameworkAttribute</c> 的 <c>.NETCoreApp,Version=v8.0</c> 里取 net 主次版本。</summary>
    private static void ParseNetVersion(AssemblyFacts facts)
    {
        if (string.IsNullOrEmpty(facts.DotNetCoreAppVersion)) return;

        const string marker = "Version=v";
        int index = facts.DotNetCoreAppVersion!.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0) return;

        string text = facts.DotNetCoreAppVersion.Substring(index + marker.Length).Trim();
        string[] parts = text.Split('.');
        if (parts.Length >= 1 && int.TryParse(parts[0], out int major)) facts.NetMajor = major;
        if (parts.Length >= 2 && int.TryParse(parts[1], out int minor)) facts.NetMinor = minor;
    }

    /// <summary>
    /// 把 <c>TargetFrameworkAttribute</c> + <c>TargetPlatformAttribute</c> 还原成 TFM 字符串。
    /// 这是「程序集真实需求」，比清单里作者手写的 targetFramework 可靠。
    /// </summary>
    private static string BuildTargetFrameworkString(int netMajor, int netMinor, string? platformVersion)
    {
        string head = $"net{netMajor}.{netMinor}";
        if (string.IsNullOrWhiteSpace(platformVersion)
            || !platformVersion!.StartsWith("Windows", StringComparison.OrdinalIgnoreCase))
        {
            return head + "-windows";
        }

        string ver = platformVersion.Substring("Windows".Length);
        // Windows7.0 是 net8.0-windows 未指定平台版本时的默认值，语义上等同「无平台版本」
        if (ver.StartsWith("7.0", StringComparison.Ordinal)) return head + "-windows";
        return $"{head}-windows{ver}";
    }

    /// <summary>扫描全部类型定义，找出实现 <c>IStarPiePlugin</c> 的类型（沿基类链）。</summary>
    private static void ReadContractTypes(MetadataReader mr, AssemblyFacts facts)
    {
        foreach (TypeDefinitionHandle handle in mr.TypeDefinitions)
        {
            TypeDefinition td = mr.GetTypeDefinition(handle);
            if ((td.Attributes & TypeAttributes.Interface) != 0) continue;
            if (!TypeImplementsContract(mr, handle, 0)) continue;

            string fullName = GetTypeFullName(mr, handle);
            facts.ContractTypes.Add(fullName);
            if (IsInstantiablePublicClass(mr, td)) facts.EntryCandidates.Add(fullName);
        }
    }

    private static bool TypeImplementsContract(MetadataReader mr, TypeDefinitionHandle handle, int depth)
    {
        if (depth > 16) return false;

        TypeDefinition td = mr.GetTypeDefinition(handle);

        // TypeDef 表第 1 行永远是 <Module> 伪类型，它的 Extends 列是空值，
        // 直接读 BaseType 会让 MetadataReader 抛 "Read out of bounds"。必须先跳过。
        if (IsModulePseudoType(mr, td)) return false;

        foreach (InterfaceImplementationHandle ih in td.GetInterfaceImplementations())
        {
            InterfaceImplementation ii = mr.GetInterfaceImplementation(ih);
            if (string.Equals(GetTypeFullName(mr, ii.Interface), ContractInterfaceFullName, StringComparison.Ordinal))
            {
                return true;
            }
        }

        // 基类链：插件常把 IStarPiePlugin 声明在抽象基类上，此时 InterfaceImpl 表里查不到
        EntityHandle baseType;
        try
        {
            baseType = td.BaseType;
        }
        catch
        {
            // 少数异常程序集的 Extends 列不可解析；按「无基类」处理，不影响后续判定
            return false;
        }

        if (baseType.Kind == HandleKind.TypeDefinition)
        {
            return TypeImplementsContract(mr, (TypeDefinitionHandle)baseType, depth + 1);
        }
        return false;
    }

    /// <summary>是否为 &lt;Module&gt; 伪类型（TypeDef 表首行，没有任何成员语义）。</summary>
    private static bool IsModulePseudoType(MetadataReader mr, TypeDefinition td) =>
        mr.StringComparer.Equals(td.Name, "<Module>");


    /// <summary>是否可作为入口：public、非抽象、且有 public 无参构造。</summary>
    private static bool IsInstantiablePublicClass(MetadataReader mr, TypeDefinition td)
    {
        TypeAttributes attrs = td.Attributes;
        if ((attrs & TypeAttributes.Abstract) != 0) return false;
        if ((attrs & TypeAttributes.VisibilityMask) != TypeAttributes.Public) return false;

        foreach (MethodDefinitionHandle handle in td.GetMethods())
        {
            MethodDefinition md = mr.GetMethodDefinition(handle);
            if (!mr.StringComparer.Equals(md.Name, ".ctor")) continue;
            if ((md.Attributes & MethodAttributes.Public) == 0) continue;
            if (md.GetParameters().Count == 0) return true;
        }
        return false;
    }

    private static void ReadAssemblyReferences(MetadataReader mr, AssemblyFacts facts)
    {
        foreach (AssemblyReferenceHandle handle in mr.AssemblyReferences)
        {
            AssemblyReference reference = mr.GetAssemblyReference(handle);
            string name = mr.GetString(reference.Name);

            if (string.Equals(name, PluginApi.AbstractionsAssemblyName, StringComparison.Ordinal))
            {
                facts.ReferencesAbstractions = true;
                facts.ReferencedAbstractionsVersion = reference.Version.ToString();
            }
        }
    }

    // ------------------------------------------------------------------ G-3 契约

    private static bool CheckContract(PluginScanResult result, PluginManifest manifest, AssemblyFacts facts)
    {
        // ① 实际 TFM 不高于宿主？（以程序集真实元数据为准，不信任清单手写值）
        if (facts.NetMajor > HostNetMajor)
        {
            Fail(result, PluginScanFailure.TargetFrameworkMismatch,
                $"程序集实际目标为 .NET {facts.NetMajor}.{facts.NetMinor}，高于宿主支持的 .NET {HostNetMajor}。");
            return false;
        }
        if (facts.NetMajor == HostNetMajor && facts.NetMinor > 0)
        {
            Fail(result, PluginScanFailure.TargetFrameworkMismatch,
                $"程序集实际目标为 .NET {facts.NetMajor}.{facts.NetMinor}，高于宿主支持的 .NET {HostNetMajor}.0。");
            return false;
        }
        if (facts.NetMajor == HostNetMajor && facts.IsPlatformVersionHigherThan(HostPlatformVersion))
        {
            Fail(result, PluginScanFailure.TargetFrameworkMismatch,
                $"程序集要求 Windows 平台版本 {facts.TargetPlatformVersion}，高于宿主的 {HostPlatformVersion}。");
            return false;
        }

        // ② 引用了 SDK 契约吗？
        if (!facts.ReferencesAbstractions)
        {
            Fail(result, PluginScanFailure.NoContractImplementation,
                "程序集没有引用 StarPie.Plugin.Abstractions，不可能是 StarPie 插件。");
            return false;
        }

        // ③ 契约程序集版本身份
        string hostAbstractionsVersion = typeof(IStarPiePlugin).Assembly.GetName().Version?.ToString() ?? "";
        if (!string.IsNullOrEmpty(facts.ReferencedAbstractionsVersion)
            && SimpleVersion.TryParse(facts.ReferencedAbstractionsVersion, out SimpleVersion referenced)
            && referenced.Major != PluginApi.ApiVersionMajor)
        {
            Fail(result, PluginScanFailure.ApiVersionMismatch,
                $"程序集引用的 SDK 契约主版本为 {referenced.Major}.x，宿主提供 {hostAbstractionsVersion}。");
            return false;
        }

        // ④ 插件目录自带 SDK 副本？（打包失误，会造成类型身份分裂）
        string bundledSdk = Path.Combine(result.SourceDirectory, PluginApi.AbstractionsAssemblyName + ".dll");
        if (File.Exists(bundledSdk))
        {
            string? bundledVersion = TryGetFileAssemblyVersion(bundledSdk);
            if (!string.IsNullOrEmpty(bundledVersion) && bundledVersion != hostAbstractionsVersion)
            {
                Fail(result, PluginScanFailure.ContractAssemblyVersionMismatch,
                    $"插件目录自带的 {PluginApi.AbstractionsAssemblyName}.dll 版本为 {bundledVersion}，宿主为 {hostAbstractionsVersion}。");
                return false;
            }
            AppLogger.LogWarn(
                $"[plugin] {manifest.Id} 目录内自带 {PluginApi.AbstractionsAssemblyName}.dll，加载时会被忽略（由宿主统一提供）。建议打包时排除。");
        }

        // ⑤ 入口类型判定
        if (!string.IsNullOrWhiteSpace(manifest.EntryType))
        {
            string wanted = manifest.EntryType!.Trim();
            string? resolved = facts.ContractTypes.Find(t => string.Equals(t, wanted, StringComparison.Ordinal));
            if (resolved == null)
            {
                Fail(result, PluginScanFailure.EntryTypeNotFound,
                    $"entryType=\"{wanted}\" 在程序集中不存在。实际实现 IStarPiePlugin 的类型：{DescribeList(facts.ContractTypes)}。");
                return false;
            }
            if (!facts.EntryCandidates.Contains(resolved))
            {
                Fail(result, PluginScanFailure.EntryTypeNotFound,
                    $"entryType=\"{wanted}\" 不是 public 具体类，或缺少 public 无参构造函数。");
                return false;
            }
            facts.EntryTypeResolved = resolved;
        }
        else
        {
            if (facts.EntryCandidates.Count == 0)
            {
                if (facts.ContractTypes.Count > 0)
                {
                    Fail(result, PluginScanFailure.EntryTypeNotFound,
                        $"找到实现 IStarPiePlugin 的类型，但它不是 public 具体类或缺少 public 无参构造：{DescribeList(facts.ContractTypes)}。");
                }
                else
                {
                    Fail(result, PluginScanFailure.NoContractImplementation,
                        "程序集里找不到任何 IStarPiePlugin 的实现类，它不是 StarPie 插件。");
                }
                return false;
            }

            if (facts.EntryCandidates.Count > 1)
            {
                Fail(result, PluginScanFailure.AmbiguousContractImplementation,
                    $"程序集里有 {facts.EntryCandidates.Count} 个 IStarPiePlugin 实现：{DescribeList(facts.EntryCandidates)}。请在 plugin.json 用 entryType 指定入口类全名。");
                return false;
            }

            facts.EntryTypeResolved = facts.EntryCandidates[0];
        }

        result.EntryTypeFullName = facts.EntryTypeResolved;
        return true;
    }

    // ------------------------------------------------------------------ G-4 完整性

    private static bool CheckIntegrity(PluginScanResult result, PluginManifest manifest, string dllPath)
    {
        result.Sha256 = ComputeSha256(dllPath);

        if (!string.IsNullOrWhiteSpace(manifest.Sha256))
        {
            string expected = manifest.Sha256!.Trim();
            if (expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
            {
                expected = expected.Substring("sha256:".Length);
            }

            if (!string.Equals(expected, result.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                Fail(result, PluginScanFailure.Sha256Mismatch,
                    $"清单声明 {Shorten(expected)}，实际 {result.Sha256Short}。");
                return false;
            }
        }

        // Authenticode 签名：只做「有没有 + 是谁」的展示，不作为加载前置条件（决策 Q2：推荐不强制）
        try
        {
            using X509Certificate signed = X509Certificate.CreateFromSignedFile(dllPath);
            result.IsSigned = true;
            result.SignerSubject = signed.Subject;
            result.SignerThumbprint = signed.GetCertHashString();
        }
        catch
        {
            result.IsSigned = false;
            result.SignerSubject = null;
            result.SignerThumbprint = null;
        }

        return true;
    }

    public static string ComputeSha256(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var sha = SHA256.Create();
            return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
        }
        catch
        {
            return "";
        }
    }

    // ------------------------------------------------------------------ 元数据工具

    private static string? GetAttributeConstructorOwner(MetadataReader mr, EntityHandle constructor)
    {
        switch (constructor.Kind)
        {
            case HandleKind.MemberReference:
                MemberReference memberReference = mr.GetMemberReference((MemberReferenceHandle)constructor);
                return GetTypeFullName(mr, memberReference.Parent);

            case HandleKind.MethodDefinition:
                MethodDefinition method = mr.GetMethodDefinition((MethodDefinitionHandle)constructor);
                return GetTypeFullName(mr, method.GetDeclaringType());

            default:
                return null;
        }
    }

    private static string? GetTypeFullName(MetadataReader mr, EntityHandle handle)
    {
        switch (handle.Kind)
        {
            case HandleKind.TypeReference:
            {
                TypeReference tr = mr.GetTypeReference((TypeReferenceHandle)handle);
                string ns = mr.GetString(tr.Namespace);
                string name = mr.GetString(tr.Name);
                return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
            }
            case HandleKind.TypeDefinition:
                return GetTypeFullName(mr, (TypeDefinitionHandle)handle);
            default:
                return null;
        }
    }

    private static string GetTypeFullName(MetadataReader mr, TypeDefinitionHandle handle)
    {
        TypeDefinition td = mr.GetTypeDefinition(handle);
        string ns = mr.GetString(td.Namespace);
        string name = mr.GetString(td.Name);
        return string.IsNullOrEmpty(ns) ? name : ns + "." + name;
    }

    /// <summary>读取只有一个固定字符串参数的属性（TargetFramework / Company / Product 都是这个形状）。</summary>
    private static string? ReadSingleStringArgument(MetadataReader mr, CustomAttribute ca)
    {
        try
        {
            BlobReader reader = mr.GetBlobReader(ca.Value);
            if (reader.RemainingBytes < 2) return null;
            if (reader.ReadUInt16() != 0x0001) return null;
            return reader.ReadSerializedString();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>读取 (string, string) 形状的属性，即 <c>AssemblyMetadataAttribute</c>。</summary>
    private static bool TryReadStringPair(MetadataReader mr, CustomAttribute ca, out string key, out string value)
    {
        key = "";
        value = "";
        try
        {
            BlobReader reader = mr.GetBlobReader(ca.Value);
            if (reader.RemainingBytes < 2) return false;
            if (reader.ReadUInt16() != 0x0001) return false;
            key = reader.ReadSerializedString() ?? "";
            value = reader.ReadSerializedString() ?? "";
            return key.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static string? TryGetFileAssemblyVersion(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var pe = new PEReader(stream);
            if (!pe.HasMetadata) return null;
            return pe.GetMetadataReader().GetAssemblyDefinition().Version.ToString();
        }
        catch
        {
            return null;
        }
    }

    private static string TrimBuildMetadata(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        int plus = value!.IndexOf('+');
        return plus >= 0 ? value.Substring(0, plus) : value;
    }

    private static List<string> SplitCapabilities(string? raw)
    {
        var list = new List<string>();
        if (string.IsNullOrWhiteSpace(raw)) return list;
        foreach (string part in raw!.Split(new[] { ';', ',', '|' }, StringSplitOptions.RemoveEmptyEntries))
        {
            string trimmed = part.Trim();
            if (trimmed.Length > 0) list.Add(trimmed);
        }
        return list;
    }

    private static string DescribeMachine(AssemblyFacts facts) => facts.Machine switch
    {
        Machine.Amd64 => "x64",
        Machine.I386 => "AnyCPU",
        Machine.Arm64 => "ARM64",
        _ => facts.Machine.ToString(),
    };

    private static string DescribeSize(string dllPath)
    {
        try
        {
            long size = new FileInfo(dllPath).Length;
            if (size < 1024) return $"{size} B";
            if (size < 1024 * 1024) return $"{size / 1024.0:F1} KB";
            return $"{size / 1024.0 / 1024.0:F2} MB";
        }
        catch
        {
            return "未知";
        }
    }

    private static bool HasDependencyFile(string dllPath)
    {
        try
        {
            string dir = Path.GetDirectoryName(dllPath) ?? "";
            string name = Path.GetFileNameWithoutExtension(dllPath);
            return File.Exists(Path.Combine(dir, name + ".deps.json"));
        }
        catch
        {
            return false;
        }
    }

    private static string DescribeList(List<string> items) =>
        items.Count == 0 ? "（无）" : string.Join("、", items);

    private static string Shorten(string hash) => hash.Length >= 12 ? hash.Substring(0, 12) : hash;

    private static PluginScanResult Fail(PluginScanResult result, PluginScanFailure code, string detail)
    {
        result.Accepted = false;
        result.Failure = code;
        result.ErrorDetail = detail;
        AppLogger.LogWarn(
            $"[plugin] 识别失败 ({code}) {Path.GetFileName(result.DllPath)}：{detail}");
        return result;
    }

    // ------------------------------------------------------------------ 事实容器

    /// <summary>一枚程序集的静态事实。全部来自 PE 元数据，不含任何运行时对象。</summary>
    private sealed class AssemblyFacts
    {
        public Machine Machine;
        public bool IlOnly;
        public bool Requires32Bit;

        public string? AssemblyName;
        public string? AssemblyVersion;
        public string? AssemblyInformationalVersion;
        public string? AssemblyCompany;
        public string? AssemblyProduct;
        public string? AssemblyTitle;
        public string? AssemblyDescription;

        public string? DotNetCoreAppVersion;
        public string? TargetPlatformVersion;
        public string? TargetFrameworkRaw;
        public int NetMajor = HostNetMajor;
        public int NetMinor = 0;

        public string? MetadataId;
        public string? MetadataName;
        public string? MetadataCapabilities;
        public string? MetadataLicense;
        public string? MetadataHomepage;

        public readonly List<string> ContractTypes = new();
        public readonly List<string> EntryCandidates = new();
        public string? EntryTypeResolved;

        public bool ReferencesAbstractions;
        public string? ReferencedAbstractionsVersion;

        /// <summary>程序集声明的平台版本是否高于给定版本。</summary>
        public bool IsPlatformVersionHigherThan(string reference)
        {
            if (string.IsNullOrWhiteSpace(TargetPlatformVersion)) return false;

            string text = TargetPlatformVersion!.StartsWith("Windows", StringComparison.OrdinalIgnoreCase)
                ? TargetPlatformVersion.Substring("Windows".Length)
                : TargetPlatformVersion;

            return Version.TryParse(text, out Version? mine)
                && Version.TryParse(reference, out Version? other)
                && mine > other;
        }
    }
}
