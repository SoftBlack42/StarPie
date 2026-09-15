using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StarPie.Plugin.HelloAction;

/// <summary>
/// 动作实现的公共基类。
/// <para>
/// 它<b>不是</b> SDK 的一部分，只是示例的组织方式。抽出来的收益是：每个动作只需要关心
/// 「描述自己」和「干什么」，参数校验与预览有默认实现可继承。
/// </para>
/// </summary>
internal abstract class ContributionBase : IActionContribution
{
    protected ContributionBase(IPluginContext context) => Context = context;

    protected IPluginContext Context { get; }

    public abstract ActionDescriptor Descriptor { get; }

    /// <summary>默认无参数。有参数的动作覆写它。</summary>
    public virtual IReadOnlyList<ParameterField> Parameters => Array.Empty<ParameterField>();

    /// <summary>默认放行。返回 null 或空串表示通过，否则返回用户可读的错误原因。</summary>
    public virtual string? Validate(IReadOnlyDictionary<string, string> parameters) => null;

    public abstract string Preview(IReadOnlyDictionary<string, string> parameters);

    public abstract Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken);
}

/// <summary>
/// 动作一：打个招呼。
/// <para>演示最小闭环 —— 参数表单 + 多语言 + 图标 + 通知服务。</para>
/// </summary>
internal sealed class GreetContribution : ContributionBase
{
    private readonly string _iconKey;

    public GreetContribution(IPluginContext context, string iconKey) : base(context) => _iconKey = iconKey;

    public override ActionDescriptor Descriptor => new()
    {
        Id = "greet",

        // 两个文案字段都给了：DisplayNameKey 优先，找不到词条时回退 DisplayName。
        // 这样即使宿主语言是插件没提供的语种，也不会把 key 直接显示给用户。
        DisplayName = "打个招呼",
        DisplayNameKey = "action.greet.name",
        Description = "在托盘气泡里显示一句问候语",

        Category = "示例插件",
        IconKey = _iconKey,

        // 只弹个气泡，不碰输入与前台窗口，理论上可以并发；
        // 但它会写日志，为了示例可读性保持串行（串行是默认值，这里显式写出来是为了便于阅读）。
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 3,
    };

    public override IReadOnlyList<ParameterField> Parameters => new List<ParameterField>
    {
        new()
        {
            Key = "message",
            Label = "问候语",
            LabelKey = "field.message.label",
            Type = ParameterFieldType.Text,
            DefaultValue = "你好，StarPie！",
            Required = true,
            Placeholder = "随便写点什么",
            MaxLength = 200,
            HelpText = "这段文字会显示在托盘气泡里。",
        },
        new()
        {
            Key = "showBalloon",
            Label = "显示托盘气泡",
            LabelKey = "field.showBalloon.label",
            Type = ParameterFieldType.Bool,
            DefaultValue = "true",
        },
        new()
        {
            Key = "tone",
            Label = "语气",
            LabelKey = "field.tone.label",
            Type = ParameterFieldType.Enum,
            DefaultValue = "friendly",
            Options = new List<ParameterOption>
            {
                new() { Value = "friendly", Label = "友好", LabelKey = "tone.friendly" },
                new() { Value = "formal", Label = "正式", LabelKey = "tone.formal" },
                new() { Value = "robot", Label = "机器人", LabelKey = "tone.robot" },
            },
        },
    };

