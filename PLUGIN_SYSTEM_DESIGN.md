# StarPie 插件系统设计大纲

> **文档状态**：设计草案（Draft v1.0），**仅含设计与契约约定，不含实现代码**
> **对应程序版本**：`1.7.4-beta.2`（`WinPieGestures.csproj`）
> **编制日期**：2026-09-15
> **配套文档**：`PLUGIN_SYSTEM_PERFORMANCE_AND_API.md`（性能实测数据 · 完整接口清单 · 社区开发指南 · 使用场景）
> **目标**：以「用户手动选择启用 `.dll`」为唯一入口，建立可供社区共创的插件体系，且不破坏既有的轻量、低延迟、确定性三大红线。

---

> ## ⚠️ 目录模型已变更（2026-09-15），正文路径请勿照抄
>
> 本文档是**设计期草案**，正文采用的是当时的目录模型 —— 单一插件根目录 `plugins\`。
> 实现已改为**两个目录职责分离**：
>
> | 目录 | 角色 |
> |---|---|
> | `<程序目录>\plugin\` | **只读来源区**：随包分发的待安装候选，扁平且只放 `.dll`。宿主**不创建、不写入、不删除** |
> | `%LOCALAPPDATA%\StarPie\plugin-data\` | **可写宿主区**：安装副本、`registry.json`、`health.json`、插件私有 `data\`。便携模式只改变这里的落点 |
>
> 于是：
> 1. 下文所有 `<...>\plugins\` 一律读作 `<...>\plugin-data\`（唯一例外是插件日志 `logs\plugins\`，它没变）；
> 2. 新增了「只读来源区 → 候选列表 → 用户点安装」这条流程：来源区里的 `.dll` 不登记、不加载、不出现在插件列表；
> 3. 安装时的复制策略改为按清单来源分叉（有 `plugin.json` 整目录复制，裸 DLL 只复制那一枚）。
>
> 权威定义见 `AGENTS.md` 第 3.7 节与 `WinPieGestures/Plugin/PluginPaths.cs`。
> **保留正文原样是为了留住设计推理过程，不要照着正文里的路径写新代码。**

---

## 目录

1. [项目现状分析（插件系统的边界条件）](#1-项目现状分析插件系统的边界条件)
2. [设计目标与非目标](#2-设计目标与非目标)
3. [总体架构与关键决策](#3-总体架构与关键决策)
4. [插件接口与扩展点设计](#4-插件接口与扩展点设计)
5. [加载、识别、启用与手动选择机制](#5-加载识别启用与手动选择机制)
6. [生命周期、隔离与安全](#6-生命周期隔离与安全)
7. [版本与依赖兼容性](#7-版本与依赖兼容性)
8. [通信与数据约定](#8-通信与数据约定)
9. [目录结构与配置项设计](#9-目录结构与配置项设计)
10. [面向社区共创的规范、示例与文档](#10-面向社区共创的规范示例与文档)
11. [对现有代码的改造点清单](#11-对现有代码的改造点清单)
12. [分期路线图](#12-分期路线图)
13. [风险、限制与待决策问题](#13-风险限制与待决策问题)

---

## 1. 项目现状分析（插件系统的边界条件）

### 1.1 技术底座

| 维度 | 现状 | 对插件系统的含义 |
|---|---|---|
| 技术栈 | .NET 8 WPF，`net8.0-windows10.0.19041.0`，`AssemblyName=StarPie` | 插件必须落到同一 TFM；插件 DLL 只能是 .NET 程序集，不能是 C++/WinRT 原生形态（除非走 P/Invoke） |
| 依赖 | **零 NuGet 依赖**，纯 BCL + P/Invoke | 插件宿主**不得**引入 `McMaster.NETCore.Plugins` 等第三方加载库；识别与加载需基于 BCL 自建 |
| 架构 | x64 only，WPF + WinForms 混合 | 插件必须 `x64`；任何 `AnyCPU`/`x86` 程序集直接拒绝 |
| 分发形态 | **双形态**：Lightweight（依赖运行时，~2.5MB）/ Standalone（`PublishSingleFile=true` + `IncludeNativeLibrariesForSelfExtract=true`，~65MB） | **单文件自包含模式下 `Assembly.Location` 为空、磁盘无 `StarPie.deps.json`**，插件加载器必须同时兼容两种宿主形态（详见 §5.4） |
| 配置 | `%LOCALAPPDATA%\StarPie\config.json`（JSON，97+ 顶层键） | 插件状态**不应**继续塞进主配置（见 §9.3 的理由） |
| 日志 | `%LOCALAPPDATA%\StarPie\logs\starpie_yyyy-MM-dd.log`，7 天轮转，异步队列写盘 | 需扩展为「按插件分流 + 限流」 |
| 权限 | manifest 为 `asInvoker`，但 `App.xaml.cs:337` 支持 `Verb="runas"` 提权重启；`TrayController` 处理 UIPI 放行 | **插件可能运行在管理员权限下**，这是安全设计的第一前提 |
| CI | `.github/workflows/build-and-test.yml` 仅做 build + publish Standalone，**不跑测试** | 插件 SDK 变更缺少自动化回归保护，需补 CI |
| 测试 | `tests/` 为 Python + pywinauto GUI E2E，覆盖停在 v1.4.3 | 插件加载契约需要新增独立的单元级测试（不依赖 GUI） |

### 1.2 既有可复用的扩展点（这是好消息）

项目已经天然存在一批**隐式扩展点**，插件系统本质上是把这些散落的 `switch`/静态列表「提公因式」成注册表：

| # | 既有扩展点 | 位置 | 形态 | 是否现成接口 |
|---|---|---|---|---|
| E1 | 动作类型分派 | `ActionExecutor.Execute()` `ActionExecutor.cs:267` 的 `switch (action.Type)` | 15 种内置类型 | ✗ 硬编码 switch |
| E2 | 动作类型下拉目录 | `SlotViewModel.AggregatedActionTypes` `SlotViewModel.cs:803`、`LocalizedActionTypes` `:816` | 每次调用 `new List<>` | ✗ 静态硬编码 |
| E3 | 系统预设定制清单 | `SlotViewModel.SystemPresetList` `SlotViewModel.cs:11` | `List<SystemPresetItem>` | ✗ 静态只读 |
| E4 | Shell 动词目录 | `ActionExecutor.ExecuteShellTool(verb)` `ActionExecutor.cs:702` | 30+ `Windows.*`/`7-Zip.*` verb | ✗ 硬编码 switch |
| E5 | **轮盘渲染形态** | `IRadialStyleRenderer` + `StyleRendererFactory.CreateRenderer(style)` `StyleRendererFactory.cs:5` | **已是接口 + 工厂** | ✓ **唯一现成可插拔点** |
| E6 | 图标体系 | `IconHelper.VectorIconList:60`、`GetSvgPathByKey:427`、`GetCustomIconsDirectory:1163`、`CustomIconItem:33` | 内置 SVG 清单 + 自定义图标目录 | △ 半开放（用户导入，非代码扩展） |
| E7 | 音效 | `SoundEffectManager`（`SoundType` 枚举 + `CustomSoundProfile`） | 枚举硬编码 | ✗ |
| E8 | OCR 引擎 | `OcrManager`（`OcrManager.cs:22`，静态类，硬编码 `Windows.Media.Ocr`） | 无抽象 | ✗ 需重构 |
| E9 | 多语言 | `I18n.Translations` 静态字典 + `GetString(key)` `I18n.cs:160`（缺 key 回退返回 key 本身） | key-值字典 | △ 机制友好，但无外部注册入口 |
| E10 | 配置导入/导出 | `ConfigManager.ExportConfig/ImportConfig`、`WheelProfile`、`CustomColorPreset` | 可序列化模型 | ✓ 可作为「配置模板包」贡献物 |
| E11 | 动作持久化模型 | `ActionItem`（`Type/Name/Parameter/Arguments/IconKey/CustomIconSvg/...` + `SubActions` 递归） | 纯 POCO + `Clone()` | ✓ 扩展成本低 |

> **结论**：E5 是唯一可以直接暴露给社区的接口；E1/E2/E4 是最有社区价值的扩展点（自定义动作）；E6/E9/E10 是低风险高收益的「数据型」扩展点。E8 需要先重构。

### 1.3 必须继承的硬性红线（AGENTS.md §1.2）

| 红线 | 原文口径 | 插件系统的对应约束 |
|---|---|---|
| R1 极致轻量 | 静默后台驻留 **15~30MB**（深睡后 10~20MB）；控制台全开 60~110MB | 插件**默认惰性**：启动只读清单文件，不 `Assembly.Load`；仅「已启用且被实际引用」才加载 |
| R2 零延迟 | 按下到轮盘呈现 **<16ms**；钩子回调内绝不执行耗时 IO | **插件代码绝不出现在钩子线程路径上**；插件渲染器有逐帧预算 |
| R3 确定性 | 极坐标命中 100%，杜绝漂移误触 | 插件不得改写核心命中算法；渲染插件只影响视觉，不影响几何判定 |
| R4 禁重量级库 | 纯 .NET 8 WPF + Win32 P/Invoke | SDK 与宿主均**不引入 NuGet 依赖** |
| R5 显式 Freeze | 画刷必须 `Freezable.Freeze()`，禁 Base64 塞 config | 插件返回的画刷由宿主**防御性冻结**；插件私有数据走独立文件，禁入 `config.json` |

### 1.4 现状风险对插件设计的直接影响

引用既有分析报告（`PROJECT_ANALYSIS_v1.6.5.md`）中的条目：

| 风险 | 位置 | 对插件系统的直接约束 |
|---|---|---|
| **H1 配置全局裸读写、跨线程无同步** | `ConfigManager.CurrentConfig` 静态属性，`ConfigManager.cs:18` | 若把插件状态写进 `config.json`，将放大该竞态。**故插件注册表使用独立 `registry.json` + 原子写** |
| **H2 更新链无完整性校验** | `UpdateManager.cs:112/286/347` | 插件引入后，供应链面从「一个主程序包」变成「N 个社区 DLL」，**必须补 SHA256 + 签名 + 权限确认**，否则复用现有更新通道会放大风险 |
| **H3 全局异常处理器无条件吞异常** | `App.xaml.cs` | 插件异常必须**自行包裹兜底**，不能指望全局处理器；且 `ActionExecutor.Execute` 内部 catch 会弹 `MessageBox`（`:332`），插件异常不得走到那里 |
| **M2 画刷几乎全部未 Freeze** | 全项目 | 插件渲染器契约里必须把 Freeze 写成**强制条款**，宿主侧再做一次防御性处理 |
| **P3 `SettingsWindow.xaml.cs` = 10,524 行 God Object** | — | 插件管理界面**不得**继续往这个文件里堆；应做成独立 `PluginManagerWindow` / `UserControl` |
| 单线程动作队列 | `ActionExecutor` 用 `Channel` 单线程消费（`:240`） | **一个阻塞的插件动作会卡死整个动作队列**，必须引入超时 + 并发分类（见 §6.5） |

### 1.5 现状小结

> StarPie 的**核心引擎足够干净**（线程分离、背压合并、代数守卫都是加分项），但**工程外围偏脆**（配置竞态、更新无校验、巨型文件）。
> 因此插件系统的设计原则是：**寄生虫式低侵入**——插件只能挂在「注册表 + 调用点」这一层薄如纸的接缝上，绝不触碰钩子线程、配置写盘、几何判定这三处核心。

---

## 2. 设计目标与非目标

### 2.1 目标

| # | 目标 | 可验收标准 |
|---|---|---|
| G1 | 用户可通过「手动选择 `.dll`」安装并启用插件 | 从文件选择到启用成功 ≤ 3 步；全过程可见插件名/版本/作者/权限/哈希 |
| G2 | 插件可扩展动作类型，且不修改主程序 | 新增一个社区动作无需 PR 主仓库 |
| G3 | 默认零插件时不产生任何性能损耗 | 未安装插件：启动耗时、驻留内存与当前版本持平（±5%） |
| G4 | 插件故障不拖垮主程序 | 插件抛异常/死循环/崩溃 → 主程序存活，插件被熔断并提示 |
| G5 | 社区可低成本上手 | 提供 SDK + 模板 + 3 个可运行示例 + 完整文档；新作者 30 分钟内跑通 Hello Action |
| G6 | 版本演进不锁死 | 主程序跨 minor 升级后，老插件在 `apiVersion` 未变时仍可用 |

### 2.2 非目标（明确不做）

| # | 非目标 | 理由 |
|---|---|---|
| N1 | 不做插件市场/云服务 | 与 PRD「纯本地、不收集不上传」隐私承诺冲突；索引改用 GitHub 仓库 JSON |
| N2 | 不做真正的安全沙箱承诺 | .NET 进程内插件无法安全沙箱化（CAS 已废弃）；如实告知 + 白名单启用 + 能力声明是唯一诚实路径 |
| N3 | 不允许插件自带 XAML 直接注入主界面（首版） | 版本耦合 + 主题不一致 + 内存不可控；改为**声明式 Schema 由宿主渲染**（§4.4） |
| N4 | 不引入第三方插件加载框架 | 违反 R4 零依赖红线 |
| N5 | 首版不做独立进程宿主 | 成本高、IPC 复杂；作为 L2 演进项保留（§6.3） |
| N6 | 不允许插件改写核心交互（触发键、命中算法、轮盘尺寸） | 违反 R3 确定性红线 |
| N7 | 不做插件自动静默安装/自动更新 | 保持「手动选择启用」的产品语义，更新需用户确认 |

---

## 3. 总体架构与关键决策

### 3.1 分层结构

```
┌──────────────────────────────────────────────────────────────┐
│  StarPie (主程序, WinExe, AssemblyName=StarPie)               │
│                                                              │
│  ┌────────────────────────┐   ┌───────────────────────────┐  │
│  │ PluginHost (宿主层)     │   │ 既有核心（零侵入）          │  │
│  │  · Scanner  扫描识别    │   │  MouseHook / KeyboardHook  │  │
│  │  · Registry 启用状态    │   │  GestureController         │  │
│  │  · Loader   ALC 加载    │   │  RadialWindow              │  │
│  │  · Catalog  贡献点注册表 │   │  ConfigManager             │  │
│  │  · Invoker  调用+超时   │   │  ActionExecutor ← 薄接缝    │  │
│  │  · Health   熔断/安全模式│   └───────────────────────────┘  │
│  └───────────┬────────────┘                                  │
│              │ 引用（编译期）                                  │
│  ┌───────────▼────────────────────────────────────────────┐  │
│  │ StarPie.Plugin.Abstractions  (SDK, 只含接口/POCO/DTO)    │  │
│  └───────────▲────────────────────────────────────────────┘  │
└──────────────┼───────────────────────────────────────────────┘
               │ 插件作者引用（唯一允许引用的程序集）
   ┌───────────┴────────────┐   ┌──────────────────────┐
   │ 社区插件 A.dll          │   │ 社区插件 B.dll        │
   │  (net8.0-windows, x64)  │   │  …                   │
   └────────────────────────┘   └──────────────────────┘
