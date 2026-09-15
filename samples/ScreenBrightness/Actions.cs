using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using StarPie.Plugin;

namespace StarPie.Plugin.ScreenBrightness;

/// <summary>
/// 一个亮度动作。
/// <para>
/// 全部动作的执行逻辑高度同构（都是「跑一次亮度操作 → 播报结果」），差别只在参数和文案。
/// 所以这里用<b>一个类 + 若干实例</b>而不是给每个动作写一个子类：子类化会把同一套结果播报策略
/// （尤其是下面 <see cref="ExecuteAsync"/> 里那段关于熔断的注释）复制七遍，
/// 将来改一处漏六处几乎是必然的。
/// </para>
/// </summary>
internal sealed class BrightnessContribution : IActionContribution
{
    private readonly IPluginContext _context;
    private readonly Func<PluginActionInput, BrightnessOutcome> _run;
    private readonly string _verb;
    private readonly bool _defaultNotifyOnSuccess;
    private readonly IReadOnlyList<ParameterField> _parameters;
    private readonly Func<IReadOnlyDictionary<string, string>, string?>? _validate;

    public BrightnessContribution(
        IPluginContext context,
        string id,
        string titleKey,
        string fallbackTitle,
        string description,
        string iconKey,
        Func<PluginActionInput, BrightnessOutcome> run,
        string verb,
        bool notifyOnSuccess,
        IReadOnlyList<ParameterField>? parameters = null,
        Func<IReadOnlyDictionary<string, string>, string?>? validate = null)
    {
        _context = context;
        _run = run;
        _verb = verb;
        _defaultNotifyOnSuccess = notifyOnSuccess;
        _parameters = parameters ?? Array.Empty<ParameterField>();
        _validate = validate;

        Descriptor = new ActionDescriptor
        {
            Id = id,
            DisplayNameKey = titleKey,
            DisplayName = fallbackTitle,
            Description = description,
            Category = "屏幕亮度",
            IconKey = iconKey,

            // 必须是后台并发：DDC/CI 单次往返可能上百毫秒，多屏叠加更久。
            // 若走 Sequential 占用动作线程，用户会明显感到「触发后轮盘卡一下」，
            // 直接违背项目的零延迟红线。
            Kind = ActionKind.Background,
            TimeoutSeconds = 20,
        };
    }

    public ActionDescriptor Descriptor { get; }

    /// <summary>
    /// 参数声明。固定在轮盘上的一档一档动作不需要参数；
    /// 「设置到指定亮度」「按步长调整」这类需要用户给数值的动作则声明出来，
    /// 由宿主渲染成主程序同款控件。
    /// </summary>
    public IReadOnlyList<ParameterField> Parameters => _parameters;

    /// <summary>
    /// 自定义校验。
    /// <para>
    /// 注意这里对多数动作返回 <c>null</c> —— 那是<b>有意的</b>：
    /// 「必填」「0~100 的范围」已经由 <see cref="ParameterField.Required"/> /
    /// <see cref="ParameterField.Min"/> / <see cref="ParameterField.Max"/> 声明过了，
    /// 宿主会据此拦下非法值，插件再写一遍只是重复。
    /// </para>
    /// <para>
    /// 只有声明表达不了的规则才写在这里（例如「步长为 0 等于什么都不做」）。
    /// </para>
    /// </summary>
    public string? Validate(IReadOnlyDictionary<string, string> parameters) =>
        _validate?.Invoke(parameters);

    /// <summary>
    /// 刻意不做悬停预览：轮盘悬停是高频操作，而读一次当前亮度要走一轮 DDC/CI 往返，
    /// 会让轮盘动画掉帧。宁可没有预览，也不能牺牲流畅度。
    /// </summary>
    public string Preview(IReadOnlyDictionary<string, string> parameters) => "";

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        BrightnessOutcome outcome;

        try
        {
            outcome = _run(input);
        }
        catch (Exception ex)
        {
            // 只有真正的异常才算失败：P/Invoke 出错、COM 组件崩溃等。
            _context.Log.Error($"{Descriptor.Id} 执行异常", ex);
            return Task.FromResult(ActionResult.Fail($"亮度调整出错：{ex.Message}"));
        }

        string message = outcome.Describe(_verb);
        _context.Log.Info($"{Descriptor.Id} → {message}");