    public override string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("message", out string? message) || string.IsNullOrWhiteSpace(message))
        {
            return "问候语不能为空。";
        }

        if (parameters.TryGetValue("tone", out string? tone)
            && tone is not ("friendly" or "formal" or "robot"))
        {
            return $"无法识别的语气：{tone}。";
        }

        return null;
    }

    public override string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        // 契约要求这个方法必须极快（设置页滚动时会高频调用），所以只做字符串拼接
        string message = parameters.TryGetValue("message", out string? value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : "你好，StarPie！";

        string tone = parameters.TryGetValue("tone", out string? t) ? t : "friendly";
        string toneText = Context.I18n.T($"tone.{tone}", tone);

        return $"{message}（{toneText}）";
    }

    public override Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string message = input.Parameter("message") ?? "你好，StarPie！";
        string tone = input.Parameter("tone") ?? "friendly";
        bool showBalloon = input.Bool("showBalloon", true);

        string decorated = tone switch
        {
            "formal" => $"【{message}】",
            "robot" => $"BEEP BOOP :: {message}",
            _ => message,
        };

        // 演示「读只读环境信息」：插件可以知道用户此刻在哪个程序里，但改不了它
        Context.Log.Info(
            $"greet 被触发：tone={tone}，前台进程={input.Context.ForegroundProcessName}，" +
            $"鼠标=({input.Context.CursorX},{input.Context.CursorY})，提权={input.Context.IsElevated}");

        if (cancellationToken.IsCancellationRequested)
        {
            return Task.FromResult(ActionResult.Fail("任务在执行前已被取消。"));
        }

        if (showBalloon)
        {
            Context.Notify.Notify(Context.Me.Name, decorated);
        }

        // Silent=false 时宿主会再帮你弹一次气泡；这里已经自己弹过了，所以静默返回
        return Task.FromResult(ActionResult.Ok());
    }
}

/// <summary>
/// 动作二：打开文件夹。
/// <para>
/// 它演示的是本 SDK <b>最重要的一条纪律</b>：需要「启程序 / 开文件夹 / 发快捷键 / 操作剪贴板」时，
/// 一律走 <see cref="IPluginContext.Host"/>，不要自己 <c>Process.Start</c> 或 P/Invoke。
/// 宿主那几条路径已经解决了路径展开、提权降权、UIPI 放行等一堆坑。
/// </para>
/// <para>
/// 它同时演示了 <see cref="ActionKind.Background"/>：先做耗时的目录校验，再切回宿主服务执行。
/// </para>
/// </summary>
internal sealed class OpenFolderContribution : ContributionBase
{
    public OpenFolderContribution(IPluginContext context) : base(context) { }

    public override ActionDescriptor Descriptor => new()
    {
        Id = "openFolder",
        DisplayName = "打开文件夹",
        DisplayNameKey = "action.openFolder.name",
        Description = "在资源管理器里打开指定目录（支持环境变量）",
        Category = "示例插件",
        Kind = ActionKind.Sequential,
        TimeoutSeconds = 5,
    };

    public override IReadOnlyList<ParameterField> Parameters => new List<ParameterField>
    {
        new()
        {
            Key = "folder",
            Label = "文件夹路径",
            LabelKey = "field.folder.label",
            Type = ParameterFieldType.Folder,
            DefaultValue = "%USERPROFILE%",
            Required = true,
            HelpText = "可以使用 %USERPROFILE% 这类环境变量。",
        },
    };

    public override string? Validate(IReadOnlyDictionary<string, string> parameters)
    {
        if (!parameters.TryGetValue("folder", out string? folder) || string.IsNullOrWhiteSpace(folder))
        {
            return "文件夹路径不能为空。";
        }
        return null;
    }

    public override string Preview(IReadOnlyDictionary<string, string> parameters)
    {
        string folder = parameters.TryGetValue("folder", out string? value) ? value : "";
        return $"打开 {folder}";
    }

    public override Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string raw = input.Parameter("folder") ?? "";
        string expanded = Environment.ExpandEnvironmentVariables(raw);

        if (!System.IO.Directory.Exists(expanded))
        {
            // 失败必须给出用户能看懂、知道该做什么的原因 —— 这是契约的明确要求
            return Task.FromResult(ActionResult.Fail($"文件夹不存在：{expanded}"));
        }

        bool opened = Context.Host.OpenFolder(expanded);
        if (!opened)
        {
            Context.Log.Warn($"宿主拒绝打开文件夹：{expanded}");
            return Task.FromResult(ActionResult.Fail($"打开文件夹失败：{expanded}"));
        }

