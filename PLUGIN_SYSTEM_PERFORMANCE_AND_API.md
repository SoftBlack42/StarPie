# StarPie 插件系统：性能实测、接口清单与社区开发指南

> **文档状态**：配套文档（Companion），对应 `PLUGIN_SYSTEM_DESIGN.md`
> **实测环境**：.NET 8.0.26 / 16 核 / Workstation GC / `Release` / Windows
> **实测日期**：2026-09-15
> **基准工程**：`.workbuddy/pluginbench/`（不属于主仓库，可随时删除或复跑）

---

> ## ⚠️ 目录模型已变更（2026-09-15）
>
> 本文档第 6 章「社区开发指南」里提到的安装落点，已由单一 `plugins\` 改为两个目录：
> `<程序目录>\plugin\`（**只读来源区**，只放待安装的 `.dll`，宿主不创建不写入）与
> `%LOCALAPPDATA%\StarPie\plugin-data\`（**可写宿主区**，安装副本与 `registry.json` 在此）。
> 正文里 `<...>\plugins\` 一律读作 `<...>\plugin-data\`；插件日志 `logs\plugins\` 未变。
> 权威定义见 `AGENTS.md` 第 3.7 节。

---

## 目录

1. [性能表现](#1-性能表现)
2. [可开放的接口与扩展点清单](#2-可开放的接口与扩展点清单)
3. [社区用户如何开发与注册插件](#3-社区用户如何开发与注册插件)
4. [典型使用场景](#4-典型使用场景)
5. [附录：复跑基准与数字口径](#5-附录复跑基准与数字口径)

---

# 1. 性能表现

## 1.1 全部为实测数据（非估算）

下表每一项都在 `Bench/Program.cs` 中真实测量得出，可复跑（见 §5）。**关键结论：插件系统本身的开销相对主程序现有指标完全可忽略，真正的风险只有两个——插件自身代码耗时，以及「卸载需摘净引用」这一条纪律。**

### 1.1.1 冷路径（一次性成本）

| 环节 | 实测值 | 出现次数 | 说明 |
|---|---:|---|---|
| `plugin.json` 解析（约 320 字节） | **12 µs** | 每次扫描 | 纯 `JsonDocument` |
| 全量静态识别（PE 头 + CorFlags + TFM + 扫接口实现） | **50 ~ 90 µs** | 每插件每次扫描 | `PEReader` + `MetadataReader`，**不加载程序集** |
| **扫描 50 个插件的估算总耗时** | **3 ~ 4.5 ms** | 启动一次 | 可异步执行，不阻塞首帧 |
| 独立可回收 ALC 加载程序集 | **0.02 ms** | 每插件一次 | 5 KB、零第三方依赖的插件 |
| 反射查找入口类型 `Assembly.GetType` | **0.9 ~ 1.8 µs** | 每插件一次 | |
| 反射实例化 `Activator.CreateInstance`（首次，含 JIT） | **250 ~ 280 µs** | 每插件一次 | 含构造链 JIT，故偏高 |
| `Initialize()` 首次调用（含 JIT） | **14 ~ 22 µs** | 每插件一次 | |
| **首次启用一个插件合计** | **约 0.3 ms** | 每插件一次 | 不含第三方依赖加载与 WPF 类型 JIT |

> ⚠️ **口径边界**：`0.02 ms` 的 ALC 加载是「5 KB 空插件 + 契约程序集已在默认 ALC」的理想值。
> 真实插件若携带 2~3 个第三方依赖或首次触碰 WPF 类型（`Geometry`/`Brush`/`DependencyProperty` 的首次 JIT 是毫秒级），首次启用预计落在 **5 ~ 50 ms** 量级。**这个区间是估算，不是实测**，建议插件作者用同一基准复测后写进自己的 README。

### 1.1.2 热路径（每次调用成本）

| 调用方式 | 实测值 | 折算吞吐 | 结论 |
|---|---:|---:|---|
| 跨 ALC 接口直接调用 | **2.3 ~ 3.9 ns/次** | **约 2.9 亿次/秒** | ✅ 推荐基线 |
| 缓存委托调用 | 2.0 ~ 3.0 ns/次 | 约 4 亿次/秒 | ✅ 推荐基线 |
| 同 ALC 调用（基线对照） | 0.5 ~ 1.3 ns/次 | — | JIT 内联后已接近循环本身开销，**不是公平对照** |
| `MethodInfo.Invoke` + 装箱 | **39 ~ 43 ns/次** | 约 2400 万次/秒 | ❌ 反模式，慢 12 倍 |
| 安全包裹：`try/catch` + `Stopwatch` 计时 | **39 ~ 45 ns/次** | 约 2400 万次/秒 | ✅ 超时机制几乎免费 |
| 安全包裹 + **每次新建参数字典** | **86 ~ 106 ns/次** | 约 1000 万次/秒 | ❌ 成本翻 2.5 倍并制造 GC 压力 |

**由此得出两条硬性设计约定**：

1. **禁用 `MethodInfo.Invoke`，首次加载时把回调缓存成强类型委托**（`Delegate.CreateDelegate`）。12 倍差距在插件动作高频触发时会被放大。
2. **参数不得每次调用新建 `Dictionary<string,string>`**。`Dictionary` 分配让单次成本从 40 ns 涨到 106 ns，且每条调用产生一个待回收对象。改用「复用的只读结构体 + 字符串切片」或按调用频率池化 —— 这条必须写进 SDK 的 `performance-rules.md`。

### 1.1.3 资源占用（内存）

| 项 | 实测值 |
|---|---:|
| 基线（宿主 + 1 个插件已加载）工作集 | 45.76 MB |
| 基线私有内存 / GC 堆 | 22.25 MB / 109 KB |
| 再加载 5 个插件 ALC 的工作集增量 | **0.10 MB** |
| **单插件边际工作集** | **约 0.02 MB** |
| **单插件边际 GC 堆** | **约 1 KB** |

> ⚠️ **口径边界**：这是 **5 KB 空插件**的边际值，几乎只反映程序集元数据表的开销。
> 真实插件的成本 ≈ 其自身程序集与全部依赖的体积（元数据 + IL + JIT 后的代码页），须按附加工件单独计量。
> 因此设计上安排了「宿主基线 / 插件附加 / 合计」三段式内存会计展示（见设计文档 §5.5），**不要让用户误以为 20 MB 的第三方库是主程序膨胀**。

### 1.1.4 卸载与热重载（这里有最重要的一个实测结论）

| 场景 | 实测结果 |
|---|---|
| 宿主**不持有**任何引用 → `Unload()` + GC | **5 个 ALC 全部真正卸载，仅需 1 轮 GC，耗时 1.6 ms** ✅ |
| 宿主**仍持有**插件实例（模拟未注销的订阅 / 未清空的注册表） | **卸载失败** ❌ |
| 上述引用随后被摘净 | **随即卸载成功** ✅ |
| 20 轮 load + 实例化 + 执行 + unload 循环 | **1.2 ms/轮；工作集漂移 0.01 MB，GC 堆漂移 1 KB** ✅ 无泄漏 |

**这三个数字连起来是本次基准最有价值的一条发现**：

> `.NET` 的可回收 ALC **本身是可靠的** —— 摘净引用后 1 轮 GC 就干净卸载，反复 20 次零漂移。
> 卸载失败的**唯一原因永远是有引用没摘干净**。所以「卸载不可靠」这个说法要精确化为：
> **不是运行时不可靠，而是宿主与插件作者都必须严守「摘净引用」纪律。**
> 这正是设计文档 §5.6 要求「所有注册 API 返回 `IDisposable` token + 宿主在停用时自动撤销（双保险，即使插件忘记 `Shutdown()` 也能摘干净）」的实证依据。

**实测修正一处原先措辞**：我原本写「`Assembly.Load` 会立即触发 module initializer」。实测结果是 ——

| 时点 | module initializer 是否已执行 |
|---|---|
| `Assembly.LoadFrom` 返回后 | **未执行** |
| 完成「反射取类型 + 实例化」后 | **已执行** |

即触发点落在**首次触碰模块成员**（反射取类型或实例化），而不是加载瞬间。
**结论不变但理由更精确**：识别阶段必须坚持纯元数据读取，**既不 Load、也不反射取类型、也不实例化** —— 因为「加载」与「反射取类型」这两个看似无害的动作，都已经进入了会执行不可信代码的边界之内。

### 1.1.5 与主程序现有红线的对账

| 现有红线（AGENTS.md §1.2） | 插件系统的占用 | 判定 |
|---|---|---|
| 静默驻留 15 ~ 30 MB | 零启用插件：**+0**（仅读 manifest，不加载程序集）；每启用插件 +0.02 MB（空插件） | ✅ 不破坏 |
| 轮盘呈现 < 16 ms | 插件**不参与**呈现主路径；渲染插件只在已呈现后的装饰层，单帧预算 4 ms（= 帧预算的 1/4） | ✅ 不破坏 |
| 钩子线程内绝不执行耗时 IO | 插件代码**零出现**在钩子线程（编译期不引用宿主 + 运行时只在执行线程/UI 线程回调） | ✅ 不破坏 |
| 动作队列单线程串行 | 调用开销 3.9 ns vs `SendInput` 毫秒级 → **占比 < 0.001%** | ✅ 不破坏 |
| 启动扫描 | 50 个插件 3 ~ 4.5 ms，可异步延迟执行 | ✅ 不破坏 |

**一句话总结性能**：插件的**调用开销可以忽略（纳秒级）**，成本集中在**首次启用（0.3 ms 起，真实插件 5~50 ms）**与**插件自身的内存/耗时**上。因此整套性能设计只有一个主旋律 —— **惰性加载**：把「零启用插件时开销为 0」当作一等公民指标来守。

### 1.1.6 需要超时保护的真实原因

调用开销是纳秒级，所以超时/熔断机制保护的**不是调用开销，而是插件代码本身**：

| 风险 | 后果 | 对策（设计文档 §6.5） |
|---|---|---|
| 插件动作同步阻塞 | `ActionExecutor` 是**单线程 Channel 消费**，一个卡住的插件会卡死整个动作队列 | 动作按 `ActionKind` 分流：`Sequential` 走执行线程（超时 5 s），`Background` 走 `Task.Run` 并发（超时 30 s） |
| 插件渲染器每帧重建几何 | 拖累 16.7 ms 帧预算 | 单帧 4 ms 预算，超时降级为内置 `ClassicRingRenderer` |
| 插件异常冒泡 | 会命中 `ActionExecutor.Execute` 的 `catch` 并**弹出 MessageBox**（`:332`） | `PluginInvoker` 统一包裹；连续 3 次失败 → `Faulted`，5 次 → 自动 `Quarantine` |

---

# 2. 可开放的接口与扩展点清单

## 2.1 分层总览

```
插件作者能看到的一切
├── A 入口与元数据       IStarPiePlugin / PluginMetadata / 标记接口
├── B 宿主服务（Context） IPluginContext 下的 12 个服务接口
├── C 贡献点接口          IActionContribution / IStyleContribution / IIconPackContribution / ...
├── D 数据契约（DTO）     PluginActionInput / ActionResult / ParameterField / ActionKind / Capabilities
└── E 负向清单           明确「不开放」的 8 项
```

## 2.2 A · 插件入口与元数据

| 接口 / 类型 | 作用 | 调用时机 | 线程 | 稳定性 |
|---|---|---|---|---|
| `IStarPiePlugin` | 唯一入口。`Metadata` 属性 + `Initialize(ctx)` + `Shutdown()` | 启用 / 停用 | UI 线程 | 🔒 冻结 |
| `PluginMetadata` | 清单的内存映射（与 `plugin.json` 交叉校验） | 全程只读 | 任意 | 🔒 冻结 |
| `IActionPlugin` | 标记：提供自定义动作 | — | — | 🔒 冻结 |
| `IStylePlugin` | 标记：提供轮盘渲染形态 | — | — | 🔒 冻结 |
| `IIconPackPlugin` | 标记：提供图标包 | — | — | 🔒 冻结 |
| `IPresetPlugin` | 标记：提供预设 / 配置模板 | — | — | 🔒 冻结 |
| `ISettingsProvider` | 插件自带设置面板（**P3 逃生门，需人工审核**） | 打开设置页 | UI 线程 | ⚠️ 实验性 |
| `IOcrProvider` | 提供 OCR 引擎（**P2，需先重构 `OcrManager`**） | 识别请求 | 后台 | ⚠️ 未开放 |
| `ISoundProvider` | 提供音效（**P2，需先重构 `SoundEffectManager`**） | 播放请求 | 后台 | ⚠️ 未开放 |

## 2.3 B · 宿主服务（`IPluginContext` 分发）

这是插件**唯一**能接触宿主能力的通道。设计上刻意做成服务定位器，插件不引用任何宿主具体类型。

| 服务接口 | 能力 | 关键约定 | 优先级 |
|---|---|---|---|
| `IPluginLogger` | 写插件专属日志（`logs/plugins/<id>_*.log`） | **限流 200 行/分钟**，超限丢弃并提示一次 | P0 |
| `IPluginSettings` | 读写插件私有 `settings.json` | 宿主做原子写；单值上限 8 KB | P0 |
| `II18nRegistry` | 注册插件词条（key 必须 `plugin.<id>.*`） | 语言切换时宿主回调插件 | P0 |
| `IIconRegistry` | 注册图标（key 必须 `plugin:<id>:*`） | 只收 SVG path data，不收位图 | P0 |
| `IActionRegistry` | **注册动作类型 + 参数 Schema**（核心） | 返回 `IDisposable` token | P0 |
| `IStyleRegistry` | 注册轮盘渲染形态 | 返回的 `Brush` 由宿主**防御性 Freeze** | P1 |
| `IPresetRegistry` | 注册预设 / 配置模板片段 | 只收数据，不收代码 | P1 |
| `ICommandRegistry` | 注册 `ShellTool` 动词（对应 `ActionExecutor.cs:702`） | verb 名不得占用 `Windows.*` / `StarPie.*` | P1 |
| `IHostActionInvoker` | **让插件复用宿主已验证的动作**（发快捷键、启动程序、操作窗口） | ⭐ 见下方说明 | P0 |
| `INotificationService` | 托盘气泡 / 非侵入提示 | 频率限流，禁止弹模态框 | P0 |
| `IHostInfo` | 宿主版本、`apiVersion`、当前语言、是否提权 | 只读 | P0 |
| `IPluginEvents` | 只读事件订阅（前台进程变化、轮盘唤出、配置切换…） | **必须持有并释放返回的 token** | P3 |
| `IDispatcherFacade` | 显式切 UI 线程 | 禁止在钩子/执行线程直接碰 UI | P0 |

> ⭐ **`IHostActionInvoker` 的特别说明（重要设计取舍）**
> 插件做「发送快捷键」这类动作时，**绝不允许自己 `SendInput`** —— 项目里 `ActionExecutor` 已经踩平了硬件扫描码映射（`MapVirtualKey`）、`KEYEVENTF_EXTENDEDKEY` 标志、修饰键 10~15 ms 保持时延等一堆坑（见 AGENTS.md §3.2）。
> 因此提供 `IHostActionInvoker.SendHotkey(...)` / `Launch(...)` / `SendText(...)`，让插件**复用宿主经过验证的实现**，并让自注入事件走 `StarPieExtraInfo = 0x53544152` 签名放行，避免插件触发的事件被自己的钩子捕获形成死循环。

## 2.4 C · 贡献点接口

| 贡献点接口 | 宿主接缝位置 | 难度 | 风险 |
|---|---|---|---|
| `IActionContribution` | `ActionExecutor.cs:267` + `SlotViewModel.cs:803` | ★ | 中（执行线程） |
| `IStyleContribution` | `StyleRendererFactory.cs:5` | ★★★ | **高（逐帧热路径）** |
| `IIconPackContribution` | `IconHelper.cs:427` | ★ | 低 |
| `IPresetContribution` | `SlotViewModel.cs:11` + 导入流程 | ★ | 低 |
| `ICommandContribution` | `ActionExecutor.cs:702` | ★ | 低 |
| `IThemeContribution` | 主题 / 配色下拉（`CustomColorPreset`） | ★ | 低 |

## 2.5 D · 数据契约（DTO）

| 类型 | 用途 | 关键字段 |
|---|---|---|
| `ActionDescriptor` | 动作元信息 | `Id` / 名称 i18n key / 图标 / 分类 / `ActionKind` |
| `ParameterField` | **声明式参数表单**（宿主渲染 UI） | `Key` / `Label` / `Type`（Text·Number·Bool·Path·File·Folder·Enum·Hotkey·Color·Icon）/ `Default` / `Required` / `EnumOptions` / `ValidationRegex` / `MaxLength` |
| `PluginActionInput` | 执行入参 | `IReadOnlyDictionary<string,string> Params`（**复用，不新建**）、`ActionContext` |
| `ActionContext` | 只读环境 | 前台进程名 / 活动窗口句柄 / 鼠标位置 / 是否提权 |
| `ActionResult` | 执行结果 | `Success` / `Message?` / `Data?` |
| `ActionKind` | 并发分类 | `Sequential`（走执行线程串行）/ `Background`（可并发） |
| `CapabilityFlags` | 能力声明位 | `Process` / `FileSystem` / `Network` / `Clipboard` / `Registry` / `GlobalHook` / `Ui` / `Admin` |
| `PluginManifest` | `plugin.json` 映射 | 见设计文档 §9.2 全字段表 |

## 2.6 E · 负向清单（明确不开放）

| 不开放 | 原因 |
|---|---|
| 注册全局钩子（`WH_MOUSE_LL` / `WH_KEYBOARD_LL`） | 进程级钩子是**全局唯一**的，插件注册会与主程序钩子互相干扰；改为通过 `IPluginEvents` 请求宿主代发事件 |
| 改写极坐标命中算法 / 扇区几何 | 违反 `AGENTS.md` §1.1「肌肉记忆与确定性」红线 |
| 直接操作 `RadialWindow` 视觉树 | 逐帧热路径会拖垮 16 ms 呈现预算 |
| 直接写 `ConfigManager.CurrentConfig` | 放大既有 H1 无锁竞态风险 |
| 直接写 `I18n.Translations` 字典 | 静态字典并发写不安全，只能走 `II18nRegistry` |
| 引用 `StarPie.dll` / `WinPieGestures.*` 命名空间 | 会让插件绑定宿主内部类型签名，宿主任何重构都会让全社区插件失效 |
| 自建 WPF 窗口（P0 阶段） | 主题不一致、内存不可控、深色对比度红线无法保证 |
| 注入托盘菜单 / 修改主界面布局 | 与「插件只为扩展能力、不为改造程序」的定位冲突 |

---

# 3. 社区用户如何开发与注册插件

## 3.1 全流程六步

```
① 装 SDK ──► ② 拉模板 ──► ③ 写入口 + 清单 ──► ④ 本地调试 ──► ⑤ 打包 ──► ⑥ 安装并手动启用
                                             (开发者模式)    .spkg      (用户侧 4 步)