```

**三层程序集划分（强约束）**：

| 程序集 | 角色 | 允许被谁引用 |
|---|---|---|
| `StarPie`（主程序） | 宿主实现 | 谁也不许引用它（**禁止插件引用主程序集**，这是防版本死锁的第一条铁律） |
| `StarPie.Plugin.Abstractions` | SDK 契约（接口 / DTO / 枚举 / 属性） | 宿主、插件都引用 |
| 社区插件 `.dll` | 贡献实现 | 仅引用 Abstractions + BCL |

> **反模式警告**：若允许插件引用 `StarPie.dll`，插件将绑定到宿主内部类型签名（如 `AppConfig`、`ActionItem` 的每一个字段），宿主任何重构都会让全社区插件失效。SDK 必须只暴露**面向契约的稳定类型**，与内部实现解耦（内部模型 → SDK DTO 做一层映射）。

### 3.2 关键设计决策速览

| # | 决策 | 选择 | 理由 |
|---|---|---|---|
| D1 | 插件形态 | **进程内 .NET 程序集 + 独立 `AssemblyLoadContext`** | 唯一能满足「手动选 .dll」且性能可接受的形态 |
| D2 | 沙箱强度 | **不做沙箱承诺，改为「白名单启用 + 能力声明 + 哈希/签名可见」** | 诚实；符合手动选择的产品语义 |
| D3 | 作用域隔离 | 每插件独立 ALC | 崩溃域/依赖冲突缓解；**但明确不是安全边界** |
| D4 | 加载时机 | **惰性**：启动只读 manifest，不加载程序集 | 守护内存红线 R1 |
| D5 | 贡献点交付方式 | **声明式数据（Schema/DTO），宿主渲染 UI** | 规避 WPF 版本耦合、主题不一致、内存失控 |
| D6 | 插件配置存储 | 插件私有目录独立文件，**不进 `config.json`** | 规避 H1 竞态与配置膨胀 |
| D7 | 调用隔离 | 超时 + 并发分类 + 连续失败熔断 + 安全模式 | 规避单线程动作队列被拖死 |
| D8 | 识别方式 | **`PEReader` + `MetadataReader` 纯静态扫描**，绝不 `Assembly.Load` 探探测 | 加载即执行（module initializer / 静态构造），必须避免 |
| D9 | 卸载语义 | **尽力而为**，失败即标记「重启生效」 | ALC 卸载在 WPF 下不彻底，不承诺热卸载可靠性 |
| D10 | 版本兼容 | `apiVersion` 主版本内兼容 + `hostVersion` 区间声明 | 简单可验证 |

---

## 4. 插件接口与扩展点设计

### 4.1 插件入口契约（契约草案，示意）

```csharp
namespace StarPie.Plugin.Abstractions;

