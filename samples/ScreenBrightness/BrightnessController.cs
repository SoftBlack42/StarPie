using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;

namespace StarPie.Plugin.ScreenBrightness;

/// <summary>
/// 显示器亮度控制的内部实现。
/// <para>
/// 宿主只封装了「模拟输入」这一类通用能力（<c>IHostActionInvoker</c>），亮度不在其中，
/// 所以这个插件必须自己 P/Invoke。这正是插件系统期望的分工：通用且高风险的能力由宿主
/// 统一提供，领域专用的实现由插件自己负责。
/// </para>
/// <para>
/// Windows 上调亮度有两条互不相通的路径，覆盖的硬件完全不同，缺一不可：
/// </para>
/// <list type="number">
/// <item><b>DDC/CI</b>（<c>dxva2.dll</c>）—— 通过视频线缆走 MCCS 协议直接与显示器通讯。
/// 绝大多数外接显示器支持（前提是显示器 OSD 里开启了 DDC/CI）。</item>
/// <item><b>WMI</b>（<c>root\wmi</c>）—— 由显卡驱动暴露，绝大多数笔记本内置屏走这条路；
/// 台式机的独立显示器通常<b>不</b>支持。</item>
/// </list>
/// <para>
/// 两条路径<b>都要尝试</b>而不是「前者失败才用后者」：笔记本外接显示器是很常见的场景，
/// 此时内置屏只能走 WMI、外接屏只能走 DDC/CI，只做一条就会漏掉一半屏幕。
/// </para>
/// <para>
/// <b>一个容易踩的坑</b>：DDC/CI 的物理显示器句柄只在
/// <c>GetPhysicalMonitorsFromHMONITOR</c> 与 <c>DestroyPhysicalMonitors</c> 之间有效。
/// 因此「先枚举读一遍、过一会儿再拿句柄去写」是行不通的 —— 句柄已经随会话销毁了。
/// 本类的做法是把「读当前值 → 算出目标值 → 写入」全部压在同一个会话生命周期内完成。
/// </para>
/// </summary>
internal static class BrightnessController
{
    /// <summary>DDC/CI 的 VCP 代码：0x10 = Luminance（亮度）。MCCS 标准里的固定值。</summary>
    private const byte VcpLuminance = 0x10;

    /// <summary>
    /// 串行化所有亮度调整。DDC/CI 单次往返可能耗时上百毫秒，用户连点轮盘时多个后台任务
    /// 并发下发会因时序交错让亮度乱跳；串行执行虽然慢一点，但每次的最终效果都是确定的。
    /// </summary>
    private static readonly object Gate = new();

    // ==================== 读取（仅供展示，不含可用句柄） ====================

    /// <summary>读取所有可调亮度的显示器，用于「当前亮度」这类查询展示。</summary>
    public static List<MonitorBrightness> ReadAll()
    {
        lock (Gate)
        {
            var monitors = new List<MonitorBrightness>();

            try
            {
                ReadDdcMonitors(monitors);
            }
            catch (Exception ex)
            {
                monitors.Add(MonitorBrightness.Unavailable($"DDC/CI 通道探测失败：{ex.Message}"));
            }

            try
            {
                ReadWmiMonitors(monitors);
            }
            catch (Exception ex)
            {
                monitors.Add(MonitorBrightness.Unavailable($"WMI 通道探测失败：{ex.Message}"));
            }

            // 软件调光是一条「虚拟屏幕」：它作用于整个桌面而不是某一块物理屏，
            // 所以只在真的处于调光状态时才报告，否则会在查询结果里多出一行噪音。
            if (GammaController.IsApplied)
            {
                monitors.Add(new MonitorBrightness
                {
                    Description = "整个桌面",
                    Channel = "软件调光",
                    IsWritable = true,
                    CurrentPercent = GammaController.CurrentPercent,
                });
            }

            return monitors;
        }
    }

    /// <summary>读出所有屏的平均亮度百分比；没有可读屏时返回 -1。</summary>
    public static int ReadAveragePercent()
    {
        List<MonitorBrightness> monitors = ReadAll();

        int sum = 0;
        int readable = 0;

        foreach (MonitorBrightness monitor in monitors)
        {
            if (monitor.CurrentPercent >= 0)
            {
                sum += monitor.CurrentPercent;
                readable++;
            }
        }

        return readable > 0 ? sum / readable : -1;
    }