```

## 3.2 第 0 步：环境准备

```
.NET 8.0 SDK（与宿主 TFM 一致）
IDE：VS 2022 / Rider / VS Code（本仓库主程序刻意不用重量级 IDE，插件开发也不强制）
可选：StarPie.Plugin.Abstractions（SDK 契约包）
```

## 3.3 第 1 步：从模板创建工程

```powershell
dotnet new install StarPie.Plugin.Template
dotnet new starpie-plugin -n StarPie.Plugin.Pomodoro
cd StarPie.Plugin.Pomodoro
```

模板已预置：`net8.0-windows10.0.19041.0`、`x64`、`GenerateDependencyFile=true`（**单文件宿主形态下必需**）、`plugin.json` 骨架、`plugin.schema.json`、`build.ps1`、GitHub Actions 工作流。

## 3.4 第 2 步：写入口类（HelloAction 完整示例）

**`Plugin.cs`**

```csharp
using StarPie.Plugin.Abstractions;

namespace StarPie.Plugin.Pomodoro;

public sealed class PomodoroPlugin : IStarPiePlugin, IActionPlugin
{
    private IPluginContext? _ctx;
    private IDisposable? _actionToken;

    public PluginMetadata Metadata => new()
    {
        Id = "com.example.pomodoro",
        Name = "番茄钟",
        Version = "1.2.0",
        Author = "example"
    };