/// <summary>插件唯一入口。宿主以无参构造实例化，随后调用 Initialize。</summary>
public interface IStarPiePlugin
{
    PluginMetadata Metadata { get; }               // 与 plugin.json 交叉校验
    void Initialize(IPluginContext context);       // 注册贡献点
    void Shutdown();                               // 撤销订阅、释放资源（必须幂等）
}
```

两条可选的**标记接口**用于表达插件能力形态（便于宿主裁剪加载成本）：

| 标记接口 | 用途 |
|---|---|
| `IActionPlugin` | 提供自定义动作类型 |
| `IStylePlugin` | 提供轮盘渲染形态 |
| `IIconPackPlugin` | 提供图标包 |
| `IPresetPlugin` | 提供预设方案 / 配置模板 |
| `ISettingsProvider` | 需要宿主渲染设置面板（见 §4.4） |

### 4.2 扩展点矩阵

> 优先级定义：**P0** = 首版必备；**P1** = 第二阶段；**P2** = 需先重构宿主；**P3** = 观察项。

| # | 扩展点 | 对应现状 | 优先级 | 宿主接缝位置 | 风险 |
|---|---|---|---|---|---|
| X1 | **自定义动作类型**（核心） | E1 `ActionExecutor.Execute` switch | **P0** | `ActionExecutor.cs:267` switch 兜底后查 `PluginCatalog` | 中（执行线程） |
| X2 | **动作类型目录**（下拉可见） | E2 `AggregatedActionTypes` | **P0** | `SlotViewModel.cs:803/816` 改为聚合 | 低 |
| X3 | **动作参数表单 Schema** | 新 | **P0** | `SubActionEditorWindow` 按 Schema 生成控件 | 中 |
| X4 | 图标包（内置矢量图标扩充） | E6 `IconHelper.VectorIconList` | **P1** | `GetSvgPathByKey` 查插件包 | 低 |
| X5 | 系统预设清单扩充 | E3 `SystemPresetList` | **P1** | `SlotViewModel.cs:11` 合并 | 低 |
| X6 | **轮盘渲染形态** | E5 `StyleRendererFactory` | **P1** | `StyleRendererFactory.cs:5` 工厂兜底 | **高**（逐帧热路径） |
| X7 | ShellTool 动词扩充 | E4 `ExecuteShellTool` | **P1** | `ActionExecutor.cs:702` default 分支 | 低 |
| X8 | 音效包 / 合成器 | E7 `SoundEffectManager` | **P2** | 需先抽象 `ISoundProvider` | 低 |
| X9 | 配置模板包（导出一份 `WheelProfile`） | E10 | **P1** | 导入流程 | 低 |
| X10 | 多语言词条注册 | E9 `I18n.Translations` | **P0** | `I18n.RegisterExternal` | 低 |
| X11 | OCR 引擎提供者 | E8 `OcrManager`（静态硬编码） | **P2** | 需先抽 `IOcrEngine` 接口 | 中（重构量大） |
| X12 | 主题/配色预设包 | `CustomColorPreset` | **P1** | 主题下拉合并 | 低 |
| X13 | 事件通知订阅（只读） | 新 | P3 | `IPluginContext.Events`（仅允许显式 token 订阅） | 中（泄漏风险） |
| X14 | 主界面自定义面板（`UserControl` 逃生门） | 新 | P3 | 需主题适配 + 内存约束 | 高 |

### 4.3 贡献点声明、命名与冲突策略

| 规则 | 内容 |
|---|---|
| 贡献 ID 规范 | 全局唯一：`<pluginId>.<contributionId>`，如 `com.example.timer.startTimer`；`pluginId` 为反向域名风格 |
| 保留命名空间 | `StarPie.*`、`Windows.*`、`Microsoft.*`、`System.*` 前缀**保留给官方**，社区插件声明即拒绝 |
| 图标 key 前缀 | 插件图标 key 必须为 `plugin:<pluginId>:<key>`，避免与 47 个内置矢量图标与用户自定义图标冲突 |
| 语言 key 前缀 | 插件词条 key 必须为 `plugin.<pluginId>.<key>` |
| ID 冲突 | 先注册者胜；后注册者**整体拒绝加载**并给出冲突详情（不允许部分注册，避免状态半残） |
| 动作 `Type` 值冲突 | 插件动作统一持久化为 `Type = "Plugin"`，真正的分发靠 `PluginActionRef`，**不占用既有内置 type 字符串空间** |
| 渲染形态 key 冲突 | 与内置 `Glassmorphism`/`CleanSectors`/`ClassicRing` 冲突时拒绝 |

### 4.4 动作参数与「声明式 UI」原则

对外动作的持久化模型（`ActionItem`，`ActionItem.cs`）已有 20+ 字段且支持 `SubActions` 递归与 `Clone()`。插件动作**不新增字段**，而是复用现有扩展槽：

```
ActionItem
 ├─ Type           = "Plugin"（稳定兜底，旧版读到不崩溃，降级为无操作）
 ├─ PluginActionRef = { PluginId, ContributionId }   ← 新增，单一复合字段
 ├─ Name / IconKey / CustomIconSvg / SubActions      ← 复用现有视觉与二级级联能力
 └─ ExtensionData  = Dictionary<string,string>       ← 新增，插件参数以字符串 KV 传递
```

**参数传递为什么用 `Dictionary<string,string>` 而不是插件自定义类型？**

1. `config.json` 由主程序用 `System.Text.Json` 序列化（`ConfigManager.cs:360`），插件类型不可序列化（会触发类型解析失败或需要多态声明）；
2. 字符串 KV 可被任意未来版本读写而不损坏配置；
3. 强制插件在边界处做类型转换与校验，天然形成「对外契约 vs 内部实现」的解耦；
4. 规避 R5 中「巨型字符串进 config」的 LOH 风险（宿主对单值长度设上限，如 8KB）。

**UI 渲染原则（关键决策 D5）**：

插件**不提供 XAML**，而是声明参数表单的 Schema，由宿主用既有控件风格渲染：

```
ParameterField { Key, Label(i18n key), Type(Text|Number|Bool|Path|File|Folder|Enum|Hotkey|Color|Icon),
                 Default, Required, Placeholder, EnumOptions[], ValidationRegex, MaxLength }
```

好处：① 主题跟随与深色模式对比度红线（AGENTS.md §5.2）由宿主统一保证；② 插件无法通过 UI 制造内存泄漏；③ 主程序改版不会让插件界面错位。
**逃生门**：`ISettingsProvider` 允许插件返回 `UserControl`，但标注为 P3、需人工审核，且强制主题注入与画刷 Freeze 检查。

---

## 5. 加载、识别、启用与手动选择机制

### 5.1 扫描目录（三层优先级）

> **本节已被实际实现取代（2026-09-15）**，下面保留原设计供对照。现行模型见文首横幅：
> ① `%LOCALAPPDATA%\StarPie\plugin-data\<id>\plugin.json` = 已安装插件的识别入口（等价于原优先级 1）；
> ② `<程序目录>\plugin\*.dll`（**扁平、顶层、只放 dll**）= 只读来源区，产出的是**待安装候选**而非已安装插件；
> ③ 用户自定义附加目录（`PluginsPreference.ExtraScanDirectories`）**仍是预留字段，尚未接入**。

| 优先级 | 目录 | 用途 | 说明 |
|---|---|---|---|
| 1 | `%LOCALAPPDATA%\StarPie\plugins\` | **默认安装位置**，手动选 `.dll` 后复制到此 | 核心目录 |
| 2 | `<程序目录>\plugins\` | 便携/绿色模式，随包分发 | 仅当检测到 `portable.flag` 或用户选择「便携模式」时启用 |
| 3 | 用户自定义附加目录（0..N） | **开发者调试专用** | 设置页可添加；不复制文件，直接引用外部路径 |

> ⚠️ `.gitignore` 已忽略 `*.dll`，因此仓库内开发用 `plugins/` 目录天然不会被提交（符合预期）。

扫描策略：**仅枚举 `plugins\<id>\plugin.json`**，不递归扫 `*.dll`（避免误把主程序目录当作插件）。扫描耗时目标 **< 10ms**（纯文件读，无反射）。

### 5.2 识别机制（静态、零执行）

识别分四道闸门，**任何一道不通过即拒绝，并给出人类可读的原因**：

| 闸门 | 检查项 | 实现思路（零 NuGet） |
|---|---|---|
| G-1 结构 | `plugin.json` 存在且符合 `schemaVersion`；必需字段齐全 | `JsonSerializer` + 手写校验 |
| G-2 程序集有效性 | 是否为合法 .NET 程序集（非原生 DLL / 非损坏文件） | `PEReader` + `MetadataReader`；无 CLR 头即拒绝 |
| G-3 契约符合性 | 是否存在实现 `IStarPiePlugin` 的类型；是否引用了 Abstractions；`TargetFrameworkAttribute` 是否为 `net8.0-windows*`；`CorFlags` 是否 x64/ILOnly | `MetadataReader` 扫 `TypeDefinition` + `InterfaceImplementation` + 自定义特性 blob；**不加载程序集** |
| G-4 完整性/来源 | SHA256 与 manifest 声明一致；是否有 Authenticode 签名（可选）；是否在「已知不良」黑名单 | `SHA256.Create()` + `X509Certificate.CreateFromSignedFile` |

> **为什么必须静态识别（决策 D8）**：实测（见 `PLUGIN_SYSTEM_PERFORMANCE_AND_API.md` §1.1.4）表明 —— `Assembly.LoadFrom` 返回时 `module initializer` **尚未执行**，但在完成「反射取类型 + 实例化」后**已执行**。也就是说触发点在**首次触碰模块成员**，而非加载瞬间。
> 结论因此更严格：识别阶段必须坚持纯元数据读取，**既不 Load、也不反射取类型、也不实例化** —— 「加载」与「反射取类型」这两个看似无害的动作，都已经进入了会执行不可信代码的边界之内。
> `System.Reflection.Metadata` 属于 .NET 共享框架自带程序集，不违反零依赖红线。实测单次全量扫描 **50 ~ 90 µs**，扫 50 个插件约 3 ~ 4.5 ms，可异步执行。

识别结果落为状态：`Compatible` / `Incompatible(原因)` / `Corrupted` / `Blocked`。

### 5.3 手动选择启用流程（核心交互，G1）

```
设置 → 「🔌 插件」标签页
   │
   ├─[➕ 从 .dll 文件安装…]  → 文件对话框（支持多选 *.dll）
   │     或直接把 .dll 拖入插件页
   │
   ├─ ① 静态识别（§5.2，无执行，进度可见）
   │      失败 → 逐项列出原因，终止
   │
   ├─ ② 呈现「安装确认卡」
   │      · 名称 / 版本 / 作者 / 主页 / 许可证
   │      · 目标框架、架构、文件大小、SHA256（前 12 位 + 可展开全文）
   │      · 签名状态：✅ 已签名（显示签名者）/ ⚠️ 未签名
   │      · **能力声明**（capabilities）：Process / FileSystem / Network /
   │        Clipboard / Registry / GlobalHook / Admin
   │      · 若是高风险能力组合 → 红色警示 + 二次确认
   │      · 必须勾选：「我已了解此插件将以 StarPie 当前权限在进程内运行」
   │
   ├─ ③ 复制到 %LOCALAPPDATA%\StarPie\plugins\<pluginId>\
   │      若目标已存在同名同版本 → 覆盖需确认；不同版本 → 并存需选择
   │
   ├─ ④ 写入 registry.json：状态初始值 = **Disabled（默认不启用）**
   │
   └─ ⑤ 用户在列表中点「启用」开关
          → 加载（§5.4）→ Initialize → 贡献点注册 → 状态 Enabled
          失败 → 状态 Failed + 原因 + 「查看日志」，贡献点不注册