    // ==================== 写入 ====================

    /// <summary>把所有显示器的亮度设为同一个百分比。</summary>
    public static BrightnessOutcome SetAll(int percent)
    {
        int target = Clamp(percent);

        lock (Gate)
        {
            var outcome = new BrightnessOutcome();

            // DDC/CI 不需要先读当前值，传入的回调直接返回目标值。
            // 但句柄的读/写仍必须在同一个会话内 —— 见类注释里的坑。
            ApplyViaDdc(outcome, _ => target);
            ApplyViaWmi(outcome, _ => target);

            if (outcome.Adjusted > 0)
            {
                DropSoftwareFallback();
                return outcome;
            }

            ApplySoftwareFallback(outcome, target);
            return outcome;
        }
    }

    /// <summary>把所有显示器的亮度相对调整 <paramref name="deltaPercent"/> 个百分点。</summary>
    public static BrightnessOutcome AdjustAll(int deltaPercent)
    {
        lock (Gate)
        {
            var outcome = new BrightnessOutcome();

            // 相对调整必须先读当前值：每块屏的起点可能不同，不能拿某一个值去套所有屏。
            ApplyViaDdc(outcome, current => current < 0 ? -1 : Clamp(current + deltaPercent));
            ApplyViaWmi(outcome, current => current < 0 ? -1 : Clamp(current + deltaPercent));

            if (outcome.Adjusted > 0)
            {
                DropSoftwareFallback();
                return outcome;
            }

            // 软件模式下没有「硬件当前值」可读，只能以自己记住的上一次目标为基准。
            ApplySoftwareFallback(outcome, Clamp(GammaController.CurrentPercent + deltaPercent));
            return outcome;
        }
    }

    /// <summary>在 <paramref name="dimPercent"/> 与 100% 之间切换（省电 / 护眼快切）。</summary>
    public static BrightnessOutcome Toggle(int dimPercent)
    {
        int dim = Clamp(dimPercent);

        // 先读一次平均值来决定往哪边切，再整体设为目标值。
        // 多屏亮度不一致时「当前是不是暗的」本就是模糊判断，取平均值最符合直觉。
        int average = ReadAveragePercent();
        int target = average < 0 || average < (dim + 100) / 2 ? 100 : dim;

        BrightnessOutcome outcome = SetAll(target);
        outcome.TargetPercent = target;
        return outcome;
    }

    // ==================== DDC/CI ====================

    /// <summary>
    /// 在同一个 DDC/CI 会话内完成「读 → 算 → 写」。
    /// </summary>
    /// <param name="resolveTarget">
    /// 由当前亮度百分比算出目标百分比；返回 -1 表示这块屏本轮跳过。
    /// </param>
    private static void ApplyViaDdc(BrightnessOutcome outcome, Func<int, int> resolveTarget)
    {
        using DdcSession session = DdcSession.Open();

        for (int i = 0; i < session.Handles.Count; i++)
        {
            IntPtr handle = session.Handles[i];
            string name = i < session.Names.Count && !string.IsNullOrWhiteSpace(session.Names[i])
                ? session.Names[i]
                : $"显示器 {i + 1}";

            try
            {
                // 读回显器自报的量程。DDC/CI 的亮度范围并非固定 0-100，
                // 直接塞 0-100 会在部分显示器上退化成「只有最亮和最暗两档」。
                if (!GetVCPFeatureAndVCPFeatureReply(handle, VcpLuminance, IntPtr.Zero, out uint current, out uint max) || max == 0)
                {
                    // 探到了物理显示器，但它不接受亮度控制。常见原因：
                    // 显示器 OSD 里关掉了 DDC/CI、走 HDMI 转接芯片、或这是笔记本内置屏。
                    // 这不记为错误 —— 内置屏还有 WMI 通道在后面兜着。
                    outcome.Skipped++;
                    continue;
                }

                int currentPercent = (int)Math.Round(current * 100.0 / max);
                int target = resolveTarget(currentPercent);

                if (target < 0)
                {
                    outcome.Skipped++;
                    continue;
                }

                uint raw = (uint)Math.Round(target * max / 100.0);

                if (SetVCPFeature(handle, VcpLuminance, raw))
                {
                    outcome.Adjusted++;
                    outcome.AdjustedNames.Add($"{name} → {target}%");
                }
                else
                {
                    outcome.Skipped++;
                    outcome.Errors.Add($"{name}：写入被拒绝");
                }
            }
            catch (Exception ex)
            {
                outcome.Skipped++;
                outcome.Errors.Add($"{name}：{ex.Message}");
            }
        }
    }

