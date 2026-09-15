using System;
using System.Collections.Generic;

namespace StarPie.Plugin.HelloAction;

/// <summary>
/// 插件入口 —— 一个程序集里必须<b>恰好有一个</b>实现 <see cref="IStarPiePlugin"/> 的 public 具体类。
/// <para>
/// 三条纪律，违反任何一条都会让插件加载失败或让 StarPie 的内存无法回收：
/// ① 构造函数必须无副作用（不做 IO、不启线程、不弹窗）；
/// ② <see cref="Initialize"/> 只做注册，不做耗时操作 —— 此刻用户正在等界面响应；
/// ③ <see cref="Shutdown"/> 必须幂等，并且释放全部订阅 token。
/// </para>
/// </summary>
public sealed class HelloActionPlugin : IStarPiePlugin
{
    private IPluginContext? _context;

    /// <summary>
    /// 订阅凭据。
    /// <para>
    /// 这个列表不是「礼貌性清理」，而是<b>卸载的前提条件</b>：宿主持有静态事件，
    /// 插件实例挂在事件链上，只要不摘掉，插件的 AssemblyLoadContext 就永远无法回收，
    /// 表现为「停用后 DLL 仍被占用、装不了新版本」。
    /// </para>
    /// </summary>
    private readonly List<IDisposable> _subscriptions = new();

    public void Initialize(IPluginContext context)
    {
        _context = context;

        context.Log.Info($"初始化中：宿主 {context.Info.HostVersion}，语言 {context.Info.LanguageCode}，便携模式 {context.Info.IsPortable}");

        RegisterLocalization(context);
        RegisterActions(context);
        RegisterEventSubscriptions(context);

        context.Log.Info("初始化完成：已注册 2 个动作、1 枚图标、若干词条。");
    }

    public void Shutdown()
    {
        // 幂等：宿主可能因为「兜底撤销」与「插件自觉清理」两条路径都调用一次
        foreach (IDisposable subscription in _subscriptions)
        {
            try
            {
                subscription.Dispose();
            }
            catch
            {
            }
        }
        _subscriptions.Clear();

        _context?.Log.Info("已停用。");
        _context = null;
    }

    // ------------------------------------------------------------------ 分步注册

    private static void RegisterLocalization(IPluginContext context)
    {
        II18nRegistry i18n = context.I18n;

        // key 写短键即可，宿主会自动加上 plugin.<pluginId>. 前缀，避免与内置词条撞车
        i18n.Register("action.greet.name", "打个招呼", "Say Hello");
        i18n.Register("action.openFolder.name", "打开文件夹", "Open Folder");
        i18n.Register("action.parameterShowcase.name", "参数表单演示", "Parameter Form Showcase");

        i18n.Register("field.message.label", "问候语", "Greeting");
        i18n.Register("field.showBalloon.label", "显示托盘气泡", "Show balloon");
        i18n.Register("field.tone.label", "语气", "Tone");
        i18n.Register("field.folder.label", "文件夹路径", "Folder path");

        // 下面这五个对应 parameterShowcase 声明的控件类型。
        // 每条都同时给了词条与字面 Label：词条命中时用译文，
        // 未命中（宿主语言没覆盖）时退回字面值，不会把 key 显示给用户。
        i18n.Register("field.note.label", "多行备注", "Notes");
        i18n.Register("field.level.label", "强度", "Level");
        i18n.Register("field.script.label", "脚本文件", "Script file");
        i18n.Register("field.hotkey.label", "组合键", "Hotkey");
        i18n.Register("field.tint.label", "标记颜色", "Tint color");

        i18n.Register("tone.friendly", "友好", "Friendly");
        i18n.Register("tone.formal", "正式", "Formal");
        i18n.Register("tone.robot", "机器人", "Robot");
    }

    private static void RegisterActions(IPluginContext context)
    {
        // 图标：注册后拿到的完整 key 直接填进 ActionDescriptor.IconKey
        string iconKey = context.Icons.RegisterSvg(
            "wave",
            "M7 11.5V5.2a1.6 1.6 0 0 1 3.2 0v5.6m0 0V4.2a1.6 1.6 0 0 1 3.2 0v6.6" +
            "m0 0V6.2a1.6 1.6 0 0 1 3.2 0v5.6c0 4.4-2.2 8.2-6.4 8.2s-6.4-2.8-6.4-6.2v-4a1.6 1.6 0 0 1 3.2 0");

        context.Actions.Register(new GreetContribution(context, iconKey));
        context.Actions.Register(new OpenFolderContribution(context));
        context.Actions.Register(new ParameterShowcaseContribution(context));
    }

    private void RegisterEventSubscriptions(IPluginContext context)
    {
        // 每一次订阅都必须留住 token，并在 Shutdown 里释放（见 _subscriptions 的注释）
        _subscriptions.Add(context.Events.OnLanguageChanged(code =>
            context.Log.Info($"收到语言切换事件：{code}")));

        // 轮盘事件回调保证不在鼠标钩子线程上（宿主已切到 UI 线程），但仍然必须极快、禁止 IO。
        // 这里刻意只做一次日志，演示「观察而不干涉」的正确用法。
        _subscriptions.Add(context.Events.OnWheelOpening(actionContext =>
            System.Diagnostics.Debug.WriteLine(
                $"[HelloAction] 轮盘将要呈现，前台进程 = {actionContext.ForegroundProcessName}")));
    }
}