```

**开发者模式旁路**：设置环境变量或在设置页开启「开发者模式」后，允许**只注册外部路径不复制文件**（便于附加调试器、热重载）。开启时页面顶部常驻醒目横幅提示风险。

### 5.4 加载器设计（`AssemblyLoadContext`）

| 要点 | 设计 |
|---|---|
| ALC 粒度 | 每插件一个独立的 `PluginLoadContext`，派生自 `AssemblyLoadContext`，`isCollectible: true` |
| 依赖解析 | 每插件目录内自带 `.deps.json` 时用 `AssemblyDependencyResolver`；无 `deps.json` 时回退到「插件目录探测 + 宿主已知程序集表」 |
| **共享程序集放行（关键）** | `Load()` 中，凡是 **Abstractions 程序集、BCL/共享框架程序集** 一律 `return null`（回退给 Default ALC）。理由：若插件自带一份 `StarPie.Plugin.Abstractions.dll` 并加载进自己的 ALC，`IStarPiePlugin` 会出现**两份类型身份**，`as`/强转全部失败——这是进程内插件最经典的翻车点 |
| 类型身份校验 | 加载后校验插件引用的 Abstractions 版本、公钥标记与宿主一致，不一致则拒绝 |
| 双分发形态兼容 | **Lightweight**：`AssemblyDependencyResolver` 正常。<br>**Standalone 单文件**：宿主自身 `Assembly.Location` 为空、无 `StarPie.deps.json`，因此**插件侧必须自带 `deps.json`**（推荐插件发布为「文件夹」形态）；若插件目录缺 `deps.json`，则只能解析引用了 Abstractions + BCL 的「纯插件」 |
| 插件打包形态 | 永远以**文件夹**为最小单元（`.spkg` 解压后即文件夹）；不支持「单个 .dll 单文件插件」，除非插件无任何第三方/原生依赖 |
| 原生依赖 | 插件目录若含 `.dll`（native），需在加载前 `NativeLibrary` 预解析或在 `ResolvingUnmanagedDll` 里挂钩；强制 x64 |
| 实例化 | `Activator.CreateInstance` 仅取**唯一** `IStarPiePlugin`：0 个 → 拒绝；>1 个 → 拒绝（要求 manifest 显式指定 `entryType`，0 个时按默认命名约定 `<AssemblyName>.Plugin`） |

### 5.5 惰性加载策略

| 状态 | 启动时行为 | 内存成本 |
|---|---|---|
| Disabled | 只读 `registry.json`，不碰插件目录 | 0 |
| Enabled + `Preload=false`（默认） | 仅读 manifest，**不加载程序集**；等首次被引用（首次执行该插件动作 / 打开插件设置页 / 用户点「立即加载」）才加载 | 0（仅 DTO） |
| Enabled + `Preload=true` | 启动后台线程延迟加载（启动完成后 3s，`ThreadPriority.BelowNormal`） | 插件自身成本 |
| 被动作引用 | 命中即加载；加载失败 → 动作执行报「插件未加载」并提示用户 | 插件自身成本 |

**内存会计口径（必须显式写进文档）**：R1 的 15~30MB 红线适用于**零插件**基线。启用插件后，应在插件管理页显示「宿主基线 / 插件附加 / 合计」，其中插件部分用 ALC 加载前后 `GC.GetTotalMemory` + 工作集差值估算，避免用户误判为主程序膨胀。

### 5.6 卸载与热重载的现实边界

| 操作 | 行为 | 说明 |
|---|---|---|
| 停用（Disable） | 撤销所有贡献点 → 调用 `Shutdown()` → `alc.Unload()` → 触发一次 `GC.Collect` + `WaitForPendingFinalizers` | **尽力而为** |
| 卸载（Uninstall） | 停用 + 删除目录（文件被占用则标记「重启后删除」） | 用 `MoveFileEx(..., MOVEFILE_DELAY_UNTIL_REBOOT)` 或启动时清理 |
| 热重载 | 停用 → 重新加载新版本 | **不保证成功**；失败则提示「重启 StarPie 生效」 |

> **诚实的限制说明（必须写进开发者文档）**：WPF 场景下 ALC 卸载经常不彻底，常见失败原因：① 插件订阅了主程序事件且未注销；② 插件创建了 WPF 视觉对象、注册了依赖属性、加入了静态缓存；③ 插件启动了自己的线程/定时器未停止；④ 反射创建的类型被静态字段持有。
> 因此契约强制要求：**插件只能通过 `IPluginContext` 提供的订阅 API 注册回调，并持有返回的 `IDisposable` token，在 `Shutdown()` 中全部释放**；宿主 `Shutdown()` 后**主动校验** ALC 是否真的卸载成功（`alc.Unloading` 事件 + 弱引用探测），失败则标记 `RequiresRestart`。

> **实测校准（见配套文档 §1.1.4）**：可回收 ALC 本身是可靠的 —— 宿主不持有任何引用时，5 个 ALC 仅需 **1 轮 GC、1.6 ms** 全部真正卸载；20 轮 load-unload 循环单轮 **1.2 ms**、工作集漂移 0.01 MB、GC 堆漂移 1 KB（无泄漏）。反之，只要宿主仍持有插件实例，卸载**必然失败**。
> 所以精确结论是：**失败原因永远是「引用没摘干净」，而不是运行时不可靠。** 这使「所有注册 API 返回 token + 宿主兜底撤销」从「双保险」升级为**必需项**。

---

## 6. 生命周期、隔离与安全

### 6.1 状态机

```
        ┌───────────┐  静态识别失败   ┌────────────┐
        │ Discovered│───────────────►│ Incompatible│  (含原因码)
        └─────┬─────┘                └────────────┘
              │ 识别通过 + 用户安装
              ▼
        ┌───────────┐   用户启用     ┌──────────┐
        │  Installed│──────────────►│ Loading  │
        │ (Disabled)│◄──────────────│          │
        └─────┬─────┘   用户停用     └────┬─────┘
              │                          │
              │                    ┌─────┴──────┐
              │                 成功│            │失败
              │                    ▼            ▼
              │              ┌──────────┐  ┌────────┐
              │              │  Active  │  │ Failed │
              │              └────┬─────┘  └────┬───┘
              │                   │             │ 连续 N 次
              │            运行期故障/熔断       ▼
              │                   ▼        ┌──────────┐
              │              ┌──────────┐  │Quarantine│
              │              │  Faulted │─►│ (自动禁用)│
              │              └────┬─────┘  └──────────┘
              │                   │
              └───────────────────┘  Unloading → Unloaded / RequiresRestart
