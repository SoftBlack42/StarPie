using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows.Data;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 「插件动作」在主程序 UI 与持久化模型之间的编解码器。
/// <para>
/// <b>为什么需要单独一个类型</b>：插件动作与内置动作在界面上的差别在于 ——
/// 内置动作靠 <see cref="ActionItem.Type"/> 一个字符串就能唯一定位
/// （<c>"Hotkey"</c> / <c>"Ocr"</c> / <c>"Tile"</c>…），而插件动作有 N 个，
/// 全都持久化成同一个 <c>Type="Plugin"</c>，真正的身份在
/// <see cref="ActionItem.PluginActionRef"/> 里。
/// </para>
/// <para>
/// <b>两段式选择</b>：界面上「动作类型」下拉只提供<b>一个</b>「插件动作」项（类型层），
/// 具体是哪一个动作由它下方的<b>子下拉</b>决定（动作层）。这样动作类型下拉的长度不再随
/// 安装的插件数增长，而插件动作又能按插件分组呈现。
/// </para>
/// <para>
/// 子下拉的项以「分组集合视图」的形式提供（<see cref="BuildPluginActionView"/>），
/// 分组头即插件显示名，由 XAML 的 <c>GroupStyle</c> 渲染 —— <b>分组头本身不可选中</b>，
/// 所以不会出现「选中了插件名却不是一个动作」的非法状态。
/// </para>
/// </summary>
internal static class PluginActionBinding
{
    /// <summary>插件动作在 <see cref="ActionItem.Type"/> 中的固定取值。</summary>
    public const string TypeName = PluginApi.ActionTypeName;

    // ------------------------------------------------------------------ 读路径

    /// <summary>
    /// 动作当前应绑定到子下拉的哪一个值（<see cref="PluginActionItem.FullId"/>）。
    /// <para>
    /// 返回 <c>null</c> 有两种含义，调用方需要区分（见 <see cref="IsReferenceBroken"/>）：
    /// <list type="bullet">
    /// <item>还没选过具体动作 —— 正常中间态，提示用户去选；</item>
    /// <item>选过、但该贡献点已随插件停用/卸载消失 —— 必须提示「已失效」，否则用户会以为配置丢了。</item>
    /// </list>
    /// </para>
    /// </summary>
    public static string? ProjectSelectedAction(ActionItem? action)
    {
        PluginActionRef? reference = action?.PluginActionRef;
        if (reference == null || !reference.IsValid) return null;

        // 查一次注册表：贡献点仍在才返回，否则下拉框会显示一个「不存在的选项」。
        return PluginHost.TryGetAction(reference.FullId, out _) ? reference.FullId : null;
    }

    /// <summary>动作是否已选定一个<b>当前可用</b>的插件动作。</summary>
    public static bool HasSelectedAction(ActionItem? action) => ProjectSelectedAction(action) != null;

    /// <summary>
    /// 动作引用是否已<b>失效</b>（引用还在，但贡献点没了）。
    /// <para>
    /// 与「从未选定」严格区分：前者要报警，后者只是中间态。
    /// 用户「先配好图标和名称、再把插件停用」是很常见的操作，那条配置不该被静默丢弃。
    /// </para>
    /// </summary>
    public static bool IsReferenceBroken(ActionItem? action)
    {
        PluginActionRef? reference = action?.PluginActionRef;
        if (reference == null || !reference.IsValid) return false;

        return !PluginHost.TryGetAction(reference.FullId, out _);
    }

    // ------------------------------------------------------------------ 写路径

    /// <summary>
    /// 把用户选中的插件动作<b>写入</b>动作对象。
    /// </summary>
    /// <returns>贡献点存在并写入成功返回 true；插件已停用/卸载导致贡献点不存在时返回 false。</returns>
    public static bool Apply(ActionItem? action, string fullId)
    {
        if (action == null || string.IsNullOrWhiteSpace(fullId)) return false;

        if (!PluginHost.TryGetAction(fullId, out PluginActionRegistration registration))
        {
            // 例如右键菜单里选择时插件刚好被停用。保持原配置不动，由调用方给出提示。
            return false;
        }

        action.Type = TypeName;
        action.PluginActionRef = new PluginActionRef
        {
            PluginId = registration.PluginId,
            ContributionId = registration.ShortId,
        };

        // 名称与图标只在「尚未自定义」时自动填充。
        // 若用户已经手写过名字，切换动作不应该把它冲掉。
        if (ShouldAutoFillName(action.Name))
        {
            action.Name = string.IsNullOrWhiteSpace(registration.DisplayName)
                ? registration.ShortId
                : registration.DisplayName;
        }

        // 图标：空着、或上一次填的也是插件图标（说明是自动填的）时跟随动作走。
        if (string.IsNullOrEmpty(action.IconKey) ||
            action.IconKey.StartsWith(PluginApi.IconKeyPrefix, StringComparison.OrdinalIgnoreCase))
        {
            action.IconKey = registration.IconKey ?? "";
        }

        return true;
    }

    /// <summary>
    /// 切回内置动作类型时调用：清掉插件引用与插件参数。
    /// <para>
    /// 不清会留下「Type 是内置类型、却还挂着插件引用与插件参数」的混合状态 ——
    /// 那种配置在界面上看不出来，却会在导出、执行时各表现一次。
    /// </para>
    /// </summary>
    public static void Clear(ActionItem? action)
    {
        if (action == null) return;
        action.PluginActionRef = null;
        action.ExtensionData = null;
    }

    // ------------------------------------------------------------------ 下拉数据