    private static void ReadDdcMonitors(List<MonitorBrightness> monitors)
    {
        using DdcSession session = DdcSession.Open();

        for (int i = 0; i < session.Handles.Count; i++)
        {
            IntPtr handle = session.Handles[i];
            string name = i < session.Names.Count && !string.IsNullOrWhiteSpace(session.Names[i])
                ? session.Names[i]
                : $"显示器 {i + 1}";

            bool supported = GetVCPFeatureAndVCPFeatureReply(handle, VcpLuminance, IntPtr.Zero, out uint current, out uint max);

            monitors.Add(new MonitorBrightness
            {
                Description = name,
                Channel = "DDC/CI",
                IsWritable = supported && max > 0,
                CurrentPercent = supported && max > 0 ? (int)Math.Round(current * 100.0 / max) : -1,
            });
        }
    }

    /// <summary>
    /// 一次「枚举物理显示器 → 用完销毁句柄」的会话。
    /// <para>
    /// 必须成对调用 <c>GetPhysicalMonitorsFromHMONITOR</c> / <c>DestroyPhysicalMonitors</c>：
    /// 后者会释放每个物理显示器句柄，漏掉就会在长期运行中稳定泄漏驱动侧句柄。
    /// </para>
    /// </summary>
    private sealed class DdcSession : IDisposable
    {
        private readonly List<PHYSICAL_MONITOR[]> _arrays = new();

        public List<IntPtr> Handles { get; } = new();

        public List<string> Names { get; } = new();

        public static DdcSession Open()
        {
            var session = new DdcSession();

            // 委托必须在 EnumDisplayMonitors 返回前保持存活：若被 GC 回收，
            // 非托管侧回调到已释放的 thunk 会直接崩掉宿主进程。
            MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref RECT rect, IntPtr data) =>
            {
                if (GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out uint count) && count > 0)
                {
                    var array = new PHYSICAL_MONITOR[count];

                    if (GetPhysicalMonitorsFromHMONITOR(hMonitor, count, array))
                    {
                        session._arrays.Add(array);

                        foreach (PHYSICAL_MONITOR physical in array)
                        {
                            session.Handles.Add(physical.hPhysicalMonitor);
                            session.Names.Add(physical.szPhysicalMonitorDescription ?? "");
                        }
                    }
                }

                return true;
            };

            try
            {
                EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            }
            finally
            {
                GC.KeepAlive(callback);
            }

