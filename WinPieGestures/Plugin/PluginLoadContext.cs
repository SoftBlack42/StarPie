using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 每个插件一个独立的可回收加载上下文。
/// <para>
/// <b>它提供的是「故障隔离」，不是「安全隔离」。</b>进程内的 .NET 插件没有安全边界
/// （CAS 早已废弃），这一点必须对用户和作者都讲清楚，不能靠 ALC 假装出沙箱。
/// </para>
/// <para>
/// <b>全部难点集中在 <see cref="Load"/> 的判定顺序上</b>：
/// 如果插件自带一份 <c>StarPie.Plugin.Abstractions.dll</c> 并被加载进它自己的 ALC，
/// 进程内就会出现<b>两份 IStarPiePlugin 类型身份</b>，于是 <c>as</c> / 强转<b>全部静默失败</b>，
/// 表现为「插件加载成功但什么都没注册」—— 这是进程内插件最经典的翻车方式。
/// </para>
/// </summary>
internal sealed class PluginLoadContext : AssemblyLoadContext
{
    private readonly AssemblyDependencyResolver? _resolver;
    private readonly string _pluginDirectory;
    private readonly List<string> _resolvedFromPluginDirectory = new();

    /// <summary>
    /// 必须由宿主提供、绝不允许插件目录覆盖的核心程序集。
    /// 这份名单除了「避免类型身份分裂」，还有一个作用：防止插件放一个假的 <c>WindowsBase.dll</c> 劫持 WPF 类型。
    /// </summary>
    private static readonly HashSet<string> HostOwnedAssemblies = new(StringComparer.OrdinalIgnoreCase)
    {
        "mscorlib",
        "netstandard",
        "System.Private.CoreLib",
        "System.Runtime",
        "WindowsBase",
        "PresentationCore",
        "PresentationFramework",
        "WindowsFormsIntegration",
        "System.Windows.Forms",
        "System.Drawing",
        "System.Drawing.Common",
        "Accessibility",
        "UIAutomationTypes",
        "UIAutomationProvider",
        "UIAutomationClientsideProviders",
    };

    public PluginLoadContext(string pluginId, string pluginDirectory, string mainAssemblyPath)
        : base($"StarPie.Plugin:{pluginId}", isCollectible: true)
    {
        _pluginDirectory = pluginDirectory;

        try
        {
            _resolver = File.Exists(mainAssemblyPath) ? new AssemblyDependencyResolver(mainAssemblyPath) : null;
        }
        catch (Exception ex)
        {
            // 缺少 deps.json 时 AssemblyDependencyResolver 仍可构造；真正失败则退化为「目录探测」模式
            AppLogger.LogWarn($"[plugin] {pluginId} 无法构造 AssemblyDependencyResolver（{ex.Message}），改用目录探测模式。");
            _resolver = null;
        }
    }

    /// <summary>从插件目录成功解析出的程序集名，用于诊断与日志。</summary>
    public IReadOnlyList<string> ResolvedFromPluginDirectory => _resolvedFromPluginDirectory;

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        string? simpleName = assemblyName.Name;
        if (string.IsNullOrEmpty(simpleName)) return null;

        // ① SDK 契约：必须由宿主默认上下文提供（决策核心，见类注释）
        if (string.Equals(simpleName, PluginApi.AbstractionsAssemblyName, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        // ② 宿主拥有的核心程序集：直接放行，绝不给插件目录覆盖的机会
        if (HostOwnedAssemblies.Contains(simpleName))
        {
            return null;
        }

        // ③ 宿主默认上下文已经能提供的（共享框架、主程序集），一律共享，
        //    避免同一程序集出现两份类型身份。
        if (HostCanProvide(assemblyName))
        {
            return null;
        }

        // ④ 到这里说明是插件私有依赖，才允许从插件目录加载
        string? path = null;

        try
        {
            path = _resolver?.ResolveAssemblyToPath(assemblyName);
        }
        catch
        {
            path = null;
        }

        path ??= ProbePluginDirectory(simpleName);

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            _resolvedFromPluginDirectory.Add(simpleName);
            return LoadFromAssemblyPath(path);
        }

        // ⑤ 兜底：交回默认上下文再试一次（可能解析到共享框架里的同名程序集）
        return null;
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        string? path = null;
        try
        {
            path = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        }
        catch
        {
            path = null;
        }

        if (string.IsNullOrEmpty(path))
        {
            path = ProbePluginDirectory(unmanagedDllName)
                   ?? ProbeNativeFolders(unmanagedDllName);
        }

        if (!string.IsNullOrEmpty(path) && File.Exists(path))
        {
            return LoadUnmanagedDllFromPath(path);
        }

        return IntPtr.Zero;
    }

    /// <summary>探测宿主默认上下文能否解析该程序集。</summary>
    private static bool HostCanProvide(AssemblyName assemblyName)
    {
        // 先看已经加载的，避免为「已加载」这种情况也走一次异常路径
        foreach (System.Reflection.Assembly loaded in Default.Assemblies)
        {
            if (string.Equals(loaded.GetName().Name, assemblyName.Name, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        try
        {
            Default.LoadFromAssemblyName(assemblyName);
            return true;
        }
        catch
        {
            // 默认上下文解析不到 → 它才是插件的私有依赖
            return false;
        }
    }

    /// <summary>在插件目录内探测程序集（含 runtimes\win-x64\lib 与 native 子目录）。</summary>
    private string? ProbePluginDirectory(string fileName)
    {
        string bare = Path.GetFileNameWithoutExtension(fileName);

        string[] candidates =
        {
            Path.Combine(_pluginDirectory, bare + ".dll"),
            Path.Combine(_pluginDirectory, fileName),
            Path.Combine(_pluginDirectory, "runtimes", "win-x64", "lib", "net8.0", bare + ".dll"),
            Path.Combine(_pluginDirectory, "runtimes", "win-x64", "native", fileName),
            Path.Combine(_pluginDirectory, "runtimes", "win-x64", "native", bare + ".dll"),
        };

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
                // 忽略非法路径
            }
        }

        return null;
    }

    /// <summary>原生依赖的额外探测目录。</summary>
    private string? ProbeNativeFolders(string fileName)
    {
        string bare = Path.GetFileNameWithoutExtension(fileName);
        string[] candidates =
        {
            Path.Combine(_pluginDirectory, "native", fileName),
            Path.Combine(_pluginDirectory, "native", bare + ".dll"),
        };

        foreach (string candidate in candidates)
        {
            try
            {
                if (File.Exists(candidate)) return candidate;
            }
            catch
            {
            }
        }

        return null;
    }
}
