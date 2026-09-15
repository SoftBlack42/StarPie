using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace WinPieGestures.Plugins;

/// <summary><c>registry.json</c> 根对象。宿主运维数据，与用户的 <c>config.json</c> 完全分离。</summary>
internal sealed class PluginRegistryFile
{
    public int RegistryVersion { get; set; } = 1;
    public string? UpdatedAt { get; set; }
    public List<PluginRegistryEntry> Entries { get; set; } = new();
}

/// <summary>单个插件的安装与启用记录。</summary>
internal sealed class PluginRegistryEntry
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Author { get; set; } = "";
    public string License { get; set; } = "";
    public string? Homepage { get; set; }

    /// <summary>相对 <see cref="PluginPaths.Root"/> 的安装目录名；默认等于 <see cref="Id"/>。</summary>
    public string InstallPath { get; set; } = "";

    /// <summary>开发者模式下的外部路径（不复制文件）。非空时以它为准。</summary>
    public string? ExternalPath { get; set; }

    public bool Enabled { get; set; }

    /// <summary>是否随主程序启动预加载。默认 false（惰性，守住内存红线 R1）。</summary>
    public bool Preload { get; set; }

    public string? EntrySha256 { get; set; }
    public string? SignerThumbprint { get; set; }

    /// <summary>用户已确认过的能力集合。升级后若新增高风险能力，需要重新确认。</summary>
    public List<string> CapabilitiesAck { get; set; } = new();

    public string? AckedAt { get; set; }
    public string? AckedHostVersion { get; set; }

    /// <summary>安装来源：<c>UserSelectedFile</c> / <c>UserSelectedFolder</c> / <c>DeveloperPath</c> / <c>Discovered</c>。</summary>
    public string Source { get; set; } = "UserSelectedFile";

    public string? InstalledAt { get; set; }

    public PluginRegistryEntry Clone() => new()
    {
        Id = Id,
        Name = Name,
        Version = Version,
        Description = Description,
        Author = Author,
        License = License,
        Homepage = Homepage,
        InstallPath = InstallPath,
        ExternalPath = ExternalPath,
        Enabled = Enabled,
        Preload = Preload,
        EntrySha256 = EntrySha256,
        SignerThumbprint = SignerThumbprint,
        CapabilitiesAck = new List<string>(CapabilitiesAck),
        AckedAt = AckedAt,
        AckedHostVersion = AckedHostVersion,
        Source = Source,
        InstalledAt = InstalledAt,
    };
}

/// <summary><c>health.json</c> 根对象。高频写，必须与用户配置分离。</summary>
internal sealed class PluginHealthFile
{
    /// <summary>连续启动失败次数（用于安全模式判定）。</summary>
    public int ConsecutiveStartupFailures { get; set; }

    /// <summary>上一次启动时实际加载成功的插件集合。安全模式触发时会被整体禁用。</summary>
    public List<string> LastStartupPluginSet { get; set; } = new();

    /// <summary>安全模式截止时间（ISO 8601）。非空且未过期时，启动不加载任何插件。</summary>
    public string? SafeModeUntil { get; set; }