        Context.Log.Info($"已打开文件夹：{expanded}");
        return Task.FromResult(ActionResult.Ok());
    }
}

/// <summary>
/// 动作三：参数表单演示。
/// <para>
/// 它<b>不做任何实际操作</b>，只把收到的参数回报一句。存在的意义是让
/// <see cref="ParameterFieldType"/> 的每一种控件都能被实地看一眼 ——
/// 参考模板若只演示三四种类型，社区作者就只能靠猜来写剩下的，
/// 而「猜出来的声明」正是插件界面出意外的根源。
/// </para>
/// <para>
/// 它声明为 <see cref="ActionKind.Background"/>：只读参数、只写日志，
/// 不碰输入、剪贴板与前台窗口，因此没必要占用唯一的动作线程。
/// 判断依据是「会不会与前台窗口交互」，而不是「跑得快不快」。
/// </para>
/// </summary>
internal sealed class ParameterShowcaseContribution : ContributionBase
{
    public ParameterShowcaseContribution(IPluginContext context) : base(context) { }

    public override ActionDescriptor Descriptor => new()
    {
        Id = "parameterShowcase",
        DisplayName = "参数表单演示",
        DisplayNameKey = "action.parameterShowcase.name",
        Description = "展示宿主能渲染哪些参数控件（不执行任何实际操作，可放心点测试触发）",
        Category = "示例插件",
        IconKey = null,
        Kind = ActionKind.Background,
        TimeoutSeconds = 5,
    };

    /// <summary>
    /// 本动作把所有参数都声明成<b>可选</b>：这是一张「控件长什么样」的陈列柜，
    /// 不该因为某个格子没填就拦住用户。必填语义已由 <c>greet</c> 那个动作演示过了。
    /// </summary>
    public override IReadOnlyList<ParameterField> Parameters => new List<ParameterField>
    {
        new()
        {
            Key = "note",
            Label = "多行备注",
            LabelKey = "field.note.label",
            Type = ParameterFieldType.MultilineText,
            Placeholder = "可以换行写很多字…",
            HelpText = "演示多行文本框。",
        },
        new()
        {
            Key = "level",
            Label = "强度",
            LabelKey = "field.level.label",
            Type = ParameterFieldType.Number,
            DefaultValue = "50",
            Min = 0,
            Max = 100,
            HelpText = "演示数值框，以及宿主自动补的取值区间提示。",
        },
        new()
        {
            Key = "script",
            Label = "脚本文件",
            LabelKey = "field.script.label",
            Type = ParameterFieldType.File,
            HelpText = "演示带「选择…」按钮的文件框。",
        },
        new()
        {
            Key = "hotkey",
            Label = "组合键",
            LabelKey = "field.hotkey.label",
            Type = ParameterFieldType.Hotkey,
            HelpText = "演示热键录制框：点一下控件再按键即可录制，Esc 取消。",
        },
        new()
        {
            Key = "tint",
            Label = "标记颜色",
            LabelKey = "field.tint.label",
            Type = ParameterFieldType.Color,
            DefaultValue = "#FF2563EB",
            HelpText = "演示取色器与实时色块。",
        },
    };

    public override string Preview(IReadOnlyDictionary<string, string> parameters) => "参数表单演示";

    public override Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        // 刻意不产生任何副作用，只回报收到了什么 ——
        // 这样用户能安全地点「测试触发」，用结果反推表单确实把值写进了配置。
        string[] keys = { "note", "level", "script", "hotkey", "tint" };
        var parts = new List<string>();

        foreach (string key in keys)
        {
            string value = input.Parameter(key) ?? "";
            if (value.Length > 40) value = value.Substring(0, 40) + "…";
            parts.Add($"{key}={(value.Length == 0 ? "(未填)" : value)}");
        }

        Context.Log.Info("parameterShowcase 收到的参数：" + string.Join(" | ", parts));

        return Task.FromResult(ActionResult.Ok("收到参数：" + string.Join("，", parts), silent: false));
    }
}