    /// <summary>
    /// 生成「动作类型」下拉里的插件项：<b>永远只有一项</b>。
    /// <para>
    /// 这里刻意不再展开每个插件动作 —— 装十个插件就会让类型下拉多出上百项，
    /// 把内置动作挤到看不见的地方，而内置项的顺序属于用户的肌肉记忆。
    /// 具体动作一律交给子下拉（<see cref="BuildPluginActionItems"/>）。
    /// </para>
    /// </summary>
    public static List<ActionTypeItem> BuildActionTypeItems()
    {
        return new List<ActionTypeItem>
        {
            new()
            {
                Tag = TypeName,
                DisplayText = "🔌 " + I18n.T("ActionTypePluginShort"),
            },
        };
    }

    /// <summary>
    /// 生成子下拉的候选动作，<b>按插件归组</b>（顺序即插件登记顺序，组内保持插件声明顺序）。
    /// <para>
    /// 本方法运行在 UI 数据绑定路径上，插件的任何异常都不允许冒泡到主界面 ——
    /// 否则一个坏插件能让整个「手势与动作」页打不开。异常时退化为空列表，
    /// 界面表现为「没有可选的插件动作」，与插件系统关闭时一致。
    /// </para>
    /// </summary>
    public static List<PluginActionItem> BuildPluginActionItems()
    {
        var items = new List<PluginActionItem>();

        try
        {
            List<PluginActionRegistration> registrations = PluginHost.GetRegisteredActions();
            if (registrations.Count == 0) return items;

            // 同名插件会让两个插件的动作被合并进同一个区块，用户无从分辨动作来自谁 ——
            // 只有真的撞名时才给组名补上插件 ID，平时保持组标题干净。
            //
            // 注意必须先按插件 ID 去重再统计：若直接对注册动作逐个取名，
            // 一个有 9 个动作的插件会被数成 9 次，「重名」于是永远成立，
            // 组标题会莫名其妙地拖着插件 ID 的尾巴。
            var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (string pluginId in registrations
                         .Select(r => r.PluginId)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                string name = ResolvePluginDisplayName(pluginId);
                nameCounts[name] = nameCounts.TryGetValue(name, out int n) ? n + 1 : 1;
            }

            foreach (PluginActionRegistration registration in registrations)
            {
                string pluginName = ResolvePluginDisplayName(registration.PluginId);
                string groupName = nameCounts[pluginName] > 1
                    ? $"{pluginName} ({registration.PluginId})"
                    : pluginName;

                items.Add(new PluginActionItem
                {
                    FullId = registration.FullId,
                    DisplayName = string.IsNullOrWhiteSpace(registration.DisplayName)
                        ? registration.ShortId
                        : registration.DisplayName,
                    PluginId = registration.PluginId,
                    GroupName = groupName,
                });
            }
        }
        catch
        {
            return new List<PluginActionItem>();
        }

        return items;
    }

    /// <summary>
    /// 子下拉的 <c>ItemsSource</c>：按 <see cref="PluginActionItem.GroupName"/> 分组的集合视图。
    /// <para>
    /// 每次求值都重建，以反映最新的插件启用状态（新装的插件应立刻出现在自己的分组里）。
    /// 选择状态由 <c>SelectedValue</c> 绑定维持，重建不会丢失用户的配置。
    /// </para>
    /// </summary>
    /// <returns>没有任何插件动作时返回 <c>null</c>，由界面显示空状态提示。</returns>
    public static ICollectionView? BuildPluginActionView()
    {
        List<PluginActionItem> items = BuildPluginActionItems();
        if (items.Count == 0) return null;

        var view = new ListCollectionView(items);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PluginActionItem.GroupName)));
        return view;
    }

    // ------------------------------------------------------------------ 内部

    /// <summary>
    /// 取插件的显示名（分组标题即用它），取不到时退回插件 ID。
    /// <para>
    /// 注意注册项（<see cref="PluginActionRegistration"/>）只有<b>动作名</b>，没有插件名，
    /// 所以必须回查登记表 —— 界面上要的是「屏幕亮度调节」这种插件级名称。
    /// 界面上凡是展示「动作来自哪个插件」的地方都应当走这里，否则子下拉里的分组标题
    /// 与详情面板里的插件标识会对不上号。
    /// </para>
    /// </summary>
    public static string ResolvePluginDisplayName(string pluginId)
    {
        if (string.IsNullOrWhiteSpace(pluginId)) return I18n.T("ActionTypePluginShort");

        try
        {
            PluginInstance? instance = PluginHost.Find(pluginId);
            if (instance != null && !string.IsNullOrWhiteSpace(instance.Entry.Name))
            {
                return instance.Entry.Name!;
            }
        }
        catch
        {
            // 取不到就退回 ID，至少不会让动作「无家可归」
        }

        return pluginId;
    }

    /// <summary>
    /// 名字是否属于「自动填充值」而非用户自定义。
    /// </summary>
    private static bool ShouldAutoFillName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return true;
        if (name == "快捷动作") return true;
        if (name.StartsWith("动作", StringComparison.Ordinal)) return true;
        if (name.StartsWith("快捷动作", StringComparison.Ordinal)) return true;
        if (name.StartsWith("子动作", StringComparison.Ordinal)) return true;

        // 上一次就是插件动作自动填的名字 —— 换动作时应当同步替换。
        // 这里查一遍注册表，代价是一次字典遍历（插件动作通常只有几个）。
        try
        {
            foreach (PluginActionRegistration registration in PluginHost.GetRegisteredActions())
            {
                if (string.Equals(registration.DisplayName, name, StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }
        catch
        {
        }

        return false;
    }
}