    public Dictionary<string, PluginHealthEntry> Plugins { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>单个插件的健康度计数。</summary>
internal sealed class PluginHealthEntry
{
    public int LoadCount { get; set; }
    public int ConsecutiveFailures { get; set; }
    public int WindowFailures { get; set; }

    /// <summary>动作调用总次数与失败次数（用于插件页展示成功率）。</summary>
    public long InvokeCount { get; set; }
    public long InvokeFailureCount { get; set; }

    /// <summary>动作调用累计耗时（毫秒），用于算平均值。</summary>
    public double TotalInvokeMs { get; set; }

    /// <summary>渲染回调超帧次数（P1 渲染插件用）。</summary>
    public int RenderFrameTimeouts { get; set; }

    public string? LastError { get; set; }

    /// <summary>是否需要在下次重启后才能彻底生效（ALC 卸载不彻底时置位）。</summary>
    public bool RequiresRestart { get; set; }

    public double AverageInvokeMs => InvokeCount <= 0 ? 0 : TotalInvokeMs / InvokeCount;
}

/// <summary>
/// <c>registry.json</c> / <c>health.json</c> 的读写器。
/// <para>
/// 设计要点：① 所有写入串行化（避免重蹈主配置 H1 的无锁竞态）；② 原子写（临时文件 + Replace）；
/// ③ 文件损坏时备份后重建，绝不因为运维数据损坏而让整个插件系统不可用。
/// </para>
/// </summary>
internal static class PluginRegistryStore
{
    private static readonly object Gate = new();

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static PluginRegistryFile? _registryCache;
    private static PluginHealthFile? _healthCache;

    // ---------------------------------------------------------------- registry

    public static PluginRegistryFile Registry
    {
        get
        {
            lock (Gate)
            {
                return _registryCache ??= Load<PluginRegistryFile>(PluginPaths.RegistryFile) ?? new PluginRegistryFile();
            }
        }
    }

    public static PluginRegistryEntry? FindEntry(string pluginId)
    {
        lock (Gate)
        {
            return Registry.Entries.Find(e => string.Equals(e.Id, pluginId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static List<PluginRegistryEntry> SnapshotEntries()
    {
        lock (Gate)
        {
            var list = new List<PluginRegistryEntry>(Registry.Entries.Count);
            foreach (PluginRegistryEntry e in Registry.Entries) list.Add(e.Clone());
            return list;
        }
    }

    /// <summary>插入或整体替换一条记录。</summary>
    public static void UpsertEntry(PluginRegistryEntry entry)
    {
        lock (Gate)
        {
            PluginRegistryFile file = Registry;
            int index = file.Entries.FindIndex(e => string.Equals(e.Id, entry.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) file.Entries[index] = entry;
            else file.Entries.Add(entry);
            SaveRegistryLocked();
        }
    }

    public static bool RemoveEntry(string pluginId)
    {
        lock (Gate)
        {
            PluginRegistryFile file = Registry;
            int removed = file.Entries.RemoveAll(e => string.Equals(e.Id, pluginId, StringComparison.OrdinalIgnoreCase));
            if (removed == 0) return false;
            SaveRegistryLocked();
            return true;
        }
    }

    /// <summary>设置启用状态并落盘。返回是否真的发生了变化。</summary>
    public static bool SetEnabled(string pluginId, bool enabled)
    {
        lock (Gate)
        {
            PluginRegistryEntry? entry = FindEntry(pluginId);
            if (entry == null) return false;
            if (entry.Enabled == enabled) return false;
            entry.Enabled = enabled;
            SaveRegistryLocked();
            return true;
        }
    }

    public static void SetPreload(string pluginId, bool preload)
    {
        lock (Gate)
        {
            PluginRegistryEntry? entry = FindEntry(pluginId);
            if (entry == null || entry.Preload == preload) return;
            entry.Preload = preload;
            SaveRegistryLocked();
        }
    }

    /// <summary>重置整个插件系统（删除 registry 与 health，保留插件目录）。</summary>
    public static void ResetAll()
    {
        lock (Gate)
        {
            _registryCache = new PluginRegistryFile();
            _healthCache = new PluginHealthFile();
            SaveRegistryLocked();
            SaveHealthLocked();
        }
    }

    private static void SaveRegistryLocked()
    {
        PluginRegistryFile file = Registry;
        file.UpdatedAt = DateTimeOffset.Now.ToString("yyyy-MM-ddTHH:mm:sszzz");
        WriteAtomic(PluginPaths.RegistryFile, JsonSerializer.Serialize(file, WriteOptions));
    }

    // ------------------------------------------------------------------ health

    public static PluginHealthFile Health
    {
        get
        {
            lock (Gate)
            {
                return _healthCache ??= Load<PluginHealthFile>(PluginPaths.HealthFile) ?? new PluginHealthFile();
            }
        }
    }

    public static PluginHealthEntry HealthOf(string pluginId)
    {
        lock (Gate)
        {
            PluginHealthFile file = Health;
            if (!file.Plugins.TryGetValue(pluginId, out PluginHealthEntry? entry))
            {
                entry = new PluginHealthEntry();
                file.Plugins[pluginId] = entry;
            }
            return entry;
        }
    }

    /// <summary>修改健康度并立即落盘。传入的操作在锁内执行，保证读改写原子。</summary>
    public static void MutateHealth(string pluginId, Action<PluginHealthEntry> mutate)
    {
        lock (Gate)
        {
            PluginHealthEntry entry = HealthOf(pluginId);
            mutate(entry);
            SaveHealthLocked();
        }
    }

    public static void MutateHealthFile(Action<PluginHealthFile> mutate)
    {
        lock (Gate)
        {
            mutate(Health);
            SaveHealthLocked();
        }
    }

    private static void SaveHealthLocked() =>
        WriteAtomic(PluginPaths.HealthFile, JsonSerializer.Serialize(Health, WriteOptions));

    // ------------------------------------------------------------------ shared

    private static T? Load<T>(string path) where T : class
    {
        try
        {
            if (!File.Exists(path)) return null;
            string json = File.ReadAllText(path);
            if (string.IsNullOrWhiteSpace(json)) return null;
            return JsonSerializer.Deserialize<T>(json, ReadOptions);
        }
        catch
        {
            // 运维数据损坏不值得让插件系统整体瘫痪：备份后重建
            try
            {
                string backup = path + $".corrupt-{DateTime.Now:yyyyMMddHHmmss}";
                File.Move(path, backup, overwrite: true);
                AppLogger.LogWarn($"[plugin] 运维数据损坏，已备份到 {backup} 并重建：{path}");
            }
            catch { }
            return null;
        }
    }

    /// <summary>原子写：先落临时文件再替换，避免中途中断留下截断文件。</summary>
    private static void WriteAtomic(string path, string contents)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string temp = path + ".tmp";
            File.WriteAllText(temp, contents);
            if (File.Exists(path))
            {
                File.Replace(temp, path, null);
            }
            else
            {
                File.Move(temp, path, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin] 写入 {path} 失败", ex);
        }
    }
}