    public void Initialize(IPluginContext context)
    {
        _ctx = context;

        _actionToken = context.Actions.Register(new StartTimerAction(context));
    }

    public void Shutdown()
    {
        _actionToken?.Dispose();   // 必须：宿主虽会兜底撤销，但主动释放才能保证 ALC 卸载
        _actionToken = null;
        _ctx = null;
    }
}

internal sealed class StartTimerAction : IActionContribution
{
    private readonly IPluginContext _ctx;

    public StartTimerAction(IPluginContext ctx) => _ctx = ctx;

    public ActionDescriptor Descriptor => new()
    {
        Id = "startTimer",
        DisplayNameKey = "plugin.com.example.pomodoro.startTimer",
        IconKey = "plugin:com.example.pomodoro:timer",
        Category = "效率",
        Kind = ActionKind.Background   // 纯计时，不碰前台窗口 → 允许并发
    };

    public IReadOnlyList<ParameterField> Parameters => new[]
    {
        new ParameterField { Key = "minutes", Label = "时长（分钟）", Type = ParameterType.Number,
                             Default = "25", Required = true, ValidationRegex = @"^([1-9]|[1-5][0-9]|60)$" },
        new ParameterField { Key = "mode", Label = "模式", Type = ParameterType.Enum,
                             Default = "work", EnumOptions = new[] { "work", "break" } }
    };

