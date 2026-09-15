using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件私有配置（<c>plugin-data\&lt;id&gt;\settings.json</c>）。
/// <para>
/// 与主 <c>config.json</c> 彻底隔离的理由（设计决策 D6）：
/// 主配置被钩子线程高频读取且写盘无锁，把插件数据并进去会直接放大既有的 H1 竞态；
/// 而且插件启停是相对高频操作，不应反复触发 97+ 键主配置的整份序列化。
/// </para>
/// <para>
/// 值一律用字符串：既避免插件自定义类型进入持久化层，也天然强制插件在边界做类型转换与校验。
/// </para>
/// </summary>
internal sealed class PluginSettings : IPluginSettings
{
    private readonly string _path;
    private readonly object _gate = new();
    private Dictionary<string, string> _values;

    public PluginSettings(string pluginId)
    {
        _path = PluginPaths.GetSettingsPath(pluginId);
        _values = Load();
    }

    public string? Get(string key)
    {
        if (string.IsNullOrEmpty(key)) return null;
        lock (_gate)
        {
            return _values.TryGetValue(key, out string? value) ? value : null;
        }
    }

    public void Set(string key, string? value)
    {
        if (string.IsNullOrEmpty(key)) return;

        // 防止插件用巨型字符串把 settings.json 撑爆（与主配置的 LOH 防护同一口径）
        if (value != null && value.Length > PluginApi.MaxParameterValueLength)
        {
            value = value.Substring(0, PluginApi.MaxParameterValueLength);
        }

        lock (_gate)
        {
            if (value == null) _values.Remove(key);
            else _values[key] = value;
        }
    }

    public bool GetBool(string key, bool defaultValue = false) =>
        bool.TryParse(Get(key), out bool value) ? value : defaultValue;

    public int GetInt(string key, int defaultValue = 0) =>
        int.TryParse(Get(key), out int value) ? value : defaultValue;

    public double GetDouble(string key, double defaultValue = 0) =>
        double.TryParse(Get(key), out double value) ? value : defaultValue;

    public void Save()
    {
        string json;
        lock (_gate)
        {
            json = JsonSerializer.Serialize(_values, new JsonSerializerOptions { WriteIndented = true });
        }

        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            if (File.Exists(_path))
            {
                File.Replace(temp, _path, null);
            }
            else
            {
                File.Move(temp, _path, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin] 保存插件配置失败：{_path}", ex);
        }
    }

    private Dictionary<string, string> Load()
    {
        try
        {
            if (!File.Exists(_path)) return new Dictionary<string, string>(StringComparer.Ordinal);
            string json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return new Dictionary<string, string>(StringComparer.Ordinal);

            return JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin] 插件配置损坏，已忽略并使用默认值：{_path}（{ex.Message}）");
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}
