using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>托盘气泡的注入点。由宿主 UI 层设置，插件侧永远见不到具体实现。</summary>
internal static class PluginNotificationHub
{
    /// <summary>参数为 (标题, 内容)。为 null 时通知降级为写日志。</summary>
    public static Action<string, string>? Sink;
}

/// <summary>非侵入式通知。宿主没有可用托盘时<b>静默降级为日志</b>，绝不弹 MessageBox（会阻塞动作线程）。</summary>
internal sealed class PluginNotificationService : INotificationService
{
    private readonly string _pluginId;

    public PluginNotificationService(string pluginId) => _pluginId = pluginId;

    public void Notify(string title, string message)
    {
        try
        {
            Action<string, string>? sink = PluginNotificationHub.Sink;
            if (sink != null)
            {
                sink(title ?? "", message ?? "");
                return;
            }
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin:{_pluginId}] 通知发送失败，已降级为日志：{ex.Message}");
        }

        AppLogger.LogInfo($"[plugin:{_pluginId}] 通知（无托盘可显示）：{title} - {message}");
    }
}

/// <summary>宿主环境信息实现。</summary>
internal sealed class PluginHostInfo : IHostInfo
{
    public string HostVersion => PluginManifestReader.HostVersion;

    public string ApiVersion => PluginApi.ApiVersion;

    public string LanguageCode => I18n.CurrentLanguageCode;

    public bool IsElevated
    {
        get
        {
            try { return ConfigManager.IsElevated(); }
            catch { return false; }
        }
    }

    public bool IsPortable => PluginPaths.IsPortable;

    public string HostExecutablePath
    {
        get
        {
            try
            {
                // 单文件发布形态下 Assembly.Location 为空，此时回退到进程主模块路径
                string? location = typeof(PluginHostInfo).Assembly.Location;
                return string.IsNullOrEmpty(location) ? Environment.ProcessPath ?? "" : location;
            }
            catch
            {
                return "";
            }
        }
    }
}

/// <summary>UI 线程调度。UI 不可用时降级为「直接执行」或「静默忽略」，绝不抛异常。</summary>
internal sealed class PluginDispatcherFacade : IDispatcherFacade
{
    private static Dispatcher? UiDispatcher
    {
        get
        {
            try
            {
                var app = System.Windows.Application.Current;
                return app?.Dispatcher;
            }
            catch
            {
                return null;
            }
        }
    }

    public bool IsOnUiThread
    {
        get
        {
            Dispatcher? dispatcher = UiDispatcher;
            return dispatcher == null || dispatcher.CheckAccess();
        }
    }

    public void Post(Action action)
    {
        if (action == null) return;

        Dispatcher? dispatcher = UiDispatcher;
        if (dispatcher == null)
        {
            // 宿主尚未完成启动：直接同步执行，保证插件在早期也能收到事件
            TryRun(action);
            return;
        }

        try
        {
            if (dispatcher.CheckAccess()) TryRun(action);
            else dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => TryRun(action)));
        }
        catch
        {
        }
    }

    public Task InvokeAsync(Action action)
    {
        if (action == null) return Task.CompletedTask;

        Dispatcher? dispatcher = UiDispatcher;
        if (dispatcher == null) return Task.Run(() => TryRun(action));

        try
        {
            return dispatcher.InvokeAsync(() => TryRun(action), DispatcherPriority.Normal).Task;
        }
        catch
        {
            return Task.CompletedTask;
        }
    }

    private static void TryRun(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // 插件回调的异常绝不能污染 WPF 的调度循环
            AppLogger.LogError("[plugin] 插件 UI 回调抛出异常", ex);
        }
    }
}

/// <summary>
/// 宿主已验证的动作能力实现。
/// <para>
/// 这里<b>刻意不重新实现</b>任何输入模拟逻辑，而是直接复用 <see cref="ActionExecutor"/> 里
/// 已经踩过坑的那几条路径：硬件扫描码映射、修饰键 10~15ms 时延保持、扩展键标志、
/// Unicode 字符流注入、Shell 令牌降权。插件自己写一遍不仅会踩同样的坑，
/// 还可能因为与主程序的全局钩子互相干扰而进入死循环。
/// </para>
/// </summary>
internal sealed class PluginHostActionInvoker : IHostActionInvoker
{
    private readonly string _pluginId;

    public PluginHostActionInvoker(string pluginId) => _pluginId = pluginId;

    public bool SendHotkey(string hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return false;
        return Guard(nameof(SendHotkey), () => ActionExecutor.ExecuteHotkey(hotkey));
    }

    public bool SendText(string text)
    {
        if (string.IsNullOrEmpty(text)) return false;
        return Guard(nameof(SendText), () => ActionExecutor.SendTextInput(text));
    }

