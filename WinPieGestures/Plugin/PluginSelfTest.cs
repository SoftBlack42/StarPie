using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using StarPie.Plugin;

namespace WinPieGestures.Plugins;

/// <summary>
/// 插件系统端到端自检。
/// <para>
/// 它把「识别 → 安装 → 启用 → 注册 → 调用 → 停用 → 卸载」整条链路跑一遍并输出报告，
/// 存在的意义有两个：① 无界面环境下也能验证插件系统是否真的能跑通（CI 回归）；
/// ② 用户报告「插件装不上」时，一个命令就能拿到全链路证据。
/// </para>
/// <para>
/// 用法：<c>StarPie.exe --plugin-selftest &lt;插件.dll&gt; [报告输出路径] [--skip-invoke]</c>
/// </para>
/// <para>
/// <b>自检整体跑在临时沙箱里</b>：两个根目录（可写宿主区与只读扫描目录）都会被钉到
/// <c>%TEMP%\StarPie-PluginSelfTest-&lt;随机&gt;\</c> 下，跑完即删。以前它直接跑在真实插件目录上，
/// 等于每做一次回归就动一次用户已经装好的插件。
/// </para>
/// </summary>
internal static class PluginSelfTest
{
    public static int Run(string dllPath, string? reportPath, bool skipInvoke = false)
    {
        var report = new StringBuilder();
        bool pass = true;

        void Line(string text)
        {
            report.AppendLine(text);
            // 实时打到终端。这条通道以前只写 Debug（进调试器）与最终的报告文件，
            // 命令行里跑完什么都看不到 —— 而它存在的意义恰恰是「一条命令拿到全链路证据」，
            // 前提是那条命令的输出真的看得见（父控制台的接入见 App.AttachParentConsoleIfCli）。
            Console.WriteLine(text);
            System.Diagnostics.Debug.WriteLine(text);
        }

        void Fail(string stage, string reason)
        {
            pass = false;
            Line($"  [FAIL] {stage}：{reason}");
        }

        PluginCandidate? FindCandidate(string fileName) => PluginHost.Candidates.FirstOrDefault(
            c => string.Equals(c.FileName, fileName, StringComparison.OrdinalIgnoreCase));

        Line("====================================================");
        Line("StarPie 插件系统端到端自检");
        Line($"时间：{DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        Line($"宿主版本：{PluginManifestReader.HostVersion} / SDK 契约：{PluginApi.ApiVersion}");
        Line($"目标文件：{dllPath}");
        if (skipInvoke)
        {
            Line("运行模式：--skip-invoke —— 跳过真实调用，不会改变本机环境");
        }
        Line("====================================================");

        // ---- 沙箱：把两个根目录钉到临时位置，跑完即删 ----
        string sandboxRoot = Path.Combine(
            Path.GetTempPath(), "StarPie-PluginSelfTest-" + Guid.NewGuid().ToString("N"));
        string sandboxHostRoot = Path.Combine(sandboxRoot, "plugin-data");
        string sandboxScanRoot = Path.Combine(sandboxRoot, "plugin");

        try
        {
            Directory.CreateDirectory(sandboxHostRoot);
            Directory.CreateDirectory(sandboxScanRoot);
            PluginPaths.OverrideRootsForTesting(sandboxHostRoot, sandboxScanRoot);
            Line($"沙箱目录：{sandboxRoot}（真实插件目录不会被触碰）");
        }
        catch (Exception sandboxError)
        {
            // 建不出沙箱就如实说明，不要假装自己是隔离的
            Line($"[WARN] 无法创建自检沙箱（{sandboxError.Message}），本次将直接跑在真实插件目录上。");
        }
        Line("====================================================");

        string? installedPluginId = null;

        try
        {
            // ---- 0 初始化 ----
            Line("");
            Line("[0] 初始化插件系统");
            var sw = Stopwatch.StartNew();

            // 自检是无界面短命进程，不该被计入启动健康统计。
            PluginHost.HeadlessMode = true;
            PluginHost.Initialize();
            sw.Stop();
            Line($"  插件根目录：{PluginPaths.Root}");
            Line($"  便携模式：{PluginPaths.IsPortable}");
            Line($"  初始化耗时：{sw.Elapsed.TotalMilliseconds:F1} ms");
            Line($"  已登记插件：{PluginHost.InstalledCount} 个");

            // ---- 1 静态识别 ----
            Line("");
            Line("[1] 静态识别（不加载程序集）");
            sw.Restart();
            PluginScanResult scan = PluginHost.PrepareInstall(dllPath);
            sw.Stop();
            Line($"  识别耗时：{sw.Elapsed.TotalMilliseconds:F2} ms");
            Line($"  结论：{(scan.Accepted ? "通过" : "拒绝")}");

            if (!scan.Accepted)
            {
                Line($"  原因码：{scan.Failure}");
                Line($"  标题：{PluginScanFailureText.Title(scan.Failure)}");
                Line($"  详情：{scan.ErrorDetail}");
                Line($"  修复建议：{PluginScanFailureText.Hint(scan.Failure)}");
                Fail("静态识别", scan.DescribeFailure());
                return Write(report, reportPath, pass);
            }

            PluginManifest manifest = scan.Manifest!;
            Line($"  清单来源：{scan.ManifestSource}");
            Line($"  ID：{manifest.Id}");
            Line($"  名称：{manifest.Name} v{manifest.Version}");
            Line($"  作者：{manifest.Author}");
            Line($"  许可证：{manifest.License}");
            Line($"  能力声明：{manifest.ResolveCapabilities()}");
            Line($"  入口类型：{scan.EntryTypeFullName ?? "(未解析)"}");
            Line($"  实际 TFM：{scan.TargetFramework}");
            Line($"  架构：{scan.MachineText}（依赖文件：{(scan.HasDependencyFile ? "有" : "无")}）");
            Line($"  文件大小：{scan.FileSizeText}");
            Line($"  SHA256：{scan.Sha256}");
            Line($"  签名：{(scan.IsSigned ? $"已签名（{scan.SignerSubject}）" : "未签名")}");

            installedPluginId = manifest.Id;

            // ---- 2 安装 ----
            Line("");
            Line("[2] 安装（复制落盘 + 登记为 Disabled）");
            var options = new PluginInstallOptions
            {
                Acknowledged = true,
                OverwriteExisting = true,
                EnableAfterInstall = false,
                AcknowledgedCapabilities = manifest.Capabilities,
            };
            PluginInstallResult install = PluginHost.CommitInstall(scan, options);
            if (!install.Success)
            {
                Fail("安装", install.Error);
                return Write(report, reportPath, pass);
            }
            Line($"  安装成功：{install.PluginId}");

            PluginInstance? instance = PluginHost.Find(install.PluginId);
            Line($"  安装后状态：{instance?.State}（已加载：{instance?.IsLoaded}）");
            if (instance?.IsLoaded == true)
            {
                Fail("安装语义", "安装后不应加载程序集，但要保持内存红线");
            }

            // ---- 3 启用 + 4 调用 ----
            // 刻意放进独立方法：这两步会拿到 PluginActionRegistration，而它的 Contribution
            // 指向插件程序集里的类型实例。这些引用若留在 Run 的栈帧上，第 5 步卸载时插件的
            // ALC 就回收不掉 —— 自检会把自己测挂，报告里出现假的「需要重启才能释放」。
            string? stageError = RunEnableAndInvoke(install.PluginId, Line, skipInvoke);
            if (stageError != null)
            {
                Fail("启用与调用", stageError);
            }

            // ---- 5 停用 ----
            Line("");
            Line("[5] 停用（撤销贡献点 → 剪断订阅 → Shutdown → 卸载 ALC）");
            sw.Restart();
            bool disabled = PluginHost.Disable(install.PluginId, out string disableError);
            sw.Stop();
            Line($"  停用结果：{(disabled ? "成功" : "失败")}（{sw.Elapsed.TotalMilliseconds:F1} ms）");
            if (!disabled) Fail("停用", disableError);

            // 等延迟判定给出最终结论。同步探测常常因为调用栈还没展开而回收不掉，
            // 不等它就会把「其实已经释放」误报成「需要重启」。
            instance = PluginHost.Find(install.PluginId);
            bool unloaded = instance?.WaitForUnloadVerdict(5000) ?? false;
            Line($"  停用后状态：{instance?.State}");
            Line($"  需要重启才能释放：{instance?.RequiresRestart}");
            if (!unloaded)
            {
                Fail("ALC 卸载", "插件程序集未被回收，停用要重启才能真正生效");
            }
            Line($"  剩余已注册动作：{PluginHost.GetRegisteredActions().Count} 个");

            if (PluginHost.GetRegisteredActions().Count != 0)
            {
                Fail("贡献点撤销", "停用后仍有动作残留在注册表里");
            }

            // ---- 6 卸载 ----
            Line("");
            Line("[6] 卸载（删除目录 + 移除登记）");
            bool uninstalled = PluginHost.Uninstall(install.PluginId, removePluginData: true, out string uninstallError);
            Line($"  卸载结果：{(uninstalled ? "成功" : "失败")}");
            if (!uninstalled)
            {
                Fail("卸载", uninstallError);
            }
            else
            {
                installedPluginId = null;
            }

            // ---- 3d 只读扫描目录（候选识别 → 单枚复制 → 装后状态）----
            Line("");
            Line("[3d] 只读扫描目录与候选安装（沙箱内）");

            string candidateFileName = Path.GetFileName(dllPath);
            const string DecoyFileName = "notaplugin.dll";

            // ① 空目录必须是 0 个候选
            int emptyCount = PluginHost.ScanCandidates();
            if (emptyCount != 0)
            {
                Fail("候选扫描", $"空的扫描目录里扫出了 {emptyCount} 个候选");
            }

            // ② 放一枚真插件，再放一枚「看着像 dll 其实不是」的文件
            File.Copy(dllPath, Path.Combine(sandboxScanRoot, candidateFileName), overwrite: true);
            File.WriteAllText(Path.Combine(sandboxScanRoot, DecoyFileName), "这只是一个文本文件，不是程序集。");
            PluginHost.ScanCandidates();

            PluginCandidate? real = FindCandidate(candidateFileName);
            PluginCandidate? decoy = FindCandidate(DecoyFileName);

            if (real == null)
            {
                Fail("候选扫描", $"扫描目录里没有扫出 {candidateFileName}");
            }
            else if (real.State != PluginCandidateState.Installable)
            {
                Fail("候选扫描", $"未安装过的插件应判为「可安装」，实际是 {real.State}");
            }

            if (decoy == null)
            {
                Fail("候选扫描", "非程序集文件没有被扫出来 —— 用户会以为「放进去了却毫无反应」");
            }
            else
            {
                Line($"  非程序集文件的结论：{decoy.StateText}｜{decoy.Note}");
                if (decoy.State != PluginCandidateState.Rejected || decoy.CanInstall)
                {
                    Fail("候选扫描", "非程序集文件必须判为「无法识别」且不给安装按钮");
                }
            }

            Line($"  候选数：{PluginHost.Candidates.Count} 个（可安装 {PluginHost.Candidates.Count(x => x.CanInstall)} 个）");

            if (real != null)
            {
                // ③ 点「安装」—— 与界面上那个按钮完全同一条路
                bool installedByCandidate = PluginHost.InstallCandidate(real, out string candidateError);
                if (!installedByCandidate)
                {
                    Fail("候选安装", candidateError);
                }
                else
                {
                    PluginInstance? installed = PluginHost.Find(real.PluginId!);
                    Line($"  安装后状态：{installed?.State}｜登记来源：{installed?.Entry.Source}");

                    // 裸 DLL 安装必须「装完就能跑」。这里曾经是个真缺陷：裸 dll 安装不回填
                    // plugin.json，而安装目录的识别要求目录里有清单 —— 于是插件装得上却永远
                    // 启用不了，报错是一句与真实原因无关的「插件目录里缺少 plugin.json」。
                    if (installed?.State != PluginRuntimeState.Active)
                    {
                        Fail("候选安装启用",
                            $"候选安装后插件应处于运行态，实际是 {installed?.State}（{installed?.LastError}）");
                    }

                    string installedManifest = PluginPaths.GetManifestPath(installed?.ManagedDirectory ?? "");
                    if (!File.Exists(installedManifest))
                    {
                        Fail("候选安装启用", $"裸 DLL 安装没有回填清单，后续识别与启用都会失败：{installedManifest}");
                    }

                    // C1 回归断言：裸 DLL 安装只复制那一枚，绝不能把扫描目录里的邻居一起搬走。
                    // 搬走邻居的后果不是「多几个文件」这么轻：装了 A 却连带出现 B，
                    // 而且 B 还会因为目录里存在两枚业务 dll 而识别失败。
                    string managedDirectory = installed?.ManagedDirectory ?? "";
                    string[] managedDlls = Directory.Exists(managedDirectory)
                        ? Directory.GetFiles(managedDirectory, "*.dll", SearchOption.TopDirectoryOnly)
                        : Array.Empty<string>();

                    Line($"  宿主目录内的程序集：{managedDlls.Length} 枚" +
                        (managedDlls.Length > 0 ? $"（{string.Join("、", managedDlls.Select(Path.GetFileName))}）" : ""));

                    if (managedDlls.Length != 1
                        || !string.Equals(Path.GetFileName(managedDlls[0]), candidateFileName, StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("单枚复制",
                            $"裸 DLL 安装只应复制 {candidateFileName} 这一枚，实际宿主目录里有 {managedDlls.Length} 枚");
                    }

                    if (!string.Equals(installed?.Entry.Source, "ScanDirectory", StringComparison.Ordinal))
                    {
                        Fail("安装来源", $"候选安装的登记来源应为 ScanDirectory，实际是 {installed?.Entry.Source}");
                    }

                    // ④ 重扫：同一枚文件应变成「已装同版本」
                    PluginHost.ScanCandidates();
                    PluginCandidate? afterInstall = FindCandidate(candidateFileName);
                    Line($"  重扫后状态：{afterInstall?.StateText ?? "(消失)"}");
                    if (afterInstall?.State != PluginCandidateState.Installed)
                    {
                        Fail("装后状态",
                            $"装完之后同一枚文件应判为「已装同版本」，实际是 {afterInstall?.State.ToString() ?? "(消失)"}");
                    }

                    // ⑤ 同 ID 撞车：两枚都必须是「ID 重复」且都不给安装按钮
                    string duplicateName = "copy-" + candidateFileName;
                    File.Copy(dllPath, Path.Combine(sandboxScanRoot, duplicateName), overwrite: true);
                    PluginHost.ScanCandidates();

                    PluginCandidate? first = FindCandidate(candidateFileName);
                    PluginCandidate? second = FindCandidate(duplicateName);
                    Line($"  ID 重复：{first?.StateText ?? "(消失)"} / {second?.StateText ?? "(消失)"}");

                    if (first?.State != PluginCandidateState.Duplicate || second?.State != PluginCandidateState.Duplicate)
                    {
                        Fail("ID 重复", "扫描目录里两枚 dll 声明同一 ID 时，两者都必须判为「ID 重复」");
                    }
                    else if (first.CanInstall || second.CanInstall)
                    {
                        Fail("ID 重复", "ID 重复的候选一律不能给安装按钮 —— 装哪一枚都说不清");
                    }
                    else if (first.Note.IndexOf(real.PluginId!, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        Fail("ID 重复", $"冲突说明里应写明撞车的是哪个 ID，实际是：{first.Note}");
                    }
                    else if (string.Equals(first.FileName, second.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        Fail("ID 重复", "两行候选必须各自显示自己的文件名，否则用户看不出该删哪一个");
                    }

                    // ⑥ 收拾干净：停用 → 等 ALC 回收结论 → 卸载 → 删掉扫描目录
                    //
                    // 顺序和 [5]/[6] 一致，不能省掉「等回收」这一步：插件程序集还挂在
                    // 未卸载的 ALC 上时文件是锁着的，此刻删目录会失败 —— 而失败又只体现在
                    // 一个被吞掉的异常里，表现就是临时目录里一次次堆出残留沙箱。
                    PluginHost.Disable(real.PluginId!, out _);
                    PluginHost.Find(real.PluginId!)?.WaitForUnloadVerdict(5000);

                    if (!PluginHost.Uninstall(real.PluginId!, removePluginData: true, out string cleanupError))
                    {
                        Fail("候选安装清理", cleanupError);
                    }
                }

                Directory.Delete(sandboxScanRoot, recursive: true);
                int afterDelete = PluginHost.ScanCandidates();
                bool recreated = Directory.Exists(sandboxScanRoot);

                Line($"  扫描目录删除后：候选 {afterDelete} 个，目录被重建={recreated}");
                if (afterDelete != 0)
                {
                    Fail("候选扫描", $"扫描目录已删除，却仍扫出 {afterDelete} 个候选");
                }
                if (recreated)
                {
                    Fail("扫描目录", "扫描目录不存在时被重新创建了 —— 程序装在只读位置会直接变成权限错误");
                }
            }

            // ---- 7 环境还原性检查 ----
            Line("");
            Line("[7] 环境还原性检查");
            Line($"  残留登记插件：{PluginHost.InstalledCount} 个");
            Line($"  残留插件词条：{I18n.ExternalTranslationCount} 条");
            if (I18n.ExternalTranslationCount != 0)
            {
                Fail("词条清理", "卸载后仍有插件词条残留（会造成语言切换时显示脏数据）");
            }
        }
        catch (Exception ex)
        {
            Fail("未捕获异常", ex.ToString());
        }
        finally
        {
            // 自检失败时不要把用户的插件目录弄脏
            if (installedPluginId != null)
            {
                try
                {
                    PluginHost.Uninstall(installedPluginId, removePluginData: true, out _);
                }
                catch
                {
                }
            }

            // 删掉整个沙箱。删不掉要如实说 —— 静默吞掉的话，临时目录会一次次堆出残留，
            // 而下次排查「磁盘怎么满了」时没人会想到是自检干的。
            try
            {
                Directory.Delete(sandboxRoot, recursive: true);
            }
            catch (Exception cleanupError)
            {
                Line($"  [WARN] 沙箱未能删除（{cleanupError.Message}）：{sandboxRoot}");
            }
        }

        Line("");
        Line("====================================================");
        Line(pass ? "自检结论：PASS —— 全链路可用" : "自检结论：FAIL —— 见上面 [FAIL] 项");
        Line("====================================================");

        return Write(report, reportPath, pass);
    }

    /// <summary>
    /// 阶段 3（启用）+ 阶段 4（调用）。
    /// <para>
    /// <b>必须独立成方法并禁止内联</b>：本方法持有 <see cref="PluginActionRegistration"/>，
    /// 它间接指向插件程序集里的类型实例。只有让这些引用随本方法的栈帧一起消失，
    /// 后续「停用 → ALC 卸载」的判定才可能为真。
    /// </para>
    /// </summary>
    /// <returns>失败原因；<c>null</c> 表示两个阶段都通过。</returns>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string? RunEnableAndInvoke(string pluginId, Action<string> line, bool skipInvoke = false)
    {
        // ---- 3 启用 ----
        line("");
        line("[3] 启用（加载 → 实例化 → Initialize → 提交贡献点）");
        var sw = Stopwatch.StartNew();
        bool enabled = PluginHost.Enable(pluginId, out string enableError);
        sw.Stop();
        line($"  启用结果：{(enabled ? "成功" : "失败")}");
        line($"  启用耗时：{sw.Elapsed.TotalMilliseconds:F1} ms");
        if (!enabled) return $"启用失败：{enableError}";

        PluginInstance? instance = PluginHost.Find(pluginId);
        line($"  加载耗时（内部计量）：{instance?.LastLoadMs:F1} ms");
        line($"  运行时状态：{instance?.State}");

        List<PluginActionRegistration> actions = PluginHost.GetRegisteredActions();
        line($"  已注册动作：{actions.Count} 个");
        foreach (PluginActionRegistration action in actions)
        {
            line($"    · {action.FullId} | {action.DisplayName} | {action.Kind} | 参数 {action.Parameters.Count} 项");
        }
        if (actions.Count == 0) return "插件启用成功但一个动作都没注册";

        // 词条命中率单独成段。显示名有字面文案兜底，所以「词条没接上」在界面上
        // 与「接上了」长得一模一样 —— 必须在这里显式暴露，否则插件作者要等到
        // 用户切换语言、发现名字没变，才会意识到自己的 key 一直没生效。
        int keyed = 0;
        int resolved = 0;
        var missed = new List<string>();

        foreach (PluginActionRegistration action in actions)
        {
            if (string.IsNullOrEmpty(action.DisplayNameKey)) continue;
            keyed++;
            if (action.DisplayNameFromI18n) resolved++;
            else missed.Add($"{action.ShortId}（{action.DisplayNameKey}）");
        }

        line("");
        line($"  词条解析：声明了 DisplayNameKey 的 {keyed} 个动作中，命中 {resolved} 个");

        // 一个 key 都没声明不算问题：字面 DisplayName 是完全合法且推荐的兜底写法。
        if (keyed > 0 && resolved < keyed)
        {
            line($"    ⚠️ 未命中：{string.Join("、", missed)}");
            line("    这些动作会退回字面 DisplayName 显示，译文不会生效。");
        }

        // ---- 3b 参数校验 ----
        //
        // 这一段验证的是「声明即校验」：插件只声明 ParameterField、一行校验代码都不写，
        // 宿主也必须能拦下空值、越界值与非法选项。
        // 之所以要在这里断言，是因为这一层「没生效」时完全没有外在症状 ——
        // 界面照常渲染、边界值照常存进配置，直到用户触发时插件自己拒绝才暴露。
        line("");
        line("[3b] 参数校验（声明驱动的约束）");

        List<PluginActionRegistration> parameterized = actions.Where(a => a.Parameters.Count > 0).ToList();

        if (parameterized.Count == 0)
        {
            line("  本插件没有声明任何参数，跳过。");
        }
        else
        {
            foreach (PluginActionRegistration candidate in parameterized)
            {
                line($"  样本动作：{candidate.ShortId}（声明 {candidate.Parameters.Count} 项）");

                foreach (ParameterField field in candidate.Parameters)
                {
                    string range = field.Min.HasValue && field.Max.HasValue
                        ? $"　范围 {FormatBound(field.Min.Value)}~{FormatBound(field.Max.Value)}"
                        : (field.Max.HasValue ? $"　上限 {FormatBound(field.Max.Value)}" : "");

                    line($"    · {field.Key}｜{field.Type}｜必填={field.Required}{range}");
                }

                bool hasRequired = candidate.Parameters.Any(
                    p => p.Required && p.Type != ParameterFieldType.Bool);

                // ① 全空输入：声明了必填就必须被拦下
                List<PluginParameterIssue> emptyIssues = PluginParameterValidator.Validate(
                    candidate.Parameters,
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

                line($"    ① 全空输入 → {emptyIssues.Count} 项不通过" +
                     (emptyIssues.Count > 0 ? $"（{emptyIssues[0]}）" : ""));

                if (hasRequired && emptyIssues.Count == 0)
                {
                    return $"「{candidate.ShortId}」声明了必填参数，但全空输入未被拦下 —— 空值会直接存进配置。";
                }

                // ②③④ 都需要一份「除被测字段外其余都合法」的基线。
                //
                // 只有当插件为每个字段都声明了 DefaultValue 时这份基线才存在。
                // 否则我们只能自己编一个值（比如给热键字段填 "x"），而那个值可能
                // 恰好过不了插件自己的 ValidationRegex —— 于是断言会因为「别的字段」而
                // 通过或失败，测试自己制造出假阳性与假阴性。自检工具宁可少测一种情形，
                // 也不能给出不可信的结论。
                bool baselineAvailable = candidate.Parameters.All(
                    p => string.IsNullOrEmpty(p.Key) || p.DefaultValue != null);

                if (!baselineAvailable)
                {
                    line("    ②～④ 跳过：本动作有字段未声明 DefaultValue，无法构造可信的基线输入。");
                    continue;
                }

                Dictionary<string, string> baseline = CollectDefaults(candidate.Parameters);

                // ② 越界输入：把带上限的数值字段设成 上限+1。
                // 断言的是「该字段名下确实出现了错误」，而不是「错误总数 > 0」——
                // 后者可能来自另一个字段，让这条断言在错误的原因下通过。
                ParameterField? ranged = candidate.Parameters.FirstOrDefault(
                    p => p.Type == ParameterFieldType.Number && p.Max.HasValue);

                if (ranged != null)
                {
                    var overflow = new Dictionary<string, string>(baseline, StringComparer.OrdinalIgnoreCase);
                    string tooBig = FormatBound(ranged.Max!.Value + 1);
                    overflow[ranged.Key] = tooBig;

                    List<PluginParameterIssue> overflowIssues =
                        PluginParameterValidator.Validate(candidate.Parameters, overflow);

                    bool attributed = overflowIssues.Any(
                        i => string.Equals(i.Key, ranged.Key, StringComparison.OrdinalIgnoreCase));

                    line($"    ② {ranged.Key}={tooBig}（上限 {FormatBound(ranged.Max.Value)}）→ " +
                         (attributed ? "已拦下" : "未拦下"));

                    if (!attributed)
                    {
                        return $"「{candidate.ShortId}」的 {ranged.Key} 超过声明上限却未被拦下。";
                    }
                }

                // ③ 正向用例：按声明的默认值填充，必须全部通过。
                // 缺了这条，任何「一律报错」的实现都能骗过上面两条断言。
                List<PluginParameterIssue> validIssues =
                    PluginParameterValidator.Validate(candidate.Parameters, baseline);

                line($"    ③ 按声明默认值填充 → {validIssues.Count} 项不通过" +
                     (validIssues.Count > 0 ? $"（{validIssues[0]}）" : ""));

                if (validIssues.Count > 0)
                {
                    return $"「{candidate.ShortId}」合法的默认值被判为不合法，会拦住本可正常使用的配置。";
                }

                // ④ 两层校验（宿主声明约束 + 插件自定义）必须对同一份输入给出一致结论。
                // 结论相反时用户会遇到最难自查的一种状态：表单全绿，一触发却被拒。
                //
                // 这里只警告、不判失败：有些插件的规则本身就与默认值互斥
                // （例如「起止时间不能相同」而两者默认值恰好相同），那是声明的写法问题，
                // 不该被自检判成宿主缺陷。
                ActionItem? probeItem = PluginHost.CreateActionItem(candidate.FullId, baseline);
                if (probeItem != null)
                {
                    PluginHost.PluginActionValidation unified =
                        PluginHost.ValidateActionParameters(probeItem);

                    line($"    ④ 走统一入口校验同一份输入 → {(unified.IsValid ? "通过" : "不通过")}");

                    if (!unified.IsValid)
                    {
                        line($"       ⚠️ {unified.Describe()}");
                        line("          声明约束与插件自定义校验结论相反。若两者规则本身互斥（如默认值不满足自定规则），");
                        line("          属声明写法问题；否则说明有一层漏判。此项不判失败，请作者自行确认。");
                    }
                }
            }
        }

        // ---- 3c 选择器接缝 ----
        line("");
        line("[3c] 动作选择器接缝（类型收敛 + 按插件分组的子下拉）");

        // 类型下拉里的插件项只能有一项。
        // 若像早先那样把每个插件动作都平铺进去，装十个插件就会多出上百项，
        // 把内置动作挤到看不见的地方 —— 而内置项的顺序属于用户的肌肉记忆。
        List<ActionTypeItem> typeItems = PluginActionBinding.BuildActionTypeItems();
        line($"    类型下拉里的插件项：{typeItems.Count} 项（应为 1 项）");
        if (typeItems.Count != 1)
        {
            return $"类型下拉里的插件项应为 1 项，实际 {typeItems.Count} 项 —— 装一个插件就多一项会把内置动作挤走。";
        }
        if (!string.Equals(typeItems[0].Tag, PluginApi.ActionTypeName, StringComparison.Ordinal))
        {
            return $"类型下拉的插件项 Tag 应为 {PluginApi.ActionTypeName}，实际是「{typeItems[0].Tag}」。";
        }

        // 子下拉候选：每个已注册动作都必须出现，且同一插件的动作落在同一分组。
        List<PluginActionRegistration> registered = PluginHost.GetRegisteredActions();
        List<PluginActionItem> options = PluginActionBinding.BuildPluginActionItems();

        line($"    子下拉候选：{options.Count} 项 / 已注册动作 {registered.Count} 项");
        if (options.Count != registered.Count)
        {
            return "子下拉候选数与已注册动作数不一致 —— 用户会看到少了动作，却无从判断少了哪些。";
        }

        var groupOfPlugin = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (PluginActionRegistration registration in registered)
        {
            PluginActionItem? option = options.FirstOrDefault(o => o.FullId == registration.FullId);
            if (option == null)
            {
                return $"已注册动作 {registration.FullId} 未出现在子下拉候选里。";
            }
            if (string.IsNullOrWhiteSpace(option.GroupName))
            {
                return $"动作 {registration.FullId} 没有分组名 —— 它在下拉里会成为没有归属的孤儿项。";
            }

            // 同一插件的动作必须归入同一分组。若按动作名去分组，
            // 一个插件的各个动作会各自成组，界面立刻变成一锅粥。
            if (groupOfPlugin.TryGetValue(registration.PluginId, out string? existing))
            {
                if (!string.Equals(existing, option.GroupName, StringComparison.Ordinal))
                {
                    return $"同一插件（{registration.PluginId}）的动作被分到了不同分组：" +
                           $"「{existing}」与「{option.GroupName}」。";
                }
            }
            else
            {
                groupOfPlugin[registration.PluginId] = option.GroupName;
            }
        }

        // 插件显示名是否真的互相冲突 —— 只有冲突时，组标题才允许带上插件 ID 后缀。
        List<string> pluginIds = new List<string>(groupOfPlugin.Keys);
        var displayNames = pluginIds
            .Select(id => PluginActionBinding.ResolvePluginDisplayName(id))
            .ToList();
        bool nameCollision = displayNames.Count != displayNames.Distinct(StringComparer.Ordinal).Count();

        foreach (string ownerId in pluginIds)
        {
            string groupName = groupOfPlugin[ownerId];
            string expectedName = PluginActionBinding.ResolvePluginDisplayName(ownerId);
            int count = registered.Count(r => string.Equals(r.PluginId, ownerId, StringComparison.Ordinal));
            line($"    分组「{groupName}」→ {count} 个动作");

            if (!nameCollision)
            {
                // 插件名互不相同是常态，此时组标题必须就是插件名本身。
                // 多出任何后缀都会让用户以为装了别的什么插件 —— 而这类问题在界面上
                // 看起来完全正常，只有对着插件列表才发现对不上。
                if (!string.Equals(groupName, expectedName, StringComparison.Ordinal))
                {
                    return $"插件 {ownerId} 的分组名「{groupName}」应为「{expectedName}」—— " +
                           "插件名并不重复，不该给组标题加后缀。";
                }
                continue;
            }

            if (!groupName.StartsWith(expectedName, StringComparison.Ordinal))
            {
                return $"插件 {ownerId} 的分组名「{groupName}」与它的显示名「{expectedName}」不一致 —— " +
                       "子下拉的组标题会与详情面板里的插件标识对不上号。";
            }
        }

        // 写读往返：子下拉选中 → 落库 → 再投影回下拉，必须仍是同一个动作。
        // 这条路断了会出现最难查的一类故障：界面看着正常，触发时却是另一个动作。
        foreach (PluginActionRegistration registration in registered)
        {
            var probe = new ActionItem
            {
                Type = PluginApi.ActionTypeName,
                ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
            };

            if (!PluginActionBinding.Apply(probe, registration.FullId))
            {
                return $"写入动作 {registration.FullId} 失败。";
            }

            string? projected = PluginActionBinding.ProjectSelectedAction(probe);
            if (!string.Equals(projected, registration.FullId, StringComparison.Ordinal))
            {
                return $"动作 {registration.FullId} 写入后投影回来变成了「{projected ?? "(空)"}」—— " +
                       "界面会显示成没选动作。";
            }

            if (PluginActionBinding.IsReferenceBroken(probe))
            {
                return $"刚写入的动作 {registration.FullId} 立刻被判为「引用已失效」。";
            }

            if (!string.Equals(probe.Type, PluginApi.ActionTypeName, StringComparison.Ordinal))
            {
                return $"写入后 Type 变成了「{probe.Type}」，应为 {PluginApi.ActionTypeName} —— 类型下拉会选不中。";
            }
        }
        line($"    写读往返：{registered.Count} 个动作全部一致");

        // 切回内置类型必须清干净，否则留下「内置类型 + 悬挂插件引用 + 插件参数」的混合状态，
        // 那种配置界面上看不出来，却会在导出与执行时各表现一次。
        var cleared = new ActionItem { Type = PluginApi.ActionTypeName };
        PluginActionBinding.Apply(cleared, registered[0].FullId);
        PluginActionBinding.Clear(cleared);
        if (cleared.PluginActionRef != null || cleared.ExtensionData != null)
        {
            return "Clear 之后仍有插件引用或插件参数残留。";
        }
        line("    切回内置类型：插件引用与插件参数均已清空");

        // ---- 4 调用 ----
        line("");
        line("[4] 调用动作（走与轮盘完全相同的接缝）");

        if (skipInvoke)
        {
            // 只想确认识别 / 注册 / 参数校验 / 选择器接缝时应当走这条：「真执行一次动作」
            // 对亮度、音量、剪贴板这类动作就是实打实的副作用，CI 与排查问题
            // 都不该顺手改动用户的机器（实测踩过：反复跑自检把屏幕亮度从 15% 推到 75%）。
            line("  已跳过（--skip-invoke）：识别、注册、参数校验与选择器接缝断言均已跑过，本机环境未被改动。");
            return null;
        }

        // 这一节是**真执行**，不是只读检查。
        // 明写出来是必要的：自检报告通篇读起来像一次静态体检，
        // 而亮度插件这类动作一旦被执行就会真的改变系统状态 ——
        // 作者若以为它是只读的，就会在排查问题时反复跑自检，
        // 结果是把用户的屏幕、音量或剪贴板越改越乱却毫无察觉。
        line("  ⚠️ 本节会真实调用一次动作，可能改变系统状态（如亮度、音量、剪贴板）。");
        PluginActionRegistration first = actions[0];
        ActionItem? actionItem = PluginHost.CreateActionItem(first.FullId);
        if (actionItem == null) return "CreateActionItem 返回 null";

        line($"  动作 Type：{actionItem.Type}");
        line($"  引用：{actionItem.PluginActionRef}");
        line($"  参数：{DescribeParameters(actionItem.ExtensionData)}");

        sw.Restart();
        PluginExecuteOutcome outcome = PluginHost.ExecutePluginAction(actionItem);
        sw.Stop();

        line($"  是否被处理：{outcome.Handled}");
        line($"  成功：{outcome.Success}");
        line($"  后台执行：{outcome.QueuedToBackground}");
        line($"  返回信息：{outcome.Message}");
        line($"  调用耗时：{sw.Elapsed.TotalMilliseconds:F3} ms");

        if (!outcome.Handled || !outcome.Success) return $"动作调用失败：{outcome.Message}";

        // 负向用例：引用一个不存在的贡献点。
        //
        // 这里刻意**不用**「缺少必填参数」来构造负例：那需要真的调用一次动作，
        // 对无参数的动作（例如亮度插件的多数动作）会真的被执行一遍，
        // 于是「自检」本身产生了副作用 —— 屏幕亮度被多调了一次。
        // 改用不存在的贡献点，既能验证宿主的防御路径（不崩溃、不静默成功），
        // 又保证零副作用，而且对任何插件都成立。
        line("  负向用例：引用不存在的贡献点（零副作用）");
        var ghost = new ActionItem
        {
            Type = PluginApi.ActionTypeName,
            Name = first.DisplayName,
            PluginActionRef = new PluginActionRef { PluginId = first.PluginId, ContributionId = "no_such_contribution_zzz" },
            ExtensionData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase),
        };
        PluginExecuteOutcome ghostOutcome = PluginHost.ExecutePluginAction(ghost);
        line($"    被处理={ghostOutcome.Handled} 成功={ghostOutcome.Success} 信息={ghostOutcome.Message}");

        if (ghostOutcome.Success)
        {
            return "宿主防御异常：引用不存在的贡献点却报告成功，用户会看到一个不存在的动作被静默执行。";
        }

        return null;
    }

    /// <summary>
    /// 收集插件声明的默认值，作为「应当合法」的基线输入。
    /// <para>
    /// 刻意<b>只</b>照抄声明，不为缺失默认值的字段编造任何值：
    /// 编出来的值（例如给热键字段填 <c>"x"</c>）可能过不了插件自己的
    /// <c>ValidationRegex</c>，于是自检会因为「测试自己造的输入」而报出宿主缺陷。
    /// 调用方需先用 <c>baselineAvailable</c> 确认每个字段都有默认值。
    /// </para>
    /// </summary>
    private static Dictionary<string, string> CollectDefaults(IReadOnlyList<ParameterField> fields)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (ParameterField field in fields)
        {
            if (string.IsNullOrEmpty(field.Key)) continue;
            if (field.DefaultValue == null) continue;
            result[field.Key] = field.DefaultValue;
        }

        return result;
    }

    /// <summary>把范围边界显示成「0」而不是「0.0」，避免报告里出现无意义的尾数。</summary>
    private static string FormatBound(double value) =>
        value == Math.Floor(value) && Math.Abs(value) < 1e15
            ? ((long)value).ToString(CultureInfo.InvariantCulture)
            : value.ToString("0.####", CultureInfo.InvariantCulture);

    private static string DescribeParameters(Dictionary<string, string>? parameters)
    {
        if (parameters == null || parameters.Count == 0) return "(无)";

        var parts = new List<string>();
        foreach (KeyValuePair<string, string> pair in parameters)
        {
            parts.Add($"{pair.Key}={pair.Value}");
        }
        return string.Join(", ", parts);
    }

    private static int Write(StringBuilder report, string? reportPath, bool pass)
    {
        string text = report.ToString();
        string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");

        // 报告是这条通道唯一的产物，**绝不能写不出去还不作声**。
        // 这里以前是个空的 catch：结果是「退出码 0、报告却遍寻不着」，而且毫无线索 ——
        // 一次成功的自检看起来和一次静默失败一模一样。
        string[] candidates = !string.IsNullOrWhiteSpace(reportPath)
            ? new[] { reportPath! }
            : new[]
            {
                Path.Combine(Path.GetTempPath(), $"starpie-plugin-selftest-{stamp}.txt"),
                // 临时目录写不进去（权限受限、被重定向、被清理）时退到日志目录：
                // 那里必然可写，否则日志本身也写不了。
                Path.Combine(AppLogger.GetLogFolderPath(), $"starpie-plugin-selftest-{stamp}.txt"),
            };

        string? written = null;
        Exception? lastError = null;

        foreach (string candidate in candidates)
        {
            try
            {
                string? directory = Path.GetDirectoryName(candidate);
                if (!string.IsNullOrEmpty(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                File.WriteAllText(candidate, text, Encoding.UTF8);
                written = candidate;
                break;
            }
            catch (Exception ex)
            {
                lastError = ex;
            }
        }

        if (written is null)
        {
            AppLogger.LogError("自检报告写入失败（已尝试全部候选路径）", lastError ?? new IOException("未知原因"));
            Console.WriteLine($"[WARN] 自检报告写入失败：{lastError?.Message}");
            Console.WriteLine("报告未能落盘，以下为完整内容：");
            Console.WriteLine(text);
        }
        else
        {
            AppLogger.LogInfo($"自检报告已写入：{written}");
            Console.WriteLine();
            Console.WriteLine($"报告已写入：{written}");
        }

        return pass ? 0 : 1;
    }
}