```

| 状态 | 用户可见文案 | 是否加载程序集 |
|---|---|---|
| `Discovered` | 已发现（未安装） | 否 |
| `Installed/Disabled` | 已安装，未启用 | 否 |
| `Loading` | 正在启用… | 是 |
| `Active` | ✅ 已启用 | 是 |
| `Failed` | ❌ 启用失败（原因） | 否（已卸载） |
| `Faulted` | ⚠️ 运行异常（n 次） | 是 |
| `Quarantine` | 🚫 已自动禁用 | 否 |
| `Incompatible` | ⚠️ 不兼容（原因） | 否 |
| `RequiresRestart` | 需重启生效 | 部分 |

### 6.2 生命周期事件时序

| 阶段 | 宿主动作 | 插件动作 | 线程 |
|---|---|---|---|
| 安装 | 静态识别、复制、写 registry（Disabled） | — | UI |
| 启用 | `PluginLoadContext` 加载、校验类型身份、实例化 | 构造函数（**必须无副作用**） | UI（可异步） |
| 初始化 | 构造 `IPluginContext`（含服务、日志、i18n、Schema 注册） | `Initialize(context)`：只做注册，不做 IO | UI |
| 注册完成 | 校验贡献点冲突 → 原子提交到 `PluginCatalog` → 刷新 UI 目录 | — | UI |
| 运行期 | 动作调用 / 渲染调用 / 设置渲染 | 实现回调 | 见 §6.5 线程约定 |
| 停用 | 先摘除贡献点，再调 `Shutdown()` | 释放 token、停线程、清静态引用 | UI |
| 卸载 | `alc.Unload()` + 校验 | — | UI + GC |
| 安全模式 | 启动失败计数 ≥ N → 禁用最后加载的插件 | — | 启动早期 |

### 6.3 隔离分级（演进路线）

| 级别 | 形态 | 隔离强度 | 性能 | 建议 |
|---|---|---|---|---|
| **L0** | 直接 `Assembly.LoadFrom` 进默认 ALC | 无（依赖冲突/崩溃域共享） | 最好 | ❌ 不采用 |
| **L1** | 每插件独立 `PluginLoadContext` + 共享程序集放行 + 超时/熔断 | **故障隔离**（非安全隔离） | 好 | ✅ **首版采用** |
| **L2** | 独立进程宿主（`StarPie.PluginHost.exe`）+ 命名管道/gRPC IPC | 崩溃/权限可隔离 | 有 IPC 开销，内存翻倍 | 🔜 二期观察，适用于「不可信/高风险」插件分级 |
| **L3** | Windows 作业对象 + 低完整性级别（AppContainer） | 最强 | 复杂，且与全局钩子/提权冲突 | ❌ 与产品形态冲突，不采用 |

> **必须对用户诚实的一句话**（建议原文写进插件页与文档）：
> 「StarPie 的插件是**在你的电脑上、以 StarPie 的权限、在同一个进程内**运行的代码。启用任意插件等同于信任其作者。StarPie 提供的是故障隔离与透明告知，**不是安全沙箱**。」

### 6.4 能力声明（Capabilities）与安全模型

| 能力 | 含义 | 是否高风险 |
|---|---|---|
| `Process` | 启动进程 / 执行命令 | ⚠️ 高 |
| `FileSystem` | 读写用户文件 | ⚠️ 高 |
| `Network` | 联网 | ⚠️ 高 |
| `Clipboard` | 剪贴板读写 | 中 |
| `Registry` | 注册表读写 | ⚠️ 高 |
| `GlobalHook` | 注册输入钩子 | ⚠️ 高 |
| `Ui` | 打开自己的窗口 | 中 |
| `Admin` | 需要管理员权限运行 | ⚠️ 高 |

规则：
1. 清单**必须**显式声明所需能力（未声明却实际调用，视为越权——首版靠审核与文档约束，不做运行时拦截）；
2. 安装确认页把能力以人类语言逐条列出；
3. 组合包含 ≥2 项 ⚠️ 高风险能力 → 红色警示 + 二次确认；
4. 权限确认记录（`CapabilitiesAck` + 时间）写入 registry，升级插件若**新增**高风险能力 → 需重新确认，插件回到 Disabled；
5. 宿主在管理员权限下运行（`ConfigManager.IsElevated()`）时，插件页常驻提示「插件将继承管理员权限」。

### 6.5 故障隔离（决策 D7）

| 机制 | 设计 |
|---|---|
| **线程约定** | 插件回调分三类：`ActionCallback`（动作执行线程）、`RenderCallback`（UI 渲染线程）、`Init/ShutdownCallback`（UI 线程）。SDK 明确标注；插件不得在渲染回调中阻塞 |
| **动作并发分类** | `ActionKind.Sequential`（会发 `SendInput`/操作剪贴板/与前台窗口交互 → 必须串行，走 `ActionExecutor` 线程）<br>`ActionKind.Background`（纯计算/网络/文件 → 宿主用 `Task.Run` 并发执行，**不占动作线程**） |
| **超时** | 动作：Sequential 默认 5s（可配 1~30s），Background 默认 30s；渲染：**单帧 4ms 预算**，超时则跳过本轮装饰绘制并计数 |
| **熔断** | 连续失败 ≥3 次 或 窗口期（60s）内失败率 >50% → 转入 `Faulted` 并弹托盘提示「插件 X 已连续出错，是否禁用？」；连续 ≥5 次自动 `Quarantine` |
| **异常边界** | `PluginInvoker` 统一包裹：捕获 **全部** `Exception`（含 `ThreadAbortException` 之外的 `StackOverflow` 无法捕获 → 只能靠进程外隔离或不做）→ 写插件专属日志 → 返回失败结果。**严禁**冒泡到 `ActionExecutor.Execute`（`:329` 会弹 `MessageBox`） |
| **渲染回调降级** | 插件渲染器抛异常 → 本轮降级为内置 `ClassicRingRenderer`，并在插件页记录 |
| **安全模式** | 启动阶段记录「启动序号 + 已加载插件集」；若连续 2 次启动在插件加载后 ≤30s 内异常退出 → 下次启动**自动禁用**上次加载的插件集并通知用户 |
| **日志限流** | 每插件日志 ≤ 200 行/分钟、单文件 5MB、保留 7 天，超限丢弃并提示一次（防止插件刷爆磁盘） |
| **实测开销** | 安全包裹（`try/catch` + `Stopwatch`）**39 ~ 45 ns/次**；跨 ALC 接口调用 **2.3 ~ 3.9 ns/次**。相对 `SendInput` 的毫秒级耗时，插件调用与保护机制的占比 **< 0.001%** —— 保护机制本身**不是**性能负担 |
| **性能纪律（实测导出）** | ① 禁止 `MethodInfo.Invoke`（实测 39 ~ 43 ns，比缓存委托慢 **12 倍**）；② 禁止每次调用新建参数字典（实测让成本从 40 ns 涨到 **86 ~ 106 ns** 并制造 GC 压力）|

### 6.6 提权与 UIPI

| 场景 | 处理 |
|---|---|
| StarPie 以管理员运行 | 插件同样获得管理员权限 → 安装确认页红色警示 |
| StarPie 普通权限 | 插件动作若需要提权（如 C 盘写入），执行失败需给出明确原因，而不是静默失败 |
| 已有 UIPI 放行逻辑 | `TrayController` 的 `ChangeWindowMessageFilterEx` 放行**仅用于托盘**，不得因插件需求扩大放行范围 |
| 「以普通用户身份运行」 | 复用 `ActionItem.RunAsStandardUser` 的 Shell 令牌降权思路：插件动作若声明 `CanRunAsStandardUser`，宿主可在降权子进程中执行（二期考虑） |

---

## 7. 版本与依赖兼容性

### 7.1 三层版本号

| 版本 | 归属 | 示例 | 作用 |
|---|---|---|---|
| `schemaVersion` | `plugin.json` 清单结构 | `1` | 清单格式演进；不识别即拒绝，给出「请升级 StarPie」 |
| `apiVersion` | **SDK 契约**（`StarPie.Plugin.Abstractions`） | `1.0` | 插件与宿主的能力契约；**主版本内向后兼容** |
| `hostVersion` | 主程序版本 | `1.7.4` | 插件声明可运行的宿主区间 |

### 7.2 兼容性判定规则（判定顺序）

```
1. schemaVersion 是否被当前宿主支持？        否 → Incompatible("清单格式过新")
2. 是否架构匹配（x64）？                     否 → Incompatible("架构不匹配")
3. TFM 是否为 net8.0-windows10.0.19041.0 或更低版本窗口？  否 → Incompatible("目标框架不兼容")
4. apiVersion 主版本 == 宿主支持的契约主版本？ 否 → Incompatible("契约版本不兼容，请更新插件")
5. hostVersion 是否落在 [minHostVersion, maxHostVersion]？否 → Incompatible("宿主版本超出声明区间")
6. 插件间依赖是否全部满足且无环？            否 → Incompatible("缺少依赖 / 循环依赖")
7. SHA256 是否匹配？                         否 → Corrupted("文件已损坏或被修改")
   ✔ 全部通过 → Compatible
