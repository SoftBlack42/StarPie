using System;
using System.Collections.Generic;
using StarPie.Plugin;

namespace StarPie.Plugin.ScreenBrightness;

/// <summary>
/// 屏幕亮度调节插件入口。
/// <para>
/// 实现意图：这既是一个能用的插件，也是插件系统的**压力测试样本** ——
/// 它同时用到了 P/Invoke 原生 API、COM 互操作、以及耗时 IO 三类在进程内插件里
/// 最容易出问题的能力。如果它能稳定跑通，说明宿主的隔离与调度做得够扎实。
/// </para>
/// </summary>
public sealed class ScreenBrightnessPlugin : IStarPiePlugin
{
    // 图标刻意用最朴素的 M/A/L/H/V/Z 命令构造：不追求精致，追求「一定解析得开」。
    // 插件图标一旦语法有误，会让轮盘的几何解析抛异常，收益远小于风险。
    private const string SunSvg =
        "M12,8A4,4 0 0,1 12,16A4,4 0 0,1 12,8Z" +   // 日面（两段半圆拼成整圆）
        "M11,1H13V4H11Z" +                           // 上光芒
        "M11,20H13V23H11Z" +                         // 下光芒
        "M1,11H4V13H1Z" +                            // 左光芒
        "M20,11H23V13H20Z";                          // 右光芒

    private const string MoonSvg = "M12,4A8,8 0 0,0 12,20Z";   // 半月，用于标识低亮度动作

    private readonly List<IDisposable> _tokens = new();
    private bool _initialized;

    public void Initialize(IPluginContext context)
    {
        // 幂等保护：宿主在重载流程里可能重复调用 Initialize。
        if (_initialized)
        {
            context.Log.Warn("Initialize 被重复调用，已忽略本次调用。");
            return;
        }

        _initialized = true;

        string sunIcon = context.Icons.RegisterSvg("sun", SunSvg);
        string moonIcon = context.Icons.RegisterSvg("moon", MoonSvg);

        BrightnessActions.RegisterLocalization(context);

        foreach (IActionContribution contribution in BrightnessActions.CreateAll(context, sunIcon, moonIcon))
        {
            _tokens.Add(context.Actions.Register(contribution));
        }

        // 把「这台机器到底能不能调亮度」在加载阶段就写进日志。
        // 用户反馈「调了没反应」时，看一眼插件日志就能分清是环境问题还是插件问题 ——
        // 这是插件作者能给用户省下的最大一笔沟通成本。
        List<MonitorBrightness> monitors = BrightnessController.ReadAll();
        int writable = 0;

        foreach (MonitorBrightness monitor in monitors)
        {
            if (monitor.IsWritable) writable++;
        }

        context.Log.Info($"已注册 {_tokens.Count} 个亮度动作；检测到 {monitors.Count} 块屏幕，其中 {writable} 块可调。");

        foreach (MonitorBrightness monitor in monitors)
        {
            string line = $"  · {monitor.Description}｜{monitor.Channel}｜" +
                          (monitor.IsWritable ? $"{monitor.CurrentPercent}%" : "不可调");

            if (!string.IsNullOrEmpty(monitor.Diagnostic))
            {
                line += $"｜{monitor.Diagnostic}";
            }

            context.Log.Info(line);
        }

        if (writable == 0)
        {
            context.Log.Warn(
                "硬件通道（DDC/CI、WMI）均不可用，亮度调节会自动回退到软件调光（gamma）。" +
                "若希望走硬件背光调光，请确认显示器 OSD 菜单中已开启 DDC/CI —— " +
                "多数品牌默认关闭，或藏在「其他设置」里。");
        }
    }

    public void Shutdown()
    {
        // 必须幂等：宿主在卸载、错误兜底、进程退出时都可能调用它。
        foreach (IDisposable token in _tokens)
        {
            try
            {
                token.Dispose();
            }
            catch
            {
                // 单个 token 释放失败不应影响其余 token —— 否则会留下一半注册项，
                // 下次启用时贡献点 ID 冲突，插件直接变成「加载失败」。
            }
        }

        _tokens.Clear();

        // 软件调光改的是系统级 gamma ramp ——即使 StarPie 退出也不会自动还原。
        // 不主动恢复的话，用户的屏幕会一直暗着，直到重启系统为止。
        // 这是卸载时必须承担的责任，不能指望用户自己发现。
        GammaController.Restore();

        // 把这个标志连同 token 一起复位。若不复位，「停用后再启用」时 Initialize
        // 会因为 _initialized 仍为 true 而直接返回，表现为「重新加载后一个动作都没有」。
        _initialized = false;
    }
}