            return session;
        }

        public void Dispose()
        {
            foreach (PHYSICAL_MONITOR[] array in _arrays)
            {
                try
                {
                    DestroyPhysicalMonitors((uint)array.Length, array);
                }
                catch
                {
                    // 释放失败没有可做的补救动作，也不该影响调用方的主流程。
                }
            }

            _arrays.Clear();
            Handles.Clear();
            Names.Clear();
        }
    }

    // ==================== WMI ====================

    /// <summary>
    /// 通过 COM 的 <c>WbemScripting.SWbemLocator</c> 访问 <c>root\wmi</c>。
    /// <para>
    /// 为什么不用 <c>System.Management</c>：它是独立的 NuGet 包，引入后插件目录里就得多带
    /// 一个程序集，破坏「插件只需引用 SDK」这一前提。<c>WbemScripting</c> 是 Windows 自带的
    /// COM 组件（<c>wbemdisp.dll</c>），纯反射即可调用，零额外依赖。
    /// </para>
    /// <para>
    /// 代价是调用点全是反射、没有编译期检查 —— 所以所有失败路径都必须显式吞掉并返回空集合，
    /// 由上层汇总成用户能看懂的信息，而不是让异常穿透到宿主的动作线程。
    /// </para>
    /// </summary>
    private const string WmiNamespace = @"root\wmi";

    /// <summary>硬件两条通道都没成功时，退到 gamma 软件调光。</summary>
    private static void ApplySoftwareFallback(BrightnessOutcome outcome, int target)
    {
        bool wasApplied = GammaController.IsApplied;

        if (target >= 100 && !wasApplied)
        {
            // 本来就是最亮、也从没启动过软件调光 —— 没有可做的事。
            // 但仍要如实告知：否则用户点「亮度 +10%」看到毫无反应，只会以为插件坏了。
            outcome.Adjusted++;
            outcome.Notable = true;
            outcome.AdjustedNames.Add("已是最亮，无需调整");
            return;
        }

        if (GammaController.Apply(target))
        {
            outcome.Adjusted++;
            outcome.UsedSoftwareFallback = true;
            outcome.AdjustedNames.Add($"软件调光 → {target}%");

            // 只在「首次启用软件调光」时提示：用户需要知道此刻生效的是软件方案而非背光。
            // 之后再调就静默，否则连点几次会被气泡烦到。
            if (!wasApplied) outcome.Notable = true;
        }
        else
        {
            outcome.Skipped++;
            outcome.Errors.Add("软件调光失败：显卡驱动拒绝了 gamma 曲线写入");
        }
    }

    /// <summary>
    /// 硬件通道生效后，把此前的软件调光撤掉。
    /// <para>
    /// 不撤的话会变成「硬件亮度 + gamma 双重压低」，屏幕比用户预期暗得多，
    /// 而且界面上没有任何提示 —— 这类「静默叠加」是最难排查的一类问题。
    /// </para>
    /// </summary>
    private static void DropSoftwareFallback()
    {
        if (GammaController.IsApplied)
        {
            GammaController.Restore();
        }
    }

    private static void ApplyViaWmi(BrightnessOutcome outcome, Func<int, int> resolveTarget)
    {
        List<object> readers = QueryWmi("SELECT CurrentBrightness FROM WmiMonitorBrightness", out _);
        List<object> writers = QueryWmi("SELECT * FROM WmiMonitorBrightnessMethods", out string? failure);

        if (writers.Count == 0)
        {
            // 台式机 + 外接显示器时「WMI 能连上但没有亮度实例」是正常现象，不记为错误。
            // 但查询本身报错就是另一回事了，必须浮上来让用户看到。
            if (failure != null)
            {
                outcome.Errors.Add($"WMI 通道不可用 —— {failure}");
            }
            return;
        }

        for (int i = 0; i < writers.Count; i++)
        {
            string name = writers.Count > 1 ? $"内置屏幕 {i + 1}" : "内置屏幕";

            try
            {
                int currentPercent = -1;

                if (i < readers.Count)
                {
                    object? value = readers[i].GetType().InvokeMember(
                        "CurrentBrightness", BindingFlags.GetProperty, null, readers[i], null);

                    if (value != null) currentPercent = Convert.ToInt32(value);
                }

                int target = resolveTarget(currentPercent);
                if (target < 0)
                {
                    outcome.Skipped++;
                    continue;
                }

                // WmiSetBrightness(Timeout, Brightness)：Timeout 单位秒，0 表示立即下发。
                // 两个参数都用最朴素的整数类型（VT_I4 / VT_UI1 均可被 COM 转换），
                // 避免再出现上面那种「类型不匹配被静默吞掉」的问题。
                writers[i].GetType().InvokeMember(
                    "WmiSetBrightness",
                    BindingFlags.InvokeMethod,
                    null,
                    writers[i],
                    new object[] { 0, (byte)target });

                outcome.Adjusted++;
                outcome.AdjustedNames.Add($"{name} → {target}%");
            }
            catch (Exception ex)
            {
                outcome.Skipped++;
                outcome.Errors.Add($"{name}：{ex.Message}");
            }
        }
    }

    private static void ReadWmiMonitors(List<MonitorBrightness> monitors)
    {
        List<object> instances = QueryWmi("SELECT CurrentBrightness FROM WmiMonitorBrightness", out string? failure);

        if (failure != null)
        {
            monitors.Add(MonitorBrightness.Unavailable($"WMI 通道查询失败 —— {failure}"));
            return;
        }

        for (int i = 0; i < instances.Count; i++)
        {
            int current = -1;

            try
            {
                object? value = instances[i].GetType().InvokeMember(
                    "CurrentBrightness", BindingFlags.GetProperty, null, instances[i], null);

                if (value != null) current = Convert.ToInt32(value);
            }
            catch
            {
                // 个别驱动不实现该属性；保留 -1 表示「不可读」。
            }

            monitors.Add(new MonitorBrightness
            {
                Description = instances.Count > 1 ? $"内置屏幕 {i + 1}" : "内置屏幕",
                Channel = "WMI",
                IsWritable = true,
                CurrentPercent = current,
            });
        }
    }

    /// <summary>
    /// 执行一次 WMI 查询并返回结果集合中的对象列表。
    /// </summary>
    /// <param name="failure">
    /// 查询失败时的真实原因；成功时为 null（「查询没报错但没有实例」属于成功）。
    /// 之所以必须区分这两者：前者是代码问题、后者是环境问题，处理方式完全不同。
    /// </param>
    private static List<object> QueryWmi(string wql, out string? failure)
    {
        failure = null;
        var results = new List<object>();

        try
        {
            Type? locatorType = Type.GetTypeFromProgID("WbemScripting.SWbemLocator");
            if (locatorType == null) return results;

            object? locator = Activator.CreateInstance(locatorType);
            if (locator == null) return results;

            // ConnectServer(strServer, strNamespace, strUser, strPassword,
            //               strLocale, strAuthority, iSecurityFlags)
            //
            // 这里刻意只传 7 个参数、且全部给明确类型，是踩过坑之后定下来的：
            // 若照 COM 文档把可选项补满 8 个参数并给 null，iSecurityFlags 会收到 VT_NULL
            // 而该参数期望 VT_I4，直接抛 DISP_E_TYPEMISMATCH（类型不匹配）。
            // 后果极具迷惑性 —— 异常被下面的 catch 吞掉后表现为「WMI 查不到任何显示器」，
            // 看起来像环境不支持，实际是调用姿势不对。
            // 省略末尾的 objWbemNamedValueSet 反而能让 COM 用它自己的默认值。
            object? services = locatorType.InvokeMember(
                "ConnectServer", BindingFlags.InvokeMethod, null, locator,
                new object[] { ".", WmiNamespace, "", "", "", "", 0 });

            if (services == null) return results;

            object? set = services.GetType().InvokeMember(
                "ExecQuery", BindingFlags.InvokeMethod, null, services, new object[] { wql });

            if (set == null) return results;

            // 用 ItemIndex 而不是 foreach：SWbemObjectSet 的枚举要走 IEnumVARIANT，
            // 在纯反射路径下取它并不可靠；ItemIndex 是明确的成员，行为可预期。
            int count = Convert.ToInt32(set.GetType().InvokeMember(
                "Count", BindingFlags.GetProperty, null, set, null) ?? 0);

            for (int i = 0; i < count; i++)
            {
                object? item = set.GetType().InvokeMember(
                    "ItemIndex", BindingFlags.InvokeMethod, null, set, new object[] { i });

                if (item != null) results.Add(item);
            }
        }
        catch (Exception ex)
        {
            // 记下真实原因再返回。这里原先只是静默吞掉异常，结果让一次
            // 「COM 参数类型不匹配」伪装成了「这台机器不支持 WMI 亮度」，
            // 排查代价极高 —— 所以失败必须留下证据，不能再吞。
            failure = $"{ex.GetType().Name}: {ex.Message}";
            results.Clear();
        }

        return results;
    }

    private static int Clamp(int percent) => percent < 0 ? 0 : (percent > 100 ? 100 : percent);

    // ==================== P/Invoke ====================

    private delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdcMonitor, ref RECT lprcMonitor, IntPtr dwData);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr lprcClip, MonitorEnumProc lpfnEnum, IntPtr dwData);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint numberOfPhysicalMonitors);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetPhysicalMonitorsFromHMONITOR(
        IntPtr hMonitor, uint physicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyPhysicalMonitors(
        uint physicalMonitorArraySize, [In] PHYSICAL_MONITOR[] physicalMonitorArray);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVCPFeatureAndVCPFeatureReply(
        IntPtr hMonitor, byte vcpCode, IntPtr pvct, out uint currentValue, out uint maximumValue);

    [DllImport("dxva2.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetVCPFeature(IntPtr hMonitor, byte vcpCode, uint newValue);
}

/// <summary>一块显示器的可调亮度信息（仅用于展示与诊断）。</summary>
internal sealed class MonitorBrightness
{
    public string Description { get; init; } = "";

    /// <summary>探测来源，如 <c>DDC/CI</c> 或 <c>WMI</c>。</summary>
    public string Channel { get; init; } = "";

    public bool IsWritable { get; init; }

    /// <summary>当前亮度百分比；-1 表示不可读。</summary>
    public int CurrentPercent { get; init; }

    /// <summary>诊断信息：通道不可用时用它说明原因。</summary>
    public string Diagnostic { get; init; } = "";

    public static MonitorBrightness Unavailable(string reason) => new()
    {
        Description = "（通道不可用）",
        IsWritable = false,
        CurrentPercent = -1,
        Diagnostic = reason,
    };
}

/// <summary>
/// 软件调光兜底通道：通过改写显示设备的 gamma ramp 让画面整体变暗。
/// <para>
/// <b>为什么需要它</b>：DDC/CI 要显示器配合、WMI 只有笔记本内置屏才有。台式机接一台
/// 没开 DDC/CI 的显示器时，前两条通道会同时落空 —— 此时若不给兜底，「调亮度」在这台机器上
/// 就是彻底不可用。gamma 方案不依赖任何硬件配合，任何显示器都能生效。
/// </para>
/// <para>
/// <b>它与硬件调光的区别（必须对用户诚实）</b>：硬件调光降低背光亮度，省电、对比度不变；
/// gamma 调光只是把像素值压低，背光功耗不变，黑色会变成灰黑、对比度下降。
/// 所以它的定位是「兜底」而不是「等价替代」，只有在硬件通道完全不可用时才启用。
/// </para>
/// <para>
/// <b>一个必须承担的责任</b>：gamma ramp 是<b>系统级</b>的，改完之后即使 StarPie 退出也不会
/// 自动恢复。所以本类在插件卸载时会把曲线写回基准值 —— 否则用户的屏幕会一直暗到重启为止。
/// </para>
/// </summary>
internal static class GammaController
{
    private static readonly object Gate = new();

    /// <summary>首次调整前捕获的原始曲线。所有调整都以它为基准，避免反复乘算累积误差。</summary>
    private static ushort[]? _baseline;

    private static int _currentPercent = 100;

    /// <summary>当前是否处于软件调光状态（亮度低于 100%）。</summary>
    public static bool IsApplied
    {
        get { lock (Gate) return _currentPercent < 100; }
    }

    /// <summary>当前软件亮度百分比。</summary>
    public static int CurrentPercent
    {
        get { lock (Gate) return _currentPercent; }
    }

    /// <summary>应用软件调光。</summary>
    public static bool Apply(int percent)
    {
        lock (Gate)
        {
            int target = Math.Clamp(percent, 1, 100);

            _baseline ??= Capture();
            if (_baseline == null) return false;

            // 按基准曲线整体缩放。逐通道独立缩放，这样用户原有的色温设置（若通过
            // gamma 调整过）能在调光后保持相对比例，不会因为统一缩放而偏色。
            var ramp = new RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256],
            };

            double factor = target / 100.0;

            for (int i = 0; i < 256; i++)
            {
                ramp.Red[i] = Scale(_baseline[i], factor);
                ramp.Green[i] = Scale(_baseline[256 + i], factor);
                ramp.Blue[i] = Scale(_baseline[512 + i], factor);
            }

            if (!Write(ramp)) return false;

            _currentPercent = target;
            return true;
        }
    }

    /// <summary>恢复首次调整前捕获的原始曲线。</summary>
    public static bool Restore()
    {
        lock (Gate)
        {
            if (_baseline == null)
            {
                _currentPercent = 100;
                return true;
            }

            var ramp = new RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256],
            };

            Array.Copy(_baseline, 0, ramp.Red, 0, 256);
            Array.Copy(_baseline, 256, ramp.Green, 0, 256);
            Array.Copy(_baseline, 512, ramp.Blue, 0, 256);

            bool ok = Write(ramp);

            // 即使写回失败也把状态标记成「未调光」：继续声称「正在软件调光」会让
            // 后续的相对调整从一个不存在的基准上算起，越调越偏。
            _currentPercent = 100;
            return ok;
        }
    }

    private static ushort Scale(ushort baselineValue, double factor)
    {
        int value = (int)Math.Round(baselineValue * factor);
        return (ushort)Math.Clamp(value, 0, 65535);
    }

    private static ushort[]? Capture()
    {
        IntPtr hdc = IntPtr.Zero;

        try
        {
            hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;

            var ramp = new RAMP
            {
                Red = new ushort[256],
                Green = new ushort[256],
                Blue = new ushort[256],
            };

            if (!GetDeviceGammaRamp(hdc, ref ramp)) return null;

            var baseline = new ushort[768];
            Array.Copy(ramp.Red, 0, baseline, 0, 256);
            Array.Copy(ramp.Green, 0, baseline, 256, 256);
            Array.Copy(ramp.Blue, 0, baseline, 512, 256);
            return baseline;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (hdc != IntPtr.Zero)
            {
                try { ReleaseDC(IntPtr.Zero, hdc); } catch { }
            }
        }
    }

    private static bool Write(RAMP ramp)
    {
        IntPtr hdc = IntPtr.Zero;

        try
        {
            hdc = GetDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return false;

            return SetDeviceGammaRamp(hdc, ref ramp);
        }
        catch
        {
            return false;
        }
        finally
        {
            if (hdc != IntPtr.Zero)
            {
                try { ReleaseDC(IntPtr.Zero, hdc); } catch { }
            }
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct RAMP
    {
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Red;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Green;

        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)]
        public ushort[] Blue;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetDeviceGammaRamp(IntPtr hdc, ref RAMP lpRamp);
}