        if (!outcome.Success)
        {
            // 关键设计：环境不支持亮度控制**不是**插件失败，绝不能返回 Fail。
            //
            // 宿主对连续失败 5 次的动作会判定为插件缺陷并自动隔离（Quarantined）。
            // 而「显示器没开 DDC/CI」「台式机外接屏不可调」都是环境事实 ——
            // 一旦返回 Fail，用户连点几次就会把一个完全正常的插件弄成「已隔离」。
            // 所以这里返回成功但要求提示，让用户知道原因即可。
            return Task.FromResult(ActionResult.Ok(message, silent: false));
        }

        // notify 参数可以覆盖动作的默认播报策略：
        // 带参数的动作默认安静执行，但用户在表单里显式勾了「完成后提示我」就该照办。
        bool notify = _parameters.Count > 0
            ? input.Bool("notify", _defaultNotifyOnSuccess)
            : _defaultNotifyOnSuccess;

        // 平静的成功不打扰用户；但「已经是最亮」「首次启用软件调光」这类结果必须说出来。
        return Task.FromResult(notify || outcome.Notable
            ? ActionResult.Ok(message, silent: false)
            : ActionResult.Ok());
    }
}

/// <summary>注册本插件的全部亮度动作与词条。</summary>
internal static class BrightnessActions
{
    public const string PluginId = "com.example.screenbrightness";

    /// <summary>
    /// 词条短键。
    /// <para>
    /// SDK 契约规定插件写<b>短键</b>，宿主登记时自动补 <c>plugin.&lt;pluginId&gt;.</c> 前缀
    /// （见 <c>II18nRegistry.Register</c> 的注释）。所以这里直接返回短键即可 ——
    /// 若在这里又手写一遍插件 ID，实际存入的键会带上两层前缀，虽然仍能命中，
    /// 但会让「按前缀搜词条」这类排查变得莫名其妙。
    /// </para>
    /// </summary>
    private static string Key(string suffix) => suffix;