```

**兼容矩阵**（示例，需随版本维护并写入文档）：

| 宿主版本 | 支持的 `apiVersion` | 支持的 `schemaVersion` | 可用 .NET SDK |
|---|---|---|---|
| 1.7.x | 1.0 | 1 | .NET 8 SDK |
| 1.8.x（预计） | 1.0 / 1.1 | 1 | .NET 8 SDK |
| 2.0.x（预计） | 2.0（1.x 显示「受限兼容」） | 1 / 2 | .NET 9 SDK |

> **契约演进策略**：接口**只增不改不删**；破坏性变更必须提升 `apiVersion` 主版本；废弃成员先标 `[Obsolete]` 并保持 ≥2 个 minor 版本可用。插件侧不得依赖任何 `internal`/`sealed` 内部类型。

### 7.3 插件间依赖

| 项 | 设计 |
|---|---|
| 声明 | `plugin.json` 的 `dependencies: [{ id, versionRange }]` |
| 版本区间语法 | 采用简化语义化版本：`"1.2.0"`（≥）、`"[1.0,2.0)"`（区间）、`"*"`（任意） |
| 解析 | 深度优先拓扑排序；**检测环**，检出即整体拒绝加载并输出环路径（`A → B → C → A`） |
| 缺失依赖 | 依赖未安装/未启用 → 该插件状态 = `Incompatible("缺少依赖 X ≥ 1.2.0")`，并给出「一键跳转安装」入口（若已发现） |
| ALC 共享 | **依赖的插件程序集不共享 ALC**（首版）；插件间通过 `IPluginContext.GetService<T>()` 拿到对方**导出的服务接口**，而非直接引用对方 DLL。这样可以避免 ALC 交叉引用导致两者都无法卸载 |
| 服务导出 | 插件可在 manifest 声明 `exports: [{ contract: "IContract 全名", version }]`；宿主做契约类型身份校验 |

### 7.4 主程序升级路径

| 场景 | 行为 |
|---|---|
| 宿主 minor 升级，插件仍兼容 | 无感；首次运行记录 `LastHostVersion` |
| 宿主新增 `apiVersion 1.1`（只增） | 老插件（`apiVersion 1.0`）继续工作 |
| 宿主废弃某接口（`Obsolete`） | 插件正常加载，插件页显示「即将弃用」提示 |
| 宿主突破性变更（`apiVersion 2.0`） | 1.x 插件标记 `Incompatible`，但**保留目录与配置**，提示作者升级；提供「受限兼容模式」开关（尝试加载，失败即回滚） |
| 宿主更新器（`UpdateManager`） | 需扩展：更新前提示「本次更新将影响 N 个已启用插件」。**供应链加固**：插件包校验和与签名校验应在更新链中先行修复（对应既有 H2 风险） |

---

## 8. 通信与数据约定

### 8.1 宿主 → 插件：契约接口

插件通过 `Initialize(IPluginContext)` 拿到宿主能力，**而非静态类直连**：

```csharp
public interface IPluginContext
{
    PluginMetadata Me { get; }
    string PluginDataDirectory { get; }              // 插件私有可写目录
    IPluginLogger Log { get; }
    IPluginSettings Settings { get; }                // 读写插件私有 settings.json
    II18nRegistry   I18n { get; }                    // 注册插件词条
    IIconRegistry   Icons { get; }                   // 注册图标
    IActionRegistry Actions { get; }                 // 注册动作类型 + 参数 Schema
    IStyleRegistry  Styles { get; }                  // 注册轮盘渲染形态（P1）
    INotificationService Notify { get; }             // 托盘气泡 / 非侵入提示
    IHostInfo Host { get; }                          // 宿主版本、apiVersion、是否提权、语言
    IPluginEvents Events { get; }                    // 只读事件（返回 IDisposable token）
    IDispatcherFacade Dispatcher { get; }            // 显式切 UI 线程
}
```

设计要点：
- **服务定位器式**，插件不接触宿主具体类型 → 宿主可重构而不破坏插件；
- 所有 `Register*` 在 `Initialize` 期间生效，运行期再注册需走显式 API 并触发 UI 刷新；
- 每个注册 API 都返回 `IDisposable`，宿主在停用时自动撤销（**双保险**，即使插件忘记 `Shutdown()` 也能摘干净）。

### 8.2 插件 → 宿主：动作执行契约

```csharp
public interface IActionContribution
{
    ActionDescriptor Descriptor { get; }             // id / 名称 i18n key / 图标 / 分类 / ActionKind
    IReadOnlyList<ParameterField> Parameters { get; }
    ActionPreview Preview(PluginActionInput input);  // 用于「测试」按钮与列表副标题
    Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken ct);
    bool Validate(PluginActionInput input, out string error);
}
```

| 约定 | 内容 |
|---|---|
| 执行签名 | 一律 `Task` 异步 + `CancellationToken`（宿主超时即取消） |
| 结果 | `ActionResult { Success, Message?, Data? }`，失败必须给可用中文修读的原因 |
| 幂等性 | `Background` 类动作需容忍重复调用（超时取消后宿主可能重试） |
| 输入 | `PluginActionInput { IReadOnlyDictionary<string,string> Params, ActionContext Context }`，`ActionContext` 提供前台进程名、活动窗口句柄、鼠标位置、是否提权等**只读**环境信息 |
| 禁忌 | 动作实现中**禁止**：调用 `MessageBox`、阻塞 >超时、注册全局钩子、修改 `AppConfig` |

### 8.3 数据落盘分区（决策 D6）

> **路径已变更（2026-09-15）**：下表 `%LOCALAPPDATA%\StarPie\plugins\` 一路读作
> `%LOCALAPPDATA%\StarPie\plugin-data\`；便携模式下为 `<程序目录>\plugin-data\`。
> 分区原则本身没有变，唯一例外是插件日志（`logs\plugins\`）位置未动。

| 数据 | 位置 | 归谁管 | 理由 |
|---|---|---|---|
| 插件启用状态、版本、哈希、能力确认 | `%LOCALAPPDATA%\StarPie\plugins\registry.json` | **宿主**（原子写） | 独立文件，规避 H1 的 `config.json` 竞态 |
| 插件健康度/失败计数/安全模式 | `%LOCALAPPDATA%\StarPie\plugins\health.json` | 宿主 | 高频写，必须与用户配置分离 |
| 插件私有配置 | `%LOCALAPPDATA%\StarPie\plugins\<id>\settings.json` | **插件**（经 `IPluginContext.Settings`） | 插件数据不污染主配置 |
| 插件私有数据（缓存/数据库） | `%LOCALAPPDATA%\StarPie\plugins\<id>\data\` | 插件 | 卸载时可选择是否保留 |
| 插件日志 | `%LOCALAPPDATA%\StarPie\logs\plugins\<id>_yyyy-MM-dd.log` | 宿主 | 与被限流的写入逻辑统一 |
| 主 `config.json` 中的 `Plugins` 段 | 仅存**用户偏好**（是否允许插件、扫描目录、开发者模式） | 宿主 | 极小、低频 |
| **只读来源区**（新增） | `<程序目录>\plugin\*.dll` | 发行方 / 用户，**宿主只读** | 随包分发的待安装候选；宿主绝不在只读的程序目录里写文件 |

### 8.4 配置 JSON 契约与向后兼容

**向后兼容措施（低成本高收益）**：

1. 在 `AppConfig` 与 `ActionItem` 上添加 `[JsonExtensionData] public Dictionary<string, JsonElement>? Extras`，使**旧版主程序读到新版/插件写入的未知字段时不丢弃**（当前 `ConfigManager` 反序列化用 `PropertyNameCaseInsensitive = true`，未知字段会被静默丢弃 —— 这对插件是致命的）；
2. `Plugins` 段与 `PluginActionRef`/`ExtensionData` 字段全部给默认值，保证**旧配置升级无感**；
3. 反序列化统一 `PropertyNameCaseInsensitive = true`（现状已是），并**禁用**插件向主配置写自定义类型。

**`ActionItem` 扩展后的 JSON 形态（示意）**：

```json
{
  "Type": "Plugin",
  "Name": "启动番茄钟",
  "PluginActionRef": { "PluginId": "com.example.pomodoro", "ContributionId": "start" },
  "ExtensionData": { "minutes": "25", "mode": "work" },
  "IconKey": "plugin:com.example.pomodoro:timer",
  "SubActions": []
}
```

> 旧版主程序读到 `Type="Plugin"` 时命中 `switch` 无匹配分支 → 静默无操作（当前实现即如此），**不会崩溃**，这是选择 `Type="Plugin"` 而非复用内置 type 的核心原因。

### 8.5 可观测性

| 项 | 设计 |
|---|---|
| 日志 | `AppLogger` 新增插件分区；每条含 `[plugin:<id>]` 前缀，便于 `grep` |
| 生命周期日志 | 加载/卸载耗时、贡献点数量、版本、哈希前 12 位全部落日志（便于社区排障） |
| 性能计数 | 插件页显示：每插件动作调用次数/平均耗时/失败率/渲染帧超时次数 |
| 用户可导出诊断包 | 「导出插件诊断」→ 含 registry/health/manifest/日志尾部（**不含**动作参数中的敏感值，需脱敏） |

---

## 9. 目录结构与配置项设计

### 9.1 磁盘布局

```
%LOCALAPPDATA%\StarPie\
├── config.json                     # 主配置（新增 Plugins 偏好段）
├── Configs\                        # 既有：多方案配置
├── logs\
│   ├── starpie_2026-09-15.log
│   └── plugins\
│       ├── com.example.pomodoro_2026-09-15.log
│       └── dev.local.mytool_2026-09-15.log
└── plugins\                        # ★ 插件根目录（新增）
    ├── registry.json               # ★ 宿主管理：启用状态 / 版本 / 哈希 / 能力确认
    ├── health.json                 # ★ 宿主管理：失败计数 / 安全模式标记
    └── com.example.pomodoro\       # ★ 一个插件 = 一个文件夹
        ├── plugin.json             # ★ 清单（唯一识别入口）
        ├── StarPie.Plugin.Pomodoro.dll
        ├── StarPie.Plugin.Pomodoro.deps.json   # 推荐携带（单文件宿主形态必需）
        ├── ThirdParty.Xyz.dll      # 插件私有第三方依赖
        ├── runtimes\win-x64\native\  # 插件私有原生依赖（x64）
        ├── i18n\                   # 插件自带多语言
        │   ├── zh-CN.json
        │   └── en-US.json
        ├── icons\                  # 插件图标（SVG）
        │   └── timer.svg
        ├── settings.json           # 插件私有配置（插件自管）
        └── data\                   # 插件私有数据目录