    public bool Launch(string path, string arguments = "", bool runAsStandardUser = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        return Guard(nameof(Launch), () => ActionExecutor.ExecuteLaunch(path, arguments ?? "", runAsStandardUser));
    }

    public bool OpenFolder(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath)) return false;
        return Guard(nameof(OpenFolder), () => ActionExecutor.ExecuteFolder(folderPath));
    }

    public bool OpenUrl(string url, string browserChoice = "Default", string? customBrowserPath = null)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return Guard(nameof(OpenUrl), () => ActionExecutor.ExecuteWebUrl(url, browserChoice, customBrowserPath));
    }

    public bool SetClipboardText(string text)
    {
        if (text == null) return false;
        return Guard(nameof(SetClipboardText), () => ActionExecutor.SafeSetClipboardText(text));
    }

    public string? GetClipboardText()
    {
        string? result = null;
        try
        {
            // 动作线程是 MTA，WPF 剪贴板 API 要求 STA —— 与主程序既有做法一致，起临时 STA 线程取
            var worker = new Thread(() =>
            {
                try
                {
                    if (System.Windows.Clipboard.ContainsText())
                    {
                        result = System.Windows.Clipboard.GetText();
                    }
                }
                catch
                {
                }
            })
            {
                IsBackground = true,
                Name = "StarPie.PluginClipboardRead",
            };
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
            worker.Join(500);
        }
        catch (Exception ex)
        {
            AppLogger.LogWarn($"[plugin:{_pluginId}] 读取剪贴板失败：{ex.Message}");
        }
        return result;
    }

    /// <summary>
    /// 统一包裹：插件通过宿主服务触发的任何异常都不允许冒泡到 <see cref="ActionExecutor.Execute"/>，
    /// 否则会命中它内部的 MessageBox 分支，在无人值守时卡住动作线程。
    /// </summary>
    private bool Guard(string operation, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogError($"[plugin:{_pluginId}] 宿主动作服务 {operation} 执行失败", ex);
            return false;
        }
    }
}

/// <summary>
/// 宿主事件订阅实现。
/// <para>
/// <b>所有订阅都必须返回可释放 token</b>，因为订阅链是「宿主静态事件 → 插件实例」，
/// 插件只要不摘掉这条链，它的 ALC 就永远无法被回收，表现为「停用后 DLL 仍被占用、改不动文件」。
/// </para>
/// </summary>
internal sealed class PluginEventService : IPluginEvents
{
    private readonly string _pluginId;
    private readonly object _gate = new();
    private readonly List<Subscription> _subscriptions = new();

    public PluginEventService(string pluginId) => _pluginId = pluginId;

    private sealed class Subscription : IDisposable
    {
        private Action? _unsubscribe;
        private bool _disposed;

        public Subscription(Action unsubscribe) => _unsubscribe = unsubscribe;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Action? action = _unsubscribe;
            _unsubscribe = null;
            try { action?.Invoke(); } catch { }
        }
    }

    /// <summary>登记一条订阅，并在宿主侧留档以便停用时兜底撤销。</summary>
    private IDisposable Track(Action unsubscribe)
    {
        var subscription = new Subscription(unsubscribe);
        lock (_gate)
        {
            _subscriptions.Add(subscription);
        }
        return subscription;
    }

    public IDisposable OnLanguageChanged(Action<string> handler)
    {
        if (handler == null) return new Subscription(() => { });

        Action onChanged = () =>
        {
            try { handler(I18n.CurrentLanguageCode); }
            catch (Exception ex) { AppLogger.LogError($"[plugin:{_pluginId}] OnLanguageChanged 回调异常", ex); }
        };

        I18n.LanguageChanged += onChanged;
        return Track(() => I18n.LanguageChanged -= onChanged);
    }

    public IDisposable OnWheelOpening(Action<ActionContext> handler)
    {
        if (handler == null) return new Subscription(() => { });
        IDisposable token = PluginHost.RegisterWheelOpening(_pluginId, handler);
        return Track(token.Dispose);
    }

    public IDisposable OnWheelClosed(Action handler)
    {
        if (handler == null) return new Subscription(() => { });
        IDisposable token = PluginHost.RegisterWheelClosed(_pluginId, handler);
        return Track(token.Dispose);
    }

    /// <summary>宿主兜底撤销：即使插件忘记释放 token，也要把订阅链彻底剪断。</summary>
    public void RevokeAll()
    {
        List<Subscription> snapshot;
        lock (_gate)
        {
            snapshot = new List<Subscription>(_subscriptions);
            _subscriptions.Clear();
        }

        foreach (Subscription subscription in snapshot)
        {
            subscription.Dispose();
        }
    }
}