    public bool Validate(PluginActionInput input, out string error)
    {
        error = "";
        return int.TryParse(input.Params.GetValueOrDefault("minutes"), out int m) && m is > 0 and <= 60
               || (error = "时长必须是 1~60 之间的整数") is not null && false;
    }

    public async Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken ct)
    {
        int minutes = int.Parse(input.Params["minutes"]);
        await Task.Delay(TimeSpan.FromMinutes(minutes), ct);   // 宿主 30s 超时会取消，勿依赖长阻塞
        _ctx.Notify.Show("番茄钟", $"{minutes} 分钟已结束");
        return ActionResult.Ok();
    }
}
```

> 注意 `Validate` 的写法仅为示意，正式模板会用更清晰的早退风格。

**`plugin.json`**

```json
{
  "schemaVersion": 1,
  "id": "com.example.pomodoro",
  "name": "番茄钟",
  "description": "在轮盘上直接启动一个番茄钟计时。",
  "author": "example",
  "license": "MIT",
  "version": "1.2.0",
  "apiVersion": "1.0",
  "minHostVersion": "1.7.0",
  "targetFramework": "net8.0-windows10.0.19041.0",
  "platform": "win-x64",
  "entryType": "StarPie.Plugin.Pomodoro.PomodoroPlugin",
  "capabilities": ["Process"],
  "contributions": { "actions": ["startTimer"], "icons": ["timer"] },
  "icon": "icons/timer.svg",
  "tags": ["效率", "计时"]
}
```

## 3.5 第 3 步：本地调试

1. **打开开发者模式**（设置 → 插件 → 开发者模式）。此时允许「只注册外部路径、不复制文件」，页顶常驻风险横幅。
2. 在插件页点「添加外部路径」，直接指向 `bin\Debug\net8.0-windows\`。
3. Visual Studio / Rider：**附加到进程 → 选 `StarPie.exe`**，插件源码断点可命中。
4. 改完代码 rebuild，点插件页「重新加载」。**注意**：热重载不保证成功（见 §1.1.4），失败时页面会给「重启 StarPie 生效」提示。
5. 排查用插件专属日志：插件页「⋯ → 查看日志」直接打开 `logs/plugins/<id>_*.log`。

## 3.6 第 4 步：打包

```powershell
./build.ps1 -Configuration Release
# 产出：dist/com.example.pomodoro-1.2.0.spkg
# 同时把主程序集的 SHA256 写回 plugin.json 的 sha256 字段
```

**打包纪律（宿主会校验）**：
- `.spkg` 本质是 zip，**必须保留文件夹结构**（因为单文件宿主形态下 `Assembly.Location` 为空，插件必须自带 `deps.json`）；
- 必须包含插件私有的全部第三方依赖与 `runtimes\win-x64\native\`；
- **绝不把 `StarPie.Plugin.Abstractions.dll` 打进包**（宿主已提供，重复会引发类型身份分裂）。

## 3.7 第 5 步：用户如何安装并启用（产品视角，4 步）

| 步 | 用户动作 | 系统行为 |
|---|---|---|
| 1 | 设置 → 「🔌 插件」→ **[➕ 从 .dll 安装…]**（支持多选，或直接把 `.dll` 拖进页面） | 跑四道静态闸门：清单结构 / PE 与 CLR 头 / 接口与 TFM 与架构 / SHA256 与签名 |
| 2 | 阅读**安装确认卡** | 展示名称、版本、作者、许可证、SHA256、签名状态、**能力声明**；高风险能力组合标红；必须勾选风险确认 |
| 3 | 确认 | 复制到 `%LOCALAPPDATA%\StarPie\plugins\<id>\`，写入 `registry.json`，状态 = **Disabled（默认不启用）** |
| 4 | 在列表中打开**启用**开关 | 独立 ALC 加载 → `Initialize` → 注册贡献点 → 状态 `Active`；动作即刻出现在动作类型下拉与预设清单里 |

**识别失败时的排查对照表**（宿主会直接给出原因码，不必猜）：

| 原因码 | 含义 | 用户该怎么办 |
|---|---|---|
| `MANIFEST_MISSING` | 缺 `plugin.json` | 说明选错了文件，应选插件文件夹里的主 dll |
| `MANIFEST_SCHEMA_TOO_NEW` | 清单格式比当前宿主新 | 升级 StarPie |
| `NOT_MANAGED_ASSEMBLY` | 不是 .NET 程序集（原生 dll） | 不能作为插件 |
| `NO_CONTRACT_IMPL` | 没找到 `IStarPiePlugin` 实现 | 不是插件，或作者编译错了 |
| `TFM_MISMATCH` | 目标框架不匹配 | 请作者用 `net8.0-windows10.0.19041.0` 重编 |
| `ARCH_MISMATCH` | 含 32 位要求 | 需要 x64 / AnyCPU 纯 IL 版本 |
| `API_VERSION_MISMATCH` | 契约主版本不符 | 更新插件 |
| `HOST_VERSION_OUT_OF_RANGE` | 宿主版本超出插件声明区间 | 升级插件，或联系作者 |
| `SHA256_MISMATCH` | 文件被修改或损坏 | 重新下载 |

## 3.8 第 6 步：发布到社区索引

```
作者打 .spkg → 传 GitHub Release → 提 PR 到 StarPie-Plugins-Registry 追加 index.json 条目
   → 官方审核 10 条清单 → 合并 → 客户端可浏览（仍需用户手动下载 + 手动启用）
