using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件专属日志。
/// <para>
/// 三个设计要点：
/// ① <b>分文件</b> —— 插件日志绝不能和主日志混在一起，否则用户排障时得在几万行里捞；
/// ② <b>限流</b> —— 插件可以在渲染回调里打日志，没有限流就能在一个下午把磁盘写满。超限丢弃并只提示一次；
/// ③ <b>不抛异常</b> —— 日志写失败绝不能影响插件与主程序的正常运行。
/// </para>
/// </summary>
internal sealed class PluginLogger : IPluginLogger
{
    /// <summary>每分钟最多写入的行数。超限部分直接丢弃。</summary>
    private const int MaxLinesPerMinute = 200;

    /// <summary>单个日志文件的软上限。超过后本次会话不再写该文件，只向主日志发一条告警。</summary>
    private const long MaxFileBytes = 5L * 1024 * 1024;

    private readonly string _pluginId;
    private readonly object _gate = new();

    private int _windowCount;
    private DateTime _windowStartUtc = DateTime.UtcNow;
    private bool _rateLimitNotified;
    private bool _sizeLimitNotified;
    private long _droppedLines;

    public PluginLogger(string pluginId)
    {
        _pluginId = pluginId;
        LogFilePath = PluginPaths.GetLogFilePath(pluginId);
    }

    public string LogFilePath { get; }

    /// <summary>单位窗口内被丢弃的行数（诊断用）。</summary>
    public long DroppedLines => Interlocked.Read(ref _droppedLines);

    public void Debug(string message) => Write("DEBUG", message, null);
    public void Info(string message) => Write("INFO", message, null);
    public void Warn(string message) => Write("WARN", message, null);
    public void Error(string message, Exception? exception = null) => Write("ERROR", message, exception);

    private void Write(string level, string? message, Exception? exception, bool mirrorToMainLog = true)
    {
        try
        {
            lock (_gate)
            {
                if (!TryConsumeQuota(level, message)) return;

                var sb = new StringBuilder(160);
                sb.Append('[').Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append("] ");
                sb.Append('[').Append(level.PadRight(5)).Append("] ");
                sb.Append(message ?? "");

                if (exception != null)
                {
                    sb.AppendLine();
                    sb.Append("    [Exception]: ").Append(exception.GetType().FullName).Append(": ").Append(exception.Message);
                    sb.AppendLine();
                    sb.Append("    [StackTrace]: ").Append(exception.StackTrace);
                }

                string line = sb.ToString();

                try
                {
                    string? dir = Path.GetDirectoryName(LogFilePath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    var info = new FileInfo(LogFilePath);
                    if (info.Exists && info.Length >= MaxFileBytes)
                    {
                        if (!_sizeLimitNotified)
                        {
                            _sizeLimitNotified = true;
                            AppLogger.LogWarn($"[plugin:{_pluginId}] 日志文件已达 5MB 上限，本次会话停止写入：{LogFilePath}");
                        }
                    }
                    else
                    {
                        File.AppendAllText(LogFilePath, line + Environment.NewLine, Encoding.UTF8);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[PluginLogger] 写入失败: {ex.Message}");
                }

                // WARN / ERROR 同时镜像到主日志，方便用户只发一个文件就能定位问题
                if (mirrorToMainLog && (level == "ERROR" || level == "WARN"))
                {
                    if (level == "ERROR")
                    {
                        AppLogger.LogError($"[plugin:{_pluginId}] {message}", exception);
                    }
                    else
                    {
                        AppLogger.LogWarn($"[plugin:{_pluginId}] {message}");
                    }
                }
            }
        }
        catch
        {
            // 日志系统本身永不抛异常
        }
    }

    /// <summary>令牌桶式限流：滑动一分钟窗口。</summary>
    private bool TryConsumeQuota(string level, string? message)
    {
        DateTime now = DateTime.UtcNow;
        if ((now - _windowStartUtc).TotalSeconds >= 60)
        {
            _windowStartUtc = now;
            _windowCount = 0;
            _rateLimitNotified = false;
        }

        if (_windowCount < MaxLinesPerMinute)
        {
            _windowCount++;
            return true;
        }

        Interlocked.Increment(ref _droppedLines);

        if (!_rateLimitNotified)
        {
            _rateLimitNotified = true;
            // 这条提示本身绕过配额直接吐给主日志，否则它也会被限流掉
            AppLogger.LogWarn(
                $"[plugin:{_pluginId}] 日志写入超过 {MaxLinesPerMinute} 行/分钟，已开始丢弃超额日志以保护磁盘。" +
                $"请检查插件是否在渲染回调里打日志。（本条提示每分钟只出现一次）");
        }

        _ = level;
        _ = message;
        return false;
    }

    /// <summary>清理由卸载插件留下的旧日志（保留 7 天，与主日志口径一致）。</summary>
    public static void CleanOldPluginLogs(int maxAgeDays = 7)
    {
        try
        {
            string dir = PluginPaths.LogDirectory;
            if (!Directory.Exists(dir)) return;

            DateTime cutoff = DateTime.Now.AddDays(-maxAgeDays);
            foreach (string file in Directory.GetFiles(dir, "*.log"))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.LastWriteTime < cutoff) info.Delete();
                }
                catch
                {
                }
            }
        }
        catch
        {
        }
    }

    /// <summary>列出某个插件的全部日志文件路径（用于「查看日志」入口）。</summary>
    public static List<string> GetLogFiles(string pluginId)
    {
        var result = new List<string>();
        try
        {
            string safe = PluginPaths.SanitizeForFileName(pluginId);
            string dir = PluginPaths.LogDirectory;
            if (!Directory.Exists(dir)) return result;

            result.AddRange(Directory.GetFiles(dir, safe + "_*.log"));
            result.Sort(StringComparer.OrdinalIgnoreCase);
            result.Reverse();
        }
        catch
        {
        }
        return result;
    }
}
