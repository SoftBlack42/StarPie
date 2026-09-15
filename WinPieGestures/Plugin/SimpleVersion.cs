using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace WinPieGestures.Plugins;

/// <summary>
/// 简化语义化版本。够用就好：只解析 <c>major.minor.patch[-prerelease]</c>，
/// 不做 build metadata 比较（插件版本号里用不上）。
/// </summary>
internal readonly struct SimpleVersion : IComparable<SimpleVersion>
{
    public int Major { get; }
    public int Minor { get; }
    public int Patch { get; }

    /// <summary>预发布标识（如 <c>beta.2</c>）。非空视为「小于」同号正式版。</summary>
    public string? PreRelease { get; }

    private SimpleVersion(int major, int minor, int patch, string? preRelease)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        PreRelease = preRelease;
    }

    public static bool TryParse(string? text, out SimpleVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        string raw = text.Trim();
        string? pre = null;
        int plus = raw.IndexOf('+');
        if (plus >= 0) raw = raw.Substring(0, plus);
        int dash = raw.IndexOf('-');
        if (dash >= 0)
        {
            pre = raw.Substring(dash + 1);
            raw = raw.Substring(0, dash);
        }

        string[] parts = raw.Split('.');
        if (parts.Length == 0 || parts.Length > 4) return false;

        Span<int> nums = stackalloc int[4];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 0)
            {
                return false;
            }
            nums[i] = n;
        }

        version = new SimpleVersion(nums[0], parts.Length > 1 ? nums[1] : 0, parts.Length > 2 ? nums[2] : 0, pre);
        return true;
    }

    public int CompareTo(SimpleVersion other)
    {
        int c = Major.CompareTo(other.Major);
        if (c != 0) return c;
        c = Minor.CompareTo(other.Minor);
        if (c != 0) return c;
        c = Patch.CompareTo(other.Patch);
        if (c != 0) return c;

        // 有预发布标识的版本小于同号正式版
        bool thisPre = !string.IsNullOrEmpty(PreRelease);
        bool otherPre = !string.IsNullOrEmpty(other.PreRelease);
        if (thisPre && !otherPre) return -1;
        if (!thisPre && otherPre) return 1;
        return string.CompareOrdinal(PreRelease, other.PreRelease);
    }

    public override string ToString() =>
        string.IsNullOrEmpty(PreRelease) ? $"{Major}.{Minor}.{Patch}" : $"{Major}.{Minor}.{Patch}-{PreRelease}";

    /// <summary>只比较主版本号。用于「契约主版本必须一致」这类判定。</summary>
    public static bool MajorEquals(SimpleVersion a, SimpleVersion b) => a.Major == b.Major;

    /// <summary>主次修订号是否完全一致（忽略预发布标识）。</summary>
    public bool SameRelease(SimpleVersion other) =>
        Major == other.Major && Minor == other.Minor && Patch == other.Patch;

    /// <summary>
    /// 判定 <paramref name="host"/> 能否满足插件声明的下界 <paramref name="minimum"/>。
    /// <para>
    /// 在严格 semver 比较之外，额外放行一种场景：<b>宿主与下界属于同一版本号的预发布版</b>。
    /// 例如宿主 <c>1.7.4-beta.2</c> 满足插件声明的 <c>≥ 1.7.4</c>。
    /// </para>
    /// <para>
    /// 为什么需要这条规则：StarPie 官方发行版会长期处于预发布通道（beta / rc），
    /// 而插件作者通常就是照着当前版本号写 <c>minHostVersion</c> 的。若按 semver 字面量
    /// 严格判定，<c>1.7.4-beta.2 &lt; 1.7.4</c>，会导致「声明当前版本的插件全部装不上」，
    /// 作者只能靠反复试探一个更低的数字来绕过，体验极差且毫无必要——
    /// 同一版本号的预发布版已经具备该版本的既定 API 面。
    /// </para>
    /// <para>
    /// 反向依然严格：宿主 <c>1.7.3</c> 绝不满足 <c>≥ 1.7.4</c>，
    /// 宿主 <c>1.7.4-beta.2</c> 也绝不满足 <c>≥ 1.7.5</c>。
    /// </para>
    /// </summary>
    public static bool SatisfiesMinimum(SimpleVersion host, SimpleVersion minimum)
    {
        if (host.CompareTo(minimum) >= 0) return true;

        // 同一版本号 + 宿主带预发布标识 => 视为满足（见上方说明）
        return host.SameRelease(minimum) && !string.IsNullOrEmpty(host.PreRelease);
    }
}

/// <summary>
/// 目标框架解析与兼容性判定。
/// <para>
/// 规则：插件 TFM 不得<b>高于</b>宿主 TFM。即 net 主版本更小 → 通过；主版本相同 → 平台版本不高于宿主；
/// <c>net8.0-windows</c>（无平台版本）视为最低，永远通过。
/// </para>
/// </summary>
internal readonly struct TargetFrameworkInfo
{
    public string Raw { get; }
    public bool IsWindows { get; }
    public int NetMajor { get; }
    public int NetMinor { get; }
    public Version? PlatformVersion { get; }

    private TargetFrameworkInfo(string raw, bool isWindows, int netMajor, int netMinor, Version? platformVersion)
    {
        Raw = raw;
        IsWindows = isWindows;
        NetMajor = netMajor;
        NetMinor = netMinor;
        PlatformVersion = platformVersion;
    }

    private static readonly Regex Pattern = new(
        @"^net(?<major>\d+)\.(?<minor>\d+)(?<platform>-[a-z]+)?(?<pv>[\d\.]+)?$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    public static bool TryParse(string? tfm, out TargetFrameworkInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(tfm)) return false;

        Match m = Pattern.Match(tfm.Trim());
        if (!m.Success) return false;

        string platform = m.Groups["platform"].Success ? m.Groups["platform"].Value : "";
        Version? pv = null;
        if (m.Groups["pv"].Success && Version.TryParse(m.Groups["pv"].Value, out Version? parsed))
        {
            pv = parsed;
        }

        info = new TargetFrameworkInfo(
            tfm.Trim(),
            platform.StartsWith("-windows", StringComparison.OrdinalIgnoreCase),
            int.Parse(m.Groups["major"].Value, CultureInfo.InvariantCulture),
            int.Parse(m.Groups["minor"].Value, CultureInfo.InvariantCulture),
            pv);
        return true;
    }

    /// <summary>判断 <paramref name="plugin"/> 能否运行在 <paramref name="host"/> 之上。</summary>
    public static bool IsCompatible(TargetFrameworkInfo plugin, TargetFrameworkInfo host)
    {
        // 网络/移动等非 windows 平台直接不兼容
        if (!plugin.IsWindows) return false;

        if (plugin.NetMajor != host.NetMajor) return plugin.NetMajor < host.NetMajor;
        if (plugin.NetMinor != host.NetMinor) return plugin.NetMinor < host.NetMinor;

        // 平台版本：无声明视为最低（0.0.0.0）
        Version p = plugin.PlatformVersion ?? new Version(0, 0);
        Version h = host.PlatformVersion ?? new Version(0, 0);
        return p <= h;
    }

    public override string ToString() => Raw;
}