    /// <summary>构建全部动作。图标 key 由调用方在注册图标后传入。</summary>
    public static List<IActionContribution> CreateAll(IPluginContext context, string sunIcon, string moonIcon)
    {
        return new List<IActionContribution>
        {
            new BrightnessContribution(
                context,
                id: "up",
                titleKey: Key("up.title"),
                fallbackTitle: "亮度 +10%",
                description: "把所有可调显示器调亮一档（相对当前值，不会超过 100%）。",
                iconKey: sunIcon,
                run: _ => BrightnessController.AdjustAll(10),
                verb: "调亮",
                notifyOnSuccess: false),

            new BrightnessContribution(
                context,
                id: "down",
                titleKey: Key("down.title"),
                fallbackTitle: "亮度 -10%",
                description: "把所有可调显示器调暗一档（相对当前值，不会低于 0%）。",
                iconKey: sunIcon,
                run: _ => BrightnessController.AdjustAll(-10),
                verb: "调暗",
                notifyOnSuccess: false),

            new BrightnessContribution(
                context,
                id: "bright100",
                titleKey: Key("bright100.title"),
                fallbackTitle: "亮度 100%（最亮）",
                description: "把所有可调显示器的亮度设为最大。",
                iconKey: sunIcon,
                run: _ => BrightnessController.SetAll(100),
                verb: "设为最亮",
                notifyOnSuccess: false),

            new BrightnessContribution(
                context,
                id: "bright50",
                titleKey: Key("bright50.title"),
                fallbackTitle: "亮度 50%（均衡）",
                description: "把所有可调显示器的亮度设为 50%。",
                iconKey: sunIcon,
                run: _ => BrightnessController.SetAll(50),
                verb: "设为 50%",
                notifyOnSuccess: false),

            new BrightnessContribution(
                context,
                id: "bright25",
                titleKey: Key("bright25.title"),
                fallbackTitle: "亮度 25%（夜间护眼）",
                description: "把所有可调显示器的亮度设为 25%，适合夜间或暗环境使用。",
                iconKey: moonIcon,
                run: _ => BrightnessController.SetAll(25),
                verb: "设为 25%",
                notifyOnSuccess: false),

            new BrightnessContribution(
                context,
                id: "toggle_dim",
                titleKey: Key("toggle_dim.title"),
                fallbackTitle: "亮度明暗一键切换",
                description: "当前平均亮度偏亮则切到 30%，偏暗则切回 100%。适合在「看清」与「护眼」之间快速往返。",
                iconKey: moonIcon,
                run: _ => BrightnessController.Toggle(30),
                verb: "切换为",
                notifyOnSuccess: true),

            new BrightnessContribution(
                context,
                id: "query",
                titleKey: Key("query.title"),
                fallbackTitle: "查看当前亮度",
                description: "读取并报告所有显示器的当前亮度，用于排查「调了没反应」这类问题。",
                iconKey: sunIcon,
                run: _ =>
                {
                    var outcome = new BrightnessOutcome();
                    List<MonitorBrightness> monitors = BrightnessController.ReadAll();

                    foreach (MonitorBrightness monitor in monitors)
                    {
                        if (monitor.CurrentPercent >= 0)
                        {
                            outcome.Adjusted++;
                            outcome.AdjustedNames.Add($"{monitor.Description}（{monitor.Channel}）{monitor.CurrentPercent}%");
                        }
                        else
                        {
                            outcome.Skipped++;
                            if (!string.IsNullOrEmpty(monitor.Diagnostic))
                            {
                                outcome.Errors.Add(monitor.Diagnostic);
                            }
                        }
                    }

                    return outcome;
                },
                verb: "读到",
                notifyOnSuccess: true),

            // ------------------------------------------------------------------
            // 下面两个动作带参数，用来演示「参数表单由宿主按声明渲染」。
            // 它们刻意展示了两种不同的校验分工：
            //   · setlevel 一行自定义校验都不写 ——「必填」「0~100」全靠 ParameterField 声明，
            //     由宿主在保存与执行前各拦一次；
            //   · stepby   额外写了一条声明表达不了的规则（步长为 0 等于什么都没做）。
            // 结论：能用声明表达的规则就不要写代码，声明会同时驱动界面与校验，不会两边不一致。
            // ------------------------------------------------------------------

            new BrightnessContribution(
                context,
                id: "setlevel",
                titleKey: Key("setlevel.title"),
                fallbackTitle: "设置到指定亮度…",
                description: "把显示器亮度设为你指定的百分比。适合把某个精确数值固定绑在一个手势上。",
                iconKey: sunIcon,
                run: input => BrightnessController.SetAll(ReadPercent(input, "level", 60, 0, 100)),
                verb: "设为",
                notifyOnSuccess: true,
                parameters: new List<ParameterField>
                {
                    new()
                    {
                        Key = "level",
                        Label = "目标亮度（%）",
                        LabelKey = Key("field.level.label"),
                        Type = ParameterFieldType.Number,
                        Required = true,
                        DefaultValue = "60",
                        Min = 0,
                        Max = 100,
                        HelpText = "0 最暗、100 最亮。实际可调范围由显示器本身决定。",
                    },
                    new()
                    {
                        Key = "notify",
                        Label = "完成后提示我",
                        LabelKey = Key("field.notify.label"),
                        Type = ParameterFieldType.Bool,
                        DefaultValue = "true",
                    },
                }),

            new BrightnessContribution(
                context,
                id: "stepby",
                titleKey: Key("stepby.title"),
                fallbackTitle: "按步长调整亮度…",
                description: "按你指定的步长相对调整亮度：负值调暗，正值调亮。",
                iconKey: sunIcon,
                run: input => BrightnessController.AdjustAll(ReadPercent(input, "delta", 10, -100, 100)),
                verb: "调整",
                notifyOnSuccess: false,
                parameters: new List<ParameterField>
                {
                    new()
                    {
                        Key = "delta",
                        Label = "步长（%）",
                        LabelKey = Key("field.delta.label"),
                        Type = ParameterFieldType.Number,
                        Required = true,
                        DefaultValue = "10",
                        Min = -100,
                        Max = 100,
                        HelpText = "负值调暗、正值调亮。-100 与 100 相当于直接切到最暗/最亮。",
                    },
                    new()
                    {
                        Key = "notify",
                        Label = "完成后提示我",
                        LabelKey = Key("field.notify.label"),
                        Type = ParameterFieldType.Bool,
                        DefaultValue = "false",
                    },
                },
                // 声明能表达「数值范围」，却表达不了「为 0 等于什么都没做」。
                // 这类规则正是自定义校验存在的理由 —— 不是把声明里的规则再抄一遍。
                validate: parameters =>
                    parameters.TryGetValue("delta", out string? raw)
                    && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out double delta)
                    && delta == 0
                        ? "步长为 0 不会改变亮度，请填写非零值。"
                        : null),
        };
    }

    /// <summary>
    /// 读取一个百分比参数并夹到合法区间。
    /// <para>
    /// 宿主已经按声明的 Min/Max 校验过一次，这里仍夹一次，是因为参数也可能来自
    /// 被手工编辑过的 config.json。插件对自己的入参做边界保护，成本极低而收益确定。
    /// </para>
    /// </summary>
    private static int ReadPercent(PluginActionInput input, string key, int fallback, int min, int max)
    {
        int value = input.Int(key, fallback);
        if (value < min) return min;
        if (value > max) return max;
        return value;
    }

    /// <summary>注册多语言词条。</summary>
    public static void RegisterLocalization(IPluginContext context)
    {
        context.I18n.RegisterTable("zh-CN", new Dictionary<string, string>
        {
            [Key("up.title")] = "亮度 +10%",
            [Key("down.title")] = "亮度 -10%",
            [Key("bright100.title")] = "亮度 100%（最亮）",
            [Key("bright50.title")] = "亮度 50%（均衡）",
            [Key("bright25.title")] = "亮度 25%（夜间护眼）",
            [Key("toggle_dim.title")] = "亮度明暗一键切换",
            [Key("query.title")] = "查看当前亮度",
            [Key("setlevel.title")] = "设置到指定亮度…",
            [Key("stepby.title")] = "按步长调整亮度…",
            [Key("field.level.label")] = "目标亮度（%）",
            [Key("field.delta.label")] = "步长（%）",
            [Key("field.notify.label")] = "完成后提示我",
        });

        context.I18n.RegisterTable("zh-TW", new Dictionary<string, string>
        {
            [Key("up.title")] = "亮度 +10%",
            [Key("down.title")] = "亮度 -10%",
            [Key("bright100.title")] = "亮度 100%（最亮）",
            [Key("bright50.title")] = "亮度 50%（均衡）",
            [Key("bright25.title")] = "亮度 25%（夜間護眼）",
            [Key("toggle_dim.title")] = "亮度明暗一鍵切換",
            [Key("query.title")] = "查看目前亮度",
            [Key("setlevel.title")] = "設定到指定亮度…",
            [Key("stepby.title")] = "按步長調整亮度…",
            [Key("field.level.label")] = "目標亮度（%）",
            [Key("field.delta.label")] = "步長（%）",
            [Key("field.notify.label")] = "完成後提示我",
        });

        context.I18n.RegisterTable("en", new Dictionary<string, string>
        {
            [Key("up.title")] = "Brightness +10%",
            [Key("down.title")] = "Brightness -10%",
            [Key("bright100.title")] = "Brightness 100% (Max)",
            [Key("bright50.title")] = "Brightness 50% (Balanced)",
            [Key("bright25.title")] = "Brightness 25% (Night)",
            [Key("toggle_dim.title")] = "Toggle Brightness (Dim / Bright)",
            [Key("query.title")] = "Report Current Brightness",
            [Key("setlevel.title")] = "Set Brightness To…",
            [Key("stepby.title")] = "Adjust Brightness By…",
            [Key("field.level.label")] = "Target brightness (%)",
            [Key("field.delta.label")] = "Step (%)",
            [Key("field.notify.label")] = "Notify when done",
        });

        context.I18n.RegisterTable("ja", new Dictionary<string, string>
        {
            [Key("up.title")] = "明るさ +10%",
            [Key("down.title")] = "明るさ -10%",
            [Key("bright100.title")] = "明るさ 100%（最大）",
            [Key("bright50.title")] = "明るさ 50%（標準）",
            [Key("bright25.title")] = "明るさ 25%（夜間）",
            [Key("toggle_dim.title")] = "明るさのワンタッチ切替",
            [Key("query.title")] = "現在の明るさを表示",
            [Key("setlevel.title")] = "指定の明るさに設定…",
            [Key("stepby.title")] = "ステップで明るさを調整…",
            [Key("field.level.label")] = "目標の明るさ（%）",
            [Key("field.delta.label")] = "ステップ（%）",
            [Key("field.notify.label")] = "完了時に通知",
        });
    }
}