/// <summary>一次亮度调整的结果汇总。</summary>
internal sealed class BrightnessOutcome
{
    /// <summary>成功调整的显示器数量（DDC/CI 与 WMI 合计）。</summary>
    public int Adjusted { get; set; }

    /// <summary>检测到但未能调整的显示器数量。</summary>
    public int Skipped { get; set; }

    /// <summary>成功调整的明细，形如「DELL U2720Q → 60%」。</summary>
    public List<string> AdjustedNames { get; } = new();

    /// <summary>调整过程中的错误信息。</summary>
    public List<string> Errors { get; } = new();

    /// <summary>Toggle 动作的最终目标值；仅 Toggle 会填，-1 表示未涉及。</summary>
    public int TargetPercent { get; set; } = -1;

    /// <summary>本次是否走了软件调光兜底（gamma）而不是硬件背光。</summary>
    public bool UsedSoftwareFallback { get; set; }

    /// <summary>
    /// 结果是否「值得一提」。
    /// <para>
    /// 调亮 / 调暗成功属于日常操作，弹气泡纯属打扰；但「已经是最亮，什么都没做」
    /// 「这次开始改用软件调光」这类结果用户必须知道，否则会误以为插件失灵。
    /// </para>
    /// </summary>
    public bool Notable { get; set; }

    public bool Success => Adjusted > 0;

    /// <summary>拼一句给用户看的结果描述。</summary>
    public string Describe(string verb)
    {
        if (Adjusted == 0)
        {
            string detail = Errors.Count > 0 ? "（" + string.Join("；", Errors) + "）" : "";
            return $"没有可用的调光通道{detail}。硬件通道（DDC/CI、WMI）与软件 gamma 调光都没能生效。";
        }

        string suffix = Skipped > 0 ? $"，另有 {Skipped} 块屏不支持" : "";
        string names = AdjustedNames.Count > 0 ? "：" + string.Join("、", AdjustedNames) : "";
        string mode = UsedSoftwareFallback
            ? "\n（硬件背光不可调，已改用软件调光 —— 它只压暗画面，不降低背光功耗）"
            : "";
        return $"已{verb} {Adjusted} 块屏幕{suffix}{names}{mode}";
    }
}