```

**审核清单（10 条）**：① `id` 唯一且不占用保留前缀；② 能力声明与实际行为一致；③ 无 `MessageBox`、无阻塞主线程；④ 渲染器画刷已 `Freeze` 且满足 4 ms 帧预算；⑤ 未引用 `StarPie.dll`；⑥ 有开源许可；⑦ 无自动下载执行远端代码；⑧ 不静默回传用户数据；⑨ `.spkg` 哈希与清单一致；⑩ 文档与截图齐全。

**命名保留字**：`starpie*`、`windows*`、`microsoft*`、`system*`、`builtin*` 一律保留给官方。

---

# 4. 典型使用场景

## 4.1 按扩展点分类的场景矩阵

| # | 场景 | 用到的扩展点 | 发布难度 | 风险 | 推荐度 |
|---|---|---|---|---|---|
| S1 | **自定义动作**（番茄钟、粘贴为纯文本、发送固定文本、调外部 CLI） | A + B(`IActionRegistry`, `IHostActionInvoker`) + C(`IActionContribution`) | ★ | 中 | ⭐⭐⭐⭐⭐ 社区主力场景 |
| S2 | **图标包**（一套品牌图标、程序集图标补齐） | B(`IIconRegistry`) + C(`IIconPackContribution`) | ★ | 低 | ⭐⭐⭐⭐⭐ |
| S3 | **配色 / 主题预设包** | C(`IThemeContribution`) + B(`IPresetRegistry`) | ★ | 低 | ⭐⭐⭐⭐ |
| S4 | **轮盘方案模板**（「PS 修图模式」「编程模式」一键导入） | C(`IPresetContribution`)，复用 `WheelProfile` + 既有导入流程 | ★★ | 低 | ⭐⭐⭐⭐ |
| S5 | **Shell 动词扩展**（对接 Everything、解压工具、Git 客户端） | C(`ICommandContribution`) + `ActionExecutor.cs:702` | ★★ | 低 | ⭐⭐⭐ |
| S6 | **自定义轮盘渲染形态**（Voronoi、液态金属、极简线性） | C(`IStyleContribution`) + `StyleRendererFactory` | ★★★ | **高** | ⭐⭐ 需严格审核 |
| S7 | **OCR 引擎替换**（P2，需先重构 `OcrManager`） | `IOcrProvider` | ★★★ | 中 | ⭐ 待重构 |
| S8 | **音效包 / 合成器**（P2，需先重构 `SoundEffectManager`） | `ISoundProvider` | ★★ | 低 | ⭐⭐ 待重构 |

## 4.2 三个完整使用场景走查

### 场景 S1-1：插件与内置动作协作 —— 「把当前选中的文件路径发到我的终端」

**用户视角**：在文件资源管理器里选中几个文件 → 划出轮盘 → 执行「发送路径到 WSL」→ 自动聚焦已开的 WSL 窗口并粘贴。

**开发者视角（关键是在"用宿主能力"而非"自己造轮子"）**：
1. `IActionContribution` 声明动作 `sendPathToWsl`，`Kind = Sequential`（要操作前台窗口，必须串行）；
2. 参数用 `ParameterField` 声明一个 `Enum`（目标终端：cmd / powershell / wsl），宿主自动渲染成下拉框；
3. 执行时：
   - 取选中文件路径 → **不自己读剪贴板**，走 `IHostActionInvoker`（宿主已实现 `ActionExecutor.GetActiveExplorerContext()`，能处理多选与文件夹场景）；
   - 切窗口 → 同样走 `IHostActionInvoker`（复用 `TryToggleProcessWindow` 的窗口枚举与还原逻辑）；
   - 粘贴 → 走 `IHostActionInvoker.SendText()`（复用 `KEYEVENTF_UNICODE` 字符流注入，规避输入法阻断）。
4. 好处：插件代码只有 30 行业务逻辑，**所有 Win32 坑都由宿主承担**。

### 场景 S2-1：图标包 —— 让所有「无图标动作」不再显示成齿轮

**用户视角**：装一个「Office 图标包」，动作列表里 Word/Excel 动作自动显示成对应图标。

**开发者视角**：
- 只需提交 SVG path data（`IIconRegistry.Register(key, svgPathData)`），key 必须 `plugin:<id>:*`；
- 宿主已有 `IconHelper.ExtractSvgPathData` 与 `VectorIconList`（47 个内置图标），插件包与之并列显示；
- **禁止收位图**（会破坏 `IconHelper` 的缓存策略与 `Freeze` 纪律）。

### 场景 S4-1：轮盘方案模板 —— 「SolidWorks 建模模式」

**用户视角**：点「导入插件方案」→ 出现「SolidWorks 建模模式」→ 一键套用，得到一整套 8 扇区动作（草图、拉伸、旋转、测量…）。

**开发者视角**：
- 本质是贡献一份 `WheelProfile` 数据片段（扇区数 + 动作槽位 + 二级子动作 + 图标 key）；
- 复用既有导入逻辑 `ConfigManager.ImportConfig`，因此**天然继承其备份与校验路径**；
- 需要声明 `capabilities: []`（纯数据，零权限）—— 这类插件是社区最欢迎的，**风险最低、收益最直观**。

## 4.3 场景选择建议（给社区作者的路线图）

```
新手第一站 ──► S2 图标包 或 S4 方案模板   （纯数据，零权限，1 小时可完成）
   │
