using System;
using System.Collections.Generic;

namespace WinPieGestures.Plugins;

/// <summary>
/// <b>只读</b>扫描目录（<c>程序目录\plugin</c>）里一个 <c>.dll</c> 的当前处境。
/// </summary>
internal enum PluginCandidateState
{
    /// <summary>没装过，可以直接安装。</summary>
    Installable,

    /// <summary>已装同版本，且内容哈希一致。重复安装没有意义。</summary>
    Installed,

    /// <summary>已装同版本但内容哈希不同 —— 文件被换过（重编译、手改）。</summary>
    Replaced,

    /// <summary>候选版本比已装的新。</summary>
    Update,

    /// <summary>候选版本比已装的旧。</summary>
    Downgrade,

    /// <summary>已装，但两侧版本号至少有一侧解析不了，无法比较。</summary>
    VersionUnknown,

    /// <summary>已被开发者模式的「外部路径登记」占用同一个 ID。</summary>
    ExternalRegistered,

    /// <summary>扫描目录里有两枚 <c>.dll</c> 声明了同一个 ID。</summary>
    Duplicate,

    /// <summary>识别未通过（不是 StarPie 插件 / 架构不符 / 缺少元数据……）。</summary>
    Rejected,
}

/// <summary>
/// 候选插件的视图模型。
/// <para>
/// 与 <see cref="PluginListItem"/> 的区别：这里是「还没安装的东西」，
/// 所以没有运行态、没有启用开关，只有「能不能装」以及「和已装的那份比是什么关系」。
/// </para>
/// <para>
/// 与 <see cref="PluginListItem"/> 一样刻意不实现变更通知：候选列表整体重扫、整体重建。
/// </para>
/// </summary>
internal sealed class PluginCandidate
{
    /// <summary>候选 <c>.dll</c> 的完整路径。安装时原样复制这一枚文件。</summary>
    public string DllPath { get; init; } = "";

    /// <summary>文件名，用于冲突提示里精确指认是哪一枚。</summary>
    public string FileName { get; init; } = "";

    /// <summary>静态识别结果（含清单、哈希、签名）。</summary>
    public PluginScanResult Scan { get; init; } = new();

    public PluginCandidateState State { get; init; } = PluginCandidateState.Installable;

    /// <summary>当前处境的一句话说明（为什么是「可安装」/「有更新」/「重复」）。</summary>
    public string Note { get; init; } = "";

    public string? PluginId => Scan.Manifest?.Id;

    public string DisplayName
    {
        get
        {
            string? name = Scan.Manifest?.Name;
            if (!string.IsNullOrWhiteSpace(name)) return name!;
            if (!string.IsNullOrWhiteSpace(PluginId)) return PluginId!;
            return FileName;
        }
    }

    /// <summary>形如 <c>v1.2.0</c>；未知版本时为空串，界面上不占位。</summary>
    public string VersionText
    {
        get
        {
            string? version = Scan.Manifest?.Version;
            return string.IsNullOrWhiteSpace(version) ? "" : $"v{version}";
        }
    }

    public string SummaryText
    {
        get
        {
            var parts = new List<string>();
            string? author = Scan.Manifest?.Author;
            if (!string.IsNullOrWhiteSpace(author)) parts.Add($"作者 {author}");

            string? description = Scan.Manifest?.Description;
            if (!string.IsNullOrWhiteSpace(description)) parts.Add(description!);

            if (Scan.Manifest?.Capabilities is { Count: > 0 } capabilities)
            {
                parts.Add($"声明能力：{string.Join("、", capabilities)}");
            }

            if (parts.Count == 0) parts.Add(FileName);
            return string.Join("　|　", parts);
        }
    }

    /// <summary>状态徽标文案。</summary>
    public string StateText => State switch
    {
        PluginCandidateState.Installable => "可安装",
        PluginCandidateState.Installed => "已装同版本",
        PluginCandidateState.Replaced => "内容已变",
        PluginCandidateState.Update => "有新版本",
        PluginCandidateState.Downgrade => "版本更旧",
        PluginCandidateState.VersionUnknown => "版本待确认",
        PluginCandidateState.ExternalRegistered => "已外部引用",
        PluginCandidateState.Duplicate => "ID 重复",
        PluginCandidateState.Rejected => "无法识别",
        _ => State.ToString(),
    };

    /// <summary>状态图标，用于快速扫读。</summary>
    public string StatusGlyph => State switch
    {
        PluginCandidateState.Installable => "📦",
        PluginCandidateState.Installed => "✅",
        PluginCandidateState.Replaced => "🔁",
        PluginCandidateState.Update => "⬆️",
        PluginCandidateState.Downgrade => "⬇️",
        PluginCandidateState.VersionUnknown => "❓",
        PluginCandidateState.ExternalRegistered => "🔗",
        PluginCandidateState.Duplicate => "⚠️",
        PluginCandidateState.Rejected => "⛔",
        _ => "📦",
    };

    /// <summary>
    /// 是否允许点「安装」。
    /// <para>
    /// <see cref="PluginCandidateState.Duplicate"/> 与
    /// <see cref="PluginCandidateState.Rejected"/> 一律不允许 ——
    /// 前者是扫描目录自身有歧义（装哪一枚都说不清），后者根本没识别出插件 ID。
    /// 已装同版本的 <see cref="PluginCandidateState.Installed"/> 也不给按钮，
    /// 装了也是白复制一遍。
    /// </para>
    /// </summary>
    public bool CanInstall => State is
        PluginCandidateState.Installable or
        PluginCandidateState.Replaced or
        PluginCandidateState.Update or
        PluginCandidateState.Downgrade or
        PluginCandidateState.VersionUnknown;

    /// <summary>安装按钮文案。</summary>
    public string InstallButtonText => State switch
    {
        PluginCandidateState.Update => "⬆️ 更新",
        PluginCandidateState.Downgrade => "⬇️ 降级安装",
        PluginCandidateState.Replaced => "🔁 覆盖安装",
        PluginCandidateState.VersionUnknown => "📦 覆盖安装",
        _ => "📦 安装",
    };

    /// <summary>供 DataTrigger 判断是否显示说明行。</summary>
    public bool HasNote => !string.IsNullOrWhiteSpace(Note);

    /// <summary>是否处于「有问题」的状态，界面上用不同颜色标出。</summary>
    public bool IsProblematic => State is
        PluginCandidateState.Duplicate or
        PluginCandidateState.Rejected;

    /// <summary>把候选转成一次安装动作所需的识别结果。UI 点击「安装」时使用。</summary>
    public PluginScanResult ToScan() => Scan;
}