```

**`.spkg` 打包格式**：`plugin.json` + 上述目录的结构化 ZIP（扩展名 `.spkg`，本质是 zip），便于社区分发与哈希校验。也可直接分发文件夹。

### 9.2 `plugin.json` 清单规范（字段表）

| 字段 | 类型 | 必填 | 说明 | 校验 |
|---|---|---|---|---|
| `schemaVersion` | int | ✔ | 清单结构版本，首版 `1` | 必须被宿主支持 |
| `id` | string | ✔ | 反向域名风格全局唯一 ID | `^[a-z0-9]([a-z0-9-]*\.)+[a-z0-9-]+$`；不得以 `starpie`/`windows`/`microsoft`/`system` 开头 |
| `name` | string | ✔ | 显示名 | ≤40 字符 |
| `description` | string | ✔ | 一句话简介 | ≤200 字符 |
| `author` | string | ✔ | 作者或组织 | ≤60 |
| `homepage` | string | ✗ | 项目主页 | URL |
| `license` | string | ✔ | SPDX 标识（建议 MIT） | — |
| `version` | string | ✔ | 插件版本（语义化） | — |
| `apiVersion` | string | ✔ | 编译所依赖的 SDK 契约版本 | 主版本必须匹配 |
| `minHostVersion` | string | ✔ | 最低宿主版本 | — |
| `maxHostVersion` | string | ✗ | 最高宿主版本（空=不限） | — |
| `targetFramework` | string | ✔ | `net8.0-windows10.0.19041.0` | 精确或更低窗口版本 |
| `platform` | string | ✔ | `win-x64` | 必须 |
| `entryType` | string | ✗ | `IStarPiePlugin` 实现的全名 | 存在性静态校验 |
| `capabilities` | string[] | ✔ | 能力声明（§6.4） | 枚举白名单 |
| `contributions` | object | ✔ | 声明提供的贡献点（动作/图标/渲染形态/预设…） | 与运行时注册交叉校验 |
| `dependencies` | array | ✗ | 插件间依赖 | 拓扑排序 + 环检测 |
| `exports` | array | ✗ | 对外导出的服务契约 | 契约类型校验 |
| `icon` | string | ✗ | 插件图标（相对路径 SVG/PNG） | — |
| `tags` | string[] | ✗ | 分类标签 | ≤8 个 |
| `sha256` | string | ✗ | 主程序集哈希（发布时由打包脚本写入） | 与实文件比对 |

提供 `plugin.schema.json`（JSON Schema）供编辑器自动补全与作者自检，**并要求作者在 CI 中跑 `starpie-plugin validate`**。

### 9.3 主 `config.json` 新增段（仅用户偏好）

```json
{
  "Plugins": {
    "EnablePluginSystem": true,
    "PortableMode": false,
    "DeveloperMode": false,
    "ExtraScanDirectories": [],
    "PreloadOnStartup": false,
    "ActionTimeoutSeconds": 5,
    "RenderFrameBudgetMs": 4,
    "MaxPluginMemoryWarningMb": 80,
    "ShowIncompatiblePlugins": true
  }
}
```

> **为什么不把启用列表放进 `config.json`**：① 既有 H1 竞态（钩子线程高频读 `CurrentConfig`，配置写盘无锁）；② 插件启停是**相对高频**操作，会不断触发整个 97+ 键配置的序列化与原子替换；③ 插件状态属于「宿主运维数据」而非「用户偏好数据」，语义上应分离；④ 独立文件可单独备份/重置（「重置插件系统」= 删 registry.json）。

### 9.4 `registry.json` / `health.json` 结构（示意）

```json
// registry.json
{
  "registryVersion": 1,
  "updatedAt": "2026-09-15T12:00:00+08:00",
  "entries": [
    {
      "id": "com.example.pomodoro",
      "name": "番茄钟",
      "version": "1.2.0",
      "installPath": "com.example.pomodoro",
      "externalPath": null,
      "enabled": true,
      "preload": false,
      "entrySha256": "9f2c...",
      "signerThumbprint": null,
      "capabilitiesAck": ["Process", "Clipboard"],
      "ackedAt": "2026-09-15T12:00:00+08:00",
      "ackedHostVersion": "1.7.4",
      "source": "UserSelectedFile",
      "installedAt": "2026-09-15T12:00:00+08:00"
    }
  ]
}
```

```json
// health.json
{
  "consecutiveStartupFailures": 0,
  "lastStartupPluginSet": ["com.example.pomodoro"],
  "safeModeUntil": null,
  "plugins": {
    "com.example.pomodoro": {
      "loadCount": 12,
      "consecutiveFailures": 0,
      "windowFailures": 1,
      "lastError": null,
      "requiresRestart": false
    }
  }
}
```

写入策略：临时文件 + `File.Replace`（与 `ConfigManager.SaveConfig` 的既有原子写法保持一致）；**registry 写入加锁**，避免重蹈 H1。

### 9.5 设置界面（避免继续膨胀 God Object）

| 项 | 设计 |
|---|---|
| 位置 | 新增第 5 个标签页「🔌 插件」（简单模式下可隐藏，高级模式可见） |
| 实现 | **独立 `PluginManagerWindow` 或 `PluginPage` UserControl**，不写入 `SettingsWindow.xaml.cs`（该文件已 10,524 行，见既有 P3 建议） |
| 页面区块 | ① 顶部：插件系统总开关 / 开发者模式 / 扫描目录管理<br>② 主列表：图标 · 名称 · 版本 · 作者 · 状态徽章 · 启用开关 · 「⋯」菜单（详情/查看日志/打开目录/卸载）<br>③ 底部：[➕ 从 .dll 安装] [从 .spkg 安装] [刷新扫描] [重置插件系统]<br>④ 详情抽屉：能力声明 · 贡献点列表 · SHA256 · 性能计数 · 错误历史 |
| 无障碍/主题 | 复用 `AppThemeManager.ApplyTheme`，满足 AGENTS.md §5.2 深色模式对比度要求 |

---

## 10. 面向社区共创的规范、示例与文档

### 10.1 仓库与服务矩阵

| 仓库 | 内容 | 维护方 |
|---|---|---|
| `SoftBlack42/StarPie` | 主程序 + `src/StarPie.Plugin.Abstractions` | 官方 |
| `SoftBlack42/StarPie.Plugin.Sample` | 官方示例插件（3 个）+ `dotnet new` 模板 | 官方 |
| `SoftBlack42/StarPie.Plugin.Template` | `StarPie.Plugin.Template` NuGet 模板包（或直接 zip 模板） | 官方 |
| `SoftBlack42/StarPie-Plugins-Registry` | **社区插件索引**（`index.json` + PR 审核） | 官方 + 社区 |
| 各作者自己的仓库 | 插件源码 + Release 中的 `.spkg` | 社区 |

> **索引仓库不做市场服务器**：`index.json` 只是「id / 名称 / 作者 / 版本 / 下载 URL / SHA256 / capabilities / 兼容区间」的静态清单，客户端拉取后可离线浏览，点击后**仍需用户手动下载并手动选择启用**（保持 N7 语义）。

### 10.2 模板与示例工程

| 示例 | 演示的扩展点 | 复杂度 |
|---|---|---|
| **HelloAction** | X1/X2 自定义动作 + X3 参数 Schema + X10 i18n 词条 | ★ 入门（30 分钟） |
| **PomodoroTimer** | X1 动作（带状态、定时器、托盘通知、私有 settings.json） | ★★ 进阶 |
| **IconPack / ThemePack** | X4 图标包 + X12 配色预设 | ★ 入门 |
| **VoronoiRenderer**（可选） | X6 自定义轮盘渲染形态（演示画刷 Freeze 与逐帧预算） | ★★★ 高级 |

模板应内置：
- `starpie-plugin.sln` + `Plugin.csproj`（TFM 预配好、`x64`、`CopyLocalLockFileAssemblies`、`GenerateDependencyFile`）
- `plugin.json` 模板 + `plugin.schema.json` + 校验脚本
- `build.ps1`：一键 `dotnet publish` → 生成 `.spkg` → 写入 `sha256` 到 manifest
- GitHub Actions 工作流：构建 + 校验清单 + 产出 Release 附件
- `README` 模板（含能力声明表与免责声明）

### 10.3 文档清单（`docs/plugins/`）

| 文档 | 内容 |
|---|---|
| `getting-started.md` | 环境准备 → 拉模板 → 写 HelloAction → 本地调试 → 打包 → 安装验证 |
| `manifest-spec.md` | `plugin.json` 全字段规范 + JSON Schema + 版本语义 |
| `api-reference.md` | `IStarPiePlugin` / `IPluginContext` / 各 `Register*` / DTO 全量说明，标注线程与超时 |
| `extension-points.md` | X1~X14 扩展点逐个说明、适用场景、示例、限制 |
| `capabilities-and-security.md` | 能力声明规范、风险等级、用户告知文案、**「不是沙箱」** 的明确说明 |
| `performance-rules.md` | 渲染逐帧预算、画刷 Freeze 强制、禁止 IO/分配的位置、内存会计口径 |
| `debugging.md` | 开发者模式、外部路径注册、附加进程调试、日志定位、热重载限制 |
| `publishing.md` | 打包 `.spkg`、哈希、签名（可选）、提交到索引仓库的 PR 流程 |
| `compatibility-matrix.md` | 宿主版本 × `apiVersion` × TFM 对应表 |
| `faq.md` | 卸载不掉？插件冲突？提权下不生效？ |
| `CODE_OF_CONDUCT.md` / `SECURITY.md`（扩展） | 社区行为准则、插件安全漏洞上报渠道 |

### 10.4 发布与索引流程

```
作者开发 → 本地自检(validate 清单) → 打 .spkg → 传 GitHub Release
                                              │
                            作者提交 PR 到 Registry（追加 index.json 条目）
                                              │
                          官方审核（清单规范 / 能力声明合理性 / 哈希 / 许可 / 安全扫描）
                                              │
                                    合并 → 客户端可见（仍需用户手动启用）