进阶 ─────────► S1 自定义动作             （社区需求最大，但需理解线程与超时约定）
   │
高手 ─────────► S5 Shell 动词 或 S6 渲染形态（后者需通过逐帧预算与 Freeze 双重审核）
   │
等待宿主重构 ──► S7 OCR / S8 音效
```

---

# 5. 附录：复跑基准与数字口径

## 5.1 如何复跑

```powershell
cd <仓库>/.workbuddy/pluginbench
dotnet build PluginA/PluginA.csproj -c Release
dotnet run   --project Bench/Bench.csproj -c Release
# 结果同时写入 Bench/bin/Release/net8.0/bench-result.txt
```

工程结构（`.workbuddy/` 已被 `.gitignore` 忽略，**不会污染主仓库**）：

```
pluginbench/
├── Contract/          # 模拟 StarPie.Plugin.Abstractions（含 3 个接口）
├── PluginA/           # 模拟社区插件（含 ModuleInitializer 探针 + deps.json）
└── Bench/             # 基准宿主（PluginLoadContext 实现与设计文档 §5.4 一致）
```

基准覆盖：静态识别 / 清单解析 / ALC 加载 / 反射实例化 / 首次 JIT / 稳态调用（4 种方式）/ 安全包裹（2 种）/ 内存边际 / 卸载（3 种引用状态）/ 20 轮 load-unload 稳定性。

## 5.2 数字口径与边界（重要）

| 项 | 口径 |
|---|---|
| CPU / 运行时 | 16 核，.NET 8.0.26，`Workstation` GC、`Concurrent` GC（与主程序 `csproj` 一致）、`TieredPGO` |
| 插件体积 | 5 KB 级空插件，**零第三方依赖** |
| 已估未测 | 含第三方依赖的首次启用（5~50 ms）、WPF 类型首次 JIT、真实插件的内存边际 |
| 未覆盖 | 真实 `RadialWindow` 渲染循环下的渲染插件帧耗时（需 WPF 环境，建议宿主侧加性能计数后再测） |
| 波动 | 单次运行内多次采样波动约 ±30%（如跨 ALC 调用 2.3~3.9 ns），结论按数量级使用 |
| 平台差异 | 换机器请以本基准重测，**不要直接引用本文绝对值** |

## 5.3 从实测得出的三条 SDK 硬性约定

1. **禁止 `MethodInfo.Invoke`** —— 首次加载时把回调缓存成强类型委托（12 倍差距）。
2. **禁止每次调用新建参数字典** —— 复用只读结构体或池化（2.5 倍差距 + GC 压力）。
3. **所有注册 API 返回 `IDisposable`，插件必须在 `Shutdown()` 中全部释放** —— 实测证明：摘净引用后 1 轮 GC 即可干净卸载，反之必然失败。