```

**审核清单（Review Checklist）**：
1. `id` 唯一且不占用保留前缀；2. 能力声明与实际行为一致（抽查源码）；3. 无 `MessageBox`、无阻塞主线程；4. 渲染器画刷已 Freeze 且满足逐帧预算；5. 未引用 `StarPie.dll`（仅引用 Abstractions + BCL）；6. 有开源许可；7. 无自动下载执行远端代码的行为；8. 隐私：不静默回传用户数据；9. `.spkg` 哈希与 manifest 一致；10. 文档与截图齐全。

### 10.5 命名规范与保留字

| 项 | 规范 |
|---|---|
| 插件 ID | `反向域名.功能`，全小写，如 `io.github.yourname.timer` |
| 贡献 ID | `<pluginId>.<contribution>`，全小写驼峰（`startTimer`） |
| 语言 key | `plugin.<pluginId>.<key>` |
| 图标 key | `plugin:<pluginId>:<key>` |
| 保留前缀 | `starpie*`、`windows*`、`microsoft*`、`system*`、`builtin*` |
| 程序集/命名空间 | `StarPie.Plugin.<PluginName>`（camel 大写）；**禁止**使用 `WinPieGestures.*`（历史命名空间） |

### 10.6 开发者体验（DX）

| 能力 | 设计 |
|---|---|
| 快速迭代 | 开发者模式下「外部路径注册」+ 不复制文件 → 改代码 rebuild → 点「重新加载」 |
| 调试 | 文档给出「附加到 StarPie 进程」步骤；SDK 提供 `[Conditional("DEBUG")]` 断言辅助 |
| 清单自检 | 宿主插件页对识别失败给出**原因码 + 修复建议**（而非一句「加载失败」） |
| 错误定位 | 插件页「查看日志」直接打开该插件日志文件；错误历史保留最近 50 条 |
| 沙盒验证 | 建议作者先在一台干净机器或临时用户配置下验证，避免污染日常配置 |

---

## 11. 对现有代码的改造点清单

> **原则**：所有改造都是「薄接缝」，不改核心链路（钩子线程 / 几何判定 / 渲染主循环）。

| # | 文件 | 改造内容 | 侵入度 | 优先级 |
|---|---|---|---|---|
| C1 | （新增）`StarPie.Plugin.Abstractions` 项目 | SDK 契约程序集 | 新文件 | P0 |
| C2 | （新增）`Plugin/PluginHost.cs`、`PluginScanner.cs`、`PluginRegistry.cs`、`PluginLoadContext.cs`、`PluginCatalog.cs`、`PluginInvoker.cs` | 宿主实现，独立目录，不混入既有文件 | 新文件 | P0 |
| C3 | `ActionExecutor.cs:267` | `switch` 结束后追加插件分派（查 `PluginCatalog` → `PluginInvoker`，带超时与异常兜底） | 极小 | P0 |
| C4 | `SlotViewModel.cs:803/816` | `AggregatedActionTypes` / `LocalizedActionTypes` 改为「内置 + 插件」聚合（保留静态内置清单，避免每次 new 的分配浪费） | 小 | P0 |
| C5 | `SlotViewModel.cs:11` | `SystemPresetList` 合并插件预设（P1） | 小 | P1 |
| C6 | `I18n.cs` | 新增 `RegisterExternal(pluginId, dict)` + 语言变更时通知插件；保持缺 key 回退行为 | 小 | P0 |
| C7 | `StyleRendererFactory.cs:5` | 工厂末尾兜底查插件渲染器，并对返回画刷做防御性 `Freeze()` | 小 | P1 |
| C8 | `IconHelper.cs:60/427/1163` | `GetSvgPathByKey` 支持 `plugin:` 前缀查插件图标包；`VectorIconList` 保持内置只读 | 小 | P1 |
| C9 | `ActionItem.cs` | 新增 `PluginActionRef`、`ExtensionData`；加 `[JsonExtensionData]`（与 §8.4 联动） | 小 | P0 |
| C10 | `AppConfig.cs` | 新增 `Plugins` 偏好段；加 `[JsonExtensionData]` | 小 | P0 |
| C11 | `App.xaml.cs`（`OnStartup`） | 启动早期「安全模式检查」→ 插件系统延迟初始化（不阻塞首帧） | 小 | P0 |
| C12 | `ConfigManager.cs` | **不动** `config.json` 读写逻辑；仅在导入/导出时保留 `Plugins` 段；另行新增 registry 独立读写 + 加锁 + 原子写 | 小 | P0 |
| C13 | `AppLogger.cs` | 新增 `GetPluginLogPath(id)` + 按插件分区 + 限流 | 小 | P0 |
| C14 | `SettingsWindow.xaml(.cs)` | **仅新增一个导航入口**指向独立 `PluginManagerWindow`；不在该文件内实现插件页 UI | 极小 | P0 |
| C15 | `UpdateManager.cs` | 更新前提示受影响的启用插件；补 SHA256 校验（顺带修既有 H2） | 中 | P1 |
| C16 | `OcrManager.cs` | 抽 `IOcrEngine` 接口以开放 X11（重构量大） | 大 | P2 |
| C17 | `SoundEffectManager.cs` | 抽 `ISoundProvider` 以开放 X8 | 中 | P2 |
| C18 | `.github/workflows/build-and-test.yml` | 新增 SDK 契约的编译校验 + 清单 Schema 校验 + 抽象层单元测试（补既有 CI 不跑测试的缺口） | 中 | P0 |
| C19 | `AGENTS.md` | 新增「插件系统架构与红线」章节（含「插件不得引用主程序集」「画刷必须 Freeze」「钩子线程零插件代码」三条铁律） | 文档 | P0 |
| C20 | `.gitignore` | 无需改动（`*.dll` 已忽略）；建议显式加 `plugins/`、`*.spkg` 以表达意图 | 极小 | P0 |

---

## 12. 分期路线图

| 阶段 | 目标 | 交付物 | 验收 |
|---|---|---|---|
| **P0 · 地基（MVP）** | 打通「手动选 .dll → 启用 → 自定义动作生效」最小闭环 | Abstractions v1.0（`IStarPiePlugin` + `IPluginContext` + `IActionContribution`）<br>Scanner/Registry/Loader/Catalog/Invoker<br>插件页（独立窗口）<br>HelloAction 示例<br>getting-started + manifest-spec + capabilities 文档 | 零插件时性能与当前版本持平；示例插件端到端跑通；识别失败给出明确原因 |
| **P1 · 贡献点扩展** | 开放低风险多形态共创 | X4 图标包 / X5 预设 / X6 渲染形态 / X7 ShellTool / X9 配置模板 / X10 i18n<br>依赖与版本区间、拓扑排序<br>熔断 + 安全模式 + 性能计数<br>能力确认与升级再确认 | 4 个示例插件；插件导致崩溃可被自动禁用；渲染插件超预算自动降级 |
| **P2 · 社区与治理** | 形成可持续共创生态 | 索引仓库 + PR 审核流程 + `plugin.schema.json` + 发布脚本 + 签名支持<br>文档补全（api-reference / performance-rules / debugging / publishing）<br>CI 校验 + 兼容矩阵 | 第三方作者独立发布 ≥3 个插件；索引可离线浏览；清单不合法者被 CI 拦下 |
| **P3 · 深化（观察）** | 提升隔离与形态上限 | L2 独立进程宿主（针对高风险插件）<br>`IOcrEngine` / `ISoundProvider` 重构开放<br>受限 `UserControl` 逃生门 + 主题强制注入 | 高风险插件可跨进程运行；插件崩溃 100% 不波及主程序 |

**建议排期原则**：P0 之前，先修复既有 H1（配置竞态）与 H2（更新无校验）中的**最小必要项**——因为插件系统会把这两个风险的暴露面从「本地单包」放大到「N 个社区 DLL」。至少要把 registry 的原子写与插件包哈希校验做在 P0 内。

---

## 13. 风险、限制与待决策问题

### 13.1 已知风险与缓解

| 风险 | 等级 | 缓解 | 残余风险 |
|---|---|---|---|
| 插件=任意代码执行（含提权） | 🔴 高 | 默认禁用 + 手动启用 + 能力声明 + 哈希/签名可见 + 提权警示 | **无法消除**，只能透明告知（须写入 README 与首次启用弹窗） |
| ALC 卸载不彻底 | 🟡 中 | 强制 token 注销 + 卸载后校验 + `RequiresRestart` 标记 | 部分插件需重启生效 |
| 单线程动作队列被插件阻塞 | 🟡 中 | 并发分类 + 超时 + 熔断 | 超时取消无法强杀同步阻塞代码（`Task` 取消是协作式的），必要时二期上 L2 进程宿主 |
| 渲染插件拖累帧率 | 🟡 中 | 逐帧 4ms 预算 + 超帧降级为内置渲染器 | 首帧感知抖动 |
| 插件膨胀破坏 R1 内存红线 | 🟡 中 | 默认惰性 + 内存会计展示 + 超阈值告警 | 用户主动启用大插件时不可避免 |
| 供应链（复用更新通道） | 🔴 高 | 插件包 SHA256 强制校验；建议作者签名；索引仓库审核 | 无签名插件仍存在 |
| 契约随宿主演进腐化 | 🟡 中 | 只增不改 + `[Obsolete]` 过渡 + 抽象层单独 CI | 需长期纪律 |
| 10,524 行的 `SettingsWindow` 继续膨胀 | 🟡 中 | 插件页做成独立窗口，不入 God Object | 需在 PR 审核中守住 |

### 13.2 待决策问题（需产品/作者拍板）

| # | 问题 | 建议（我的倾向） |
|---|---|---|
| Q1 | 插件是否允许自带 WPF `UserControl`？ | **首版禁止**，仅声明式 Schema（§4.4）；P3 再评估逃生门 |
| Q2 | 是否引入 Authenticode 代码签名作为「推荐/强制」？ | **推荐不强制**：强制会让个人开发者门槛过高；签名插件显示绿标并给「更高信任」提示，但不作为加载前置条件 |
| Q3 | 是否提供官方 NuGet 包 `StarPie.Plugin.Abstractions`？ | **提供**（作者体验最好），但需注意主机零依赖红线**不受影响**（是插件侧引入，非宿主） |
| Q4 | 是否内置「插件索引浏览器」？ | 建议 P2 做**只读浏览 + 跳转下载**，不做应用内自动安装（保持手动语义） |
| Q5 | 插件是否允许注册全局钩子（`GlobalHook` 能力）？ | 建议**禁止并入插件进程**（`WH_*_LL` 为进程级，会与主程序钩子互相干扰），改为「通过 `IPluginContext` 请求宿主提供按键事件订阅」 |
| Q6 | 卸载插件时是否保留私有数据？ | 默认**保留**并询问；提供「彻底清除」选项 |
| Q7 | 是否支持「插件包签名 + 官方认证徽章」？ | 建议 P2 起步，用索引仓库审核代替技术认证 |
| Q8 | `apiVersion 1.0` 冻结范围 | 建议 P0 只冻结「动作 + i18n + 图标」三件事，其余留待 P1，避免过早固化契约 |

---

## 附：一句话总结

> 插件系统的设计核心不是「怎么加载 DLL」，而是**把 StarPie 已存在的 14 个隐式扩展点收束成一张薄注册表，同时守住三处不可触碰的核心（钩子线程、几何判定、配置写盘）**；对社区，诚实地交付「故障隔离 + 透明告知」，而不是无法兑现的「安全沙箱」承诺。
