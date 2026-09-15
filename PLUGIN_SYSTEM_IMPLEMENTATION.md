# StarPie 插件系统 · 实现说明

> 本文档面向三读者：**维护宿主的开发者**（看结构与职责边界）、**社区插件作者**（看接口与用法）、
> **未来的 AI Agent**（看为什么这么设计、哪些坑已经踩过）。
>
> 配套文档：`PLUGIN_SYSTEM_DESIGN.md`（设计大纲）、`PLUGIN_SYSTEM_PERFORMANCE_AND_API.md`（性能实测与接口清单）。

---

> ## ⚠️ 目录模型已变更（2026-09-15），正文路径请勿照抄
>
> 正文（尤其第 1 章的结构图与安装流程）用的是当时的**单一插件根目录** `plugins\`。
> 实现已改为**两个目录职责分离**：
>
> - `<程序目录>\plugin\` = **只读来源区**：随包分发的待安装候选，扁平且只放 `.dll`，宿主**不创建、不写入、不删除**；
> - `%LOCALAPPDATA%\StarPie\plugin-data\` = **可写宿主区**：安装副本 + `registry.json` + `health.json` + 插件私有 `data\`（便携模式只改这里的落点）。
>
> 因此正文里所有 `<...>\plugins\` 一律读作 `<...>\plugin-data\`（唯一例外是插件日志 `logs\plugins\`）。
> 安装流程也新增了「只读来源区 → 候选列表 → 用户点安装」这条路径，且复制策略按清单来源分叉。
> 权威定义见 `AGENTS.md` 第 3.7 节与 `WinPieGestures/Plugin/PluginPaths.cs`。

---

## 0. 本轮交付内容与验收状态

### 0.1 验收证据

端到端自检（无界面，`--plugin-selftest`）已跑通全链路：

| 阶段 | 结果 | 关键指标 |
|---|---|---|
| [0] 初始化 | PASS | 200 ms，不加载任何程序集 |
| [1] 静态识别 | PASS | 22 ms，清单来源 Manifest，x64，SHA256 已算 |
| [2] 安装 | PASS | 落盘 + 登记为 Disabled，**未加载程序集** |
| [3] 启用 | PASS | 285 ms（其中加载 34~48 ms），注册 2 个动作 |
| [4] 调用 | PASS | 11~23 ms；负向用例正确拒绝缺参调用 |
| [5] 停用 | PASS | 195~220 ms，**ALC 真实回收**（`需要重启才能释放 = False`） |
| [6] 卸载 | PASS | 目录即时删除（无「文件被占用」挂起） |
| [7] 环境还原 | PASS | 残留登记 0、残留词条 0 |

> **关于「ALC 真实回收」的独立证据**：卸载后插件目录被**即时删除成功**，日志中不再出现
> `目录被占用，已挂起删除`。DLL 文件锁只在 ALC 真正卸载时才释放——这比宿主自己的弱引用探测更硬。

### 0.2 代码规模

| 部分 | 文件数 | 行数 |
|---|---|---|
| SDK 契约程序集 `StarPie.Plugin.Abstractions/` | 8 个 .cs | 837 |
| 宿主插件内核 `WinPieGestures/Plugin/` | 16 个 .cs | 6416 |
| 示例插件 `samples/HelloAction/` | 2 个 .cs | 344 |
| 示例插件配置（`plugin.json` / `plugin.schema.json` / `csproj`） | 3 个 | 331 |
| 合计 | | **7928** |

构建状态：SDK / 宿主 / 示例三者均 **0 警告 0 错误**，零 NuGet 依赖。
契约程序集编译后仅 **24.5 KB**（插件作者要引用的全部东西）。

---

## 1. 整体结构

### 1.1 三层程序集（强制边界）

```
┌───────────────────────────────────────────────────────────────┐
│  社区插件 DLL（第三方）                                        │
│    · 只引用 StarPie.Plugin.Abstractions + BCL                  │
│    · 绝不引用 StarPie.dll（宿主内部类型签名不是公开契约）        │
└───────────────────────────┬───────────────────────────────────┘
                            │ 编译期引用
┌───────────────────────────▼───────────────────────────────────┐
│  StarPie.Plugin.Abstractions.dll   ← SDK 契约（随主程序分发）   │
│    · 只有接口 / 枚举 / 纯 DTO，零实现、零状态                   │
│    · TFM: net8.0-windows（低于宿主，可被向下引用）              │
└───────────────────────────┬───────────────────────────────────┘
                            │ 编译期引用（宿主 → SDK，永不反向）
┌───────────────────────────▼───────────────────────────────────┐
│  StarPie.exe（宿主）                                           │
│    · TFM: net8.0-windows10.0.19041.0，AssemblyName=StarPie      │
│    · x64 only，零 NuGet 依赖                                   │
│    · 包含全部插件内核（WinPieGestures/Plugin/）                 │
└───────────────────────────────────────────────────────────────┘
```

**为什么要拆出独立的 SDK 程序集**，而不是把接口放进宿主：

1. 插件作者只需引用一个 ~20KB 的契约 DLL 就能编译，不必拖入整个宿主；
2. 类型身份唯一。只要 SDK 由**宿主默认 ALC** 提供，插件里的 `IStarPiePlugin` 与宿主里的就是同一个类型，
   `as` 强转才有效。若把接口定义在 `StarPie.dll` 里，插件引用它就等于引用宿主——版本一升级就碎片化。

**发布形态**：`StarPie.Plugin.Abstractions.dll` 必须与 `StarPie.exe` 同目录（`ProjectReference` 默认
CopyLocal 即为正确行为）。同时它也会作为官方 NuGet 包发布（`IsPackable=true`，`PackageId=StarPie.Plugin.Abstractions`）。

### 1.2 磁盘布局

```
%LocalAppData%\StarPie\
├── config.json                     ← 主配置（含 Plugins 配置节；插件启停状态不在这里）
├── logs\
│   ├── starpie_YYYY-MM-DD.log      ← 宿主日志（含 WARN/ERROR 级插件消息镜像）
│   └── plugins\
│       └── <pluginId>_YYYY-MM-DD.log   ← 每插件独立日志，限流 200 行/分、单文件 5MB
└── plugins\                        ← 便携模式下位于程序目录 \plugins\
    ├── registry.json               ← 插件登记表（启停/预加载/安装来源）★不进 config.json
    ├── health.json                 ← 健康度（失败计数/熔断状态/卸载结论）
    ├── .pending-delete-<guid>\     ← 卸载时文件被占用，挂起到下次启动清理
    └── <pluginId>\
        ├── StarPie.Plugin.XXX.dll  ← 入口程序集（**不含** Abstractions.dll）
        ├── XXX.deps.json
        ├── plugin.json             ← 清单
        ├── <插件私有依赖>.dll
        └── data\
            ├── settings.json       ← 插件私有配置（原子写）
            └── ...                 ← 插件自己的数据
```

**为什么启停状态必须独立于 `config.json`**：项目既有的 `config.json` 存在已知的读写竞态（H1）。
把高频、可能来自任意线程的插件状态写进去会放大该问题。`registry.json` / `health.json` 走独立的
加锁 + 原子写通道（临时文件 + `File.Replace`），互不干扰。

---

## 2. 核心模块职责划分

### A. 识别与准入层（决定「这个 DLL 能不能用」，全程不执行任何插件代码）

| 模块 | 行数 | 职责 |
|---|---|---|
| `PluginPaths.cs` | 149 | **磁盘路径的唯一来源**。Portable/常规模式判定、各文件路径、ID 合法性正则（必须是小写反向域名）、保留前缀校验、文件名安全化 |
| `PluginManifestReader.cs` | 306 | 读并校验 `plugin.json`；裸 DLL 场景从 `AssemblyMetadataAttribute` 兜底还原清单；持有宿主版本号（读自 `AssemblyInformationalVersion`）与宿主 TFM |
| `PluginScanner.cs` | 868 | **四道闸门**的 PE 静态识别。PE 头 + CorFlags + `MetadataReader`，判定 TFM/架构/契约实现/完整性。**绝不 `Assembly.Load`** |
| `PluginScanResult.cs` | 174 | 识别结果 + 20 种失败原因码；每个原因码配一段面向用户的标题与修复建议 |
| `SimpleVersion.cs` | 184 | 简化 semver 解析比较（带回滚下界的预发布放行规则）；`TargetFrameworkInfo` 的 TFM 解析与兼容判定 |

### B. 加载与隔离层（决定「怎么把它跑起来、怎么干净地拿掉」）

| 模块 | 行数 | 职责 |
|---|---|---|
| `PluginLoadContext.cs` | 223 | 每插件一个**可回收 ALC**。`Load()` 四步判定：SDK → 放行；宿主拥有 → 放行；Default 能提供 → 放行；剩余才从插件目录加载。原生 DLL 走同样的目录探测 |
| `PluginInstance.cs` | 851 | 单个插件的运行时对象。`Load()`（重识别 → 建 ALC → 取类型并**校验类型身份** → 构造上下文 → `Initialize` → 提交事务）；`Unload()`（撤贡献点 → 剪订阅 → Shutdown → 释放 token → Teardown）；状态机；熔断计数；健康度落盘；**两阶段卸载判定** |

### C. 注册与调度层（插件与轮盘之间的唯一接缝）

| 模块 | 行数 | 职责 |
|---|---|---|
| `PluginCatalog.cs` | 292 | **唯一的接缝注册表**。动作 / 图标 / 词条三类注册；**事务式**（`BeginSession` 暂存 → `Commit` 冲突则整体拒绝 / `Discard`）；查询与整体撤销 |
| `PluginContext.cs` | 443 | `IPluginContext` 的实现。内含三个注册表实现（动作 ID 正则、参数 Key 去重、枚举必须有选项、SVG 64KB 上限）与 `RegistrationToken` |
| `PluginInvoker.cs` | 394 | 动作调用。`Sequential`（占动作线程，默认 5s 超时）/ `Background`（线程池，默认 30s）；异常归一化；结果解释；熔断判定 |
| `PluginHostServices.cs` | 355 | 宿主服务的具体实现：日志 / 设置 / 通知 / 宿主信息 / UI 派发 / **动作调用代理**（强制复用宿主已验证的 `SendInput` 链路）/ 事件服务 |

### D. 持久化与外围层

| 模块 | 行数 | 职责 |
|---|---|---|
| `PluginRegistryStore.cs` | 341 | `registry.json` + `health.json` 的读写。全局锁串行化、原子写、损坏文件备份重建 |
| `PluginSettings.cs` | 114 | 插件私有 `settings.json`，原子写，单值长度受 SDK 常量约束（防 LOH 碎片） |
| `PluginLogger.cs` | 198 | 每插件独立日志文件、限流、WARN/ERROR 镜像到宿主日志、旧日志清理 |
| `PluginHost.cs` | 1213 | **门面，也是唯一对外入口**。安装 / 启停 / 卸载 / 调用 / 事件广播 / 安全模式 / 预加载 / 惰性加载。主程序只认这一个类 |
| `PluginSelfTest.cs` | 311 | `--plugin-selftest <dll> [报告路径]`，无界面端到端自检 |

### E. 宿主接缝（改动既有文件）

| 文件 | 改动 |
|---|---|
| `ActionExecutor.cs` | `switch` 新增 `case "Plugin"` 分支 → `PluginHost.ExecutePluginAction`；把 `ExecuteWebUrl` / `SafeSetClipboardText` / `ExecuteFolder` / `ExecuteLaunch` / `ExecuteHotkey` 由 `private` 改 `internal`，供插件通过 `IHostActionInvoker` 复用 |
| `ActionItem.cs` | 新增 `PluginActionRef?`、`Dictionary<string,string>? ExtensionData`、`[JsonExtensionData] Extras`；`Clone()` 补深拷贝 |
| `AppConfig.cs` | 新增 `Plugins` 配置节与 `[JsonExtensionData] Extras` |
| `PluginsPreference.cs`（新增） | 插件系统偏好模型，9 项参数 |
| `App.xaml.cs` | 启动：单实例检测放行 `--plugin-selftest`；`LoadConfig` 后旁路自检；托盘就绪后 `Dispatcher.BeginInvoke(ApplicationIdle, PluginHost.Initialize)`。退出：`PluginHost.ShutdownAll()` |
| `I18n.cs` | 新增外部词条注册表（volatile 写时复制快照，**读侧完全无锁**），供插件注入多语言而不拖慢既有查表路径 |

### F. 示例

| 文件 | 说明 |
|---|---|
| `samples/HelloAction/HelloActionPlugin.cs` | 入口实现：注册动作、词条、图标、事件订阅，演示 token 留存与幂等 `Shutdown` |
| `samples/HelloAction/Actions.cs` | 两个动作贡献：`GreetContribution`（4 个参数含 Enum+Bool，演示通知）、`OpenFolderContribution`（演示走 `Context.Host.OpenFolder` 而非自己 `Process.Start`） |
| `samples/HelloAction/plugin.json` | 完整清单示例 |
| `samples/HelloAction/plugin.schema.json` | 清单的 JSON Schema，供作者在 IDE 里获得补全与校验 |
| `samples/HelloAction/HelloAction.csproj` | 参考模板，含**三条必须照抄的配置**及其原因注释 |

---

## 3. 模块间接口与交互方式

### 3.1 依赖方向（单向，不可逆）

```
PluginHost（门面）
  ├─→ PluginRegistryStore     持久化
  ├─→ PluginScanner ─→ PluginManifestReader ─→ SimpleVersion
  ├─→ PluginInstance ─→ PluginLoadContext（ALC）
  │        ├─→ PluginContext ─→ 三个注册表实现
  │        └─→ PluginCatalog（暂存/提交）
  ├─→ PluginInvoker ─→ PluginHostServices（宿主服务实现）
  └─→ PluginLogger / PluginSettings / PluginPaths

ActionExecutor（既有）──只有一处指针──→ PluginHost.ExecutePluginAction
```

**关键约束**：`PluginScanner` / `PluginManifestReader` / `SimpleVersion` / `PluginPaths` **不引用**
`PluginInstance` 与 `PluginLoadContext`。识别层必须能在完全不触碰加载能力的前提下工作。

### 3.2 六条主要交互链路

**① 启动初始化（严格不加载程序集）**

```
App.OnStartup → LoadConfig → 单实例检查（--plugin-selftest 旁路）
   → 托盘就绪 → Dispatcher.BeginInvoke(ApplicationIdle)
      → PluginHost.Initialize()
           ├─ PluginPaths.Configure(portable)  解析磁盘布局
           ├─ CleanupPendingDeletions()        清理上轮挂起的删除
           ├─ CheckSafeMode()                  连续 2 次启动异常 → 自动禁用插件系统
           ├─ SyncFromDisk()                   登记新发现的插件目录
           └─ SchedulePreload()                延迟 3s 预加载（若配置开启）
      → NotificationSink 接入托盘气泡
结果：只有清单在内存里，程序集一个没加载。
```

**② 手动选择 DLL → 安装（用户唯一入口）**

```
用户点「从文件安装…」→ 文件选择器（过滤 *.dll）
   → PluginHost.PrepareInstall(dllPath)
        → PluginScanner.ScanSelectedDll()
             闸门 G-1 清单有效性 / G-2 程序集结构 / G-3 契约符合性 / G-4 完整性
             ★ 顺序刻意是「先看程序集，再看清单」——用户最常见的错误就是选错文件
             失败 → PluginScanResult，带原因码 + 用户话术 + 修复建议
   → 返回识别结果给 UI 展示「能力声明 + 贡献点 + SHA256 + 签名」
   → 用户勾选确认 → PluginHost.CommitInstall(scan, options{ Acknowledged, OverwriteExisting,
                                                        AcknowledgedCapabilities })
        → 复制落盘到 plugins\<id>\（跳过打包内的 Abstractions.dll）
        → 登记为 Disabled
```

**③ 启用（加载 + 事务式注册）**

```
PluginHost.Enable(pluginId)
   → PluginInstance.Load()
        ├─ 加载前**重新识别一次**（防止文件在安装后被替换）
        ├─ new PluginLoadContext(pluginDirectory, isCollectible: true)
        ├─ alc.LoadFromAssemblyPath(entryDll)
        ├─ assembly.GetType(entryType)
        ├─ ★ created as IStarPiePlugin  ← 类型身份校验，失败即报「SDK 版本不一致」
        ├─ new PluginContext(...) + new PluginEventService(...)
        ├─ plugin.Initialize(context)
        │     └─ 插件在这里调用 context.Actions.Register(...) / I18n.RegisterTable(...) / ...
        │        全部写入 **PluginRegistrationSession 暂存区**，对外不可见
        └─ session.Commit()
              ├─ 无冲突 → 一次性并入 PluginCatalog（此刻才对轮盘可见）
              └─ 有冲突 → 整体拒绝，不留半残状态
```

**④ 从轮盘触发插件动作**

```
用户划到扇区 → ActionExecutor.Execute(action)
   → case "Plugin" → PluginHost.ExecutePluginAction(action)
        ├─ PluginActionRef 合法性检查
        ├─ Catalog.TryGetAction(fullId)
        ├─ 未加载？ → 惰性 Enable()（首帧多等一次加载，代价换来静默期零内存占用）
        ├─ Quarantined？ → 直接拒绝
        ├─ contribution.Validate(parameters)  ← 插件自己的校验，异常被拦
        └─ PluginInvoker.Invoke(instance, registration, parameters)
             ├─ Sequential → 动作线程同步执行，task.Wait(timeout) + 宽限期
             └─ Background → Task.Run + Task.WhenAny，不占动作线程
             → 结果归一化为 PluginExecuteOutcome
        ★ 最外层 try/catch 兜底：本方法**绝不抛异常**，失败只记日志 + 托盘气泡，绝不弹 MessageBox
```

**⑤ 停用与卸载判定（本项目最隐蔽的一处坑）**

```
PluginHost.Disable(pluginId)
   ├─ instance.UnloadVerdictFinalized = 回调   ← 先登记，不急着下结论
   ├─ instance.Unload()
   │     ├─ RevokeContributions()      ┐
   │     ├─ RevokeEventSubscriptions() ├ 各自独立成 **[NoInlining] 方法**
   │     ├─ RequestPluginShutdown()    ├ 为的是让插件侧对象随栈帧一起消失
   │     ├─ DisposeTokens()            ┘
   │     └─ Teardown()
   │          ├─ 摘净字段（_plugin / _pluginContext / _events / _session / _loadContext）
   │          ├─ alc.Unload()  ← 只是**请求**卸载
   │          ├─ context = null       ← 本帧最后一个指向 ALC 的局部变量
   │          ├─ 快路径：[NoInlining] WaitForCollection(weak)  ← 只传弱引用，绝不传 ALC
   │          │     ├─ 成功 → RequiresRestart=false，回调(true)
   │          │     └─ 失败 → 暂标 true，**不下结论**
   │          └─ ScheduleDeferredUnloadProbe()  ← 250ms / 750ms / 2000ms 三轮
   │                计时器回调跑在线程池，其调用栈与触发卸载的那条链**毫无关系**
   └─ FlushHealth() + 登记 Enabled=false
```

**为什么必须是两阶段**：同步那一刻，调用栈上可能仍有别的帧持有插件侧对象——例如插件管理页
正拿着 `List<PluginActionRegistration>`（其 `Contribution` 就是插件程序集里的类型实例），
而用户恰恰就是从这个页面点的「停用」。这些引用会随栈展开自然消失，但同步探测等不到那一刻。
所以**结论只能在栈展开之后下**，日志与用户提示也必须由最终回调驱动，否则会把
「已成功释放」误报成「请重启 StarPie」。

**⑥ 事件广播（宿主 → 插件，反方向）**

```
WPF 侧：MouseHook 捕获 → RadialWindow 呈现
   → 呈现前：Dispatcher.BeginInvoke(PluginHost.RaiseWheelOpening(context))
        ★ 必须投递到 UI 线程：轮盘呈现路径紧跟在鼠标钩子之后，插件代码绝不允许出现在那条路径上（红线 R2）
   → RaiseWheelOpening 先复制 handler 快照再加锁外调用，单个 handler 异常不影响其余
语言切换：I18n.LanguageChanged（宿主内部事件）→ PluginEventService 的包装闭包 → 插件 handler
```

### 3.3 插件侧看到的接口全貌

插件只通过一个入口拿到一切：`Initialize(IPluginContext context)`。

| 通道 | 接口 | 说明 |
|---|---|---|
| 我是谁 / 在哪 | `context.Me` / `PluginDirectory` / `DataDirectory` | `DataDirectory` 是插件唯一应写入的位置 |
| 日志 | `context.Log` → `IPluginLogger` | 自动带插件 ID，限流，不需要自己管文件 |
| 配置 | `context.Settings` → `IPluginSettings` | 原子写，不必自己处理 `settings.json` |
| 注册动作 | `context.Actions.Register(contribution)` → `IDisposable` | 返回 token **必须留存** |
| 注册词条 | `context.I18n.Register / RegisterTable` | key 自动加 `plugin.` 前缀 |
| 注册图标 | `context.Icons.RegisterSvg(...)` → 完整 key | 自动加 `plugin:<id>:` 前缀，64KB 上限 |
| 调宿主能力 | `context.Host` → `IHostActionInvoker` | `SendHotkey` / `SendText` / `Launch` / `OpenFolder` / `OpenUrl` / 剪贴板。**强制复用**宿主已验证的 `SendInput` 链路，严禁插件自己 P/Invoke |
| 订阅事件 | `context.Events` → `IPluginEvents` | `OnLanguageChanged` / `OnWheelOpening` / `OnWheelClosed`，均返回 token |
| 回 UI 线程 | `context.Dispatcher` → `IDispatcherFacade` | `Post` / `InvokeAsync` / `IsOnUiThread` |
| 通知用户 | `context.Notify` → `INotificationService` | 走托盘气泡，无托盘时降级为日志 |
| 宿主信息 | `context.Info` → `IHostInfo` | 宿主版本、语言、是否便携、是否提权 |

**每一条注册 API 都返回 `IDisposable`——这是必需项，不是可选设计。**
实测结论：只要宿主仍持有插件实例或其贡献对象，ALC 卸载 100% 失败。插件忘掉释放 token 是常态，
所以宿主侧的 `RevokeAll` 兜底撤销同样必需。

动作调用的输入输出契约：

```csharp
public interface IActionContribution
{
    ActionDescriptor Descriptor { get; }                        // Id / 显示名 / 归类 / 图标 key / 执行方式 / 超时
    IReadOnlyList<ParameterField> Parameters { get; }           // 声明式参数表单，由宿主渲染，插件不提供 XAML
    string? Validate(IReadOnlyDictionary<string, string> parameters);  // 返回 null 表示通过，否则为错误文案
    string Preview(IReadOnlyDictionary<string, string> parameters);    // 轮盘悬停时的实时提示，返回空串表示不显示
    Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken);
}
```

`PluginActionInput` 除了参数，还带一份 `Context`——宿主在调用瞬间采集的前台环境快照
（前台进程名 / 窗口标题 / 窗口句柄 / 光标坐标 / 是否提权 / 当前语言）。
插件不需要自己 P/Invoke 去查这些。

---

## 4. 关键机制说明

### 4.1 纯静态识别：四道闸门，零执行

| 闸门 | 判什么 | 手段 |
|---|---|---|
| G-1 清单 | `plugin.json` 是否存在且字段合法 | `PluginManifestReader.Validate`（schemaVersion → id → 基本字段 → 版本 → 宿主区间 → platform → TFM） |
| G-2 程序集结构 | 是否 .NET 程序集、架构、TFM、引用集 | `PEReader` + `CorFlags` + `TargetFrameworkAttribute` + `TargetPlatformAttribute` |
| G-3 契约符合性 | 是否存在可实例化的 `IStarPiePlugin` 实现 | `MetadataReader` 遍历 TypeDef，沿基类链判定 |
| G-4 完整性 | SHA256、签名 | 流式计算摘要 + `X509Certificate.CreateFromSignedFile` |

**为什么必须零执行**：`Assembly.LoadFrom` 一但返回，模块初始化器（module initializer）就可能已经跑过；
一旦反射取类型或实例化，任意代码就落地了。识别阶段的定位是「让用户在安装前看清这是什么」，
所以它必须能对一个来源不明的 DLL 给出结论而完全不承担执行风险。

三个实测细节已经写进代码注释：

- `MetadataReader` 的 **TypeDef 表第 1 行永远是 `<Module>` 伪类型**，其 `Extends` 列为空值，
  直接读 `BaseType` 会抛 `BadImageFormatException: Read out of bounds`。必须先按名字跳过。
- **刻意不要求 `Machine == Amd64`**：AnyCPU 纯 IL 程序集在 x64 宿主中完全安全，
  强制要求会误伤 Visual Studio 的默认模板（它默认生成 AnyCPU）。
- 插件若由 VS 默认模板生成，`TargetPlatformAttribute` 是 `Windows7.0`，
  需按「无平台版本」处理（视为最低），否则会误判为不兼容。

### 4.2 版本兼容的三重判定

1. **API 契约主版本必须一致**（`apiVersion` 的 major 必须等于宿主 `PluginApi.ApiVersionMajor`）。
   这是硬门槛，契约主版本变化意味着签名不兼容。
2. **TFM 不得高于宿主**：`net8.0-windows` / `net8.0-windows7.0` 通过，`net9.0+` 拒绝。
3. **宿主版本区间**：`minHostVersion` / `maxHostVersion`。
   其中一条规则值得单独说明——**宿主为同版本号的预发布版时视为满足下界**：
   宿主 `1.7.4-beta.2` 满足插件声明的 `≥ 1.7.4`。原因很实际：官方发行版长期处于 beta / rc 通道，
   插件作者就是照着当前版本号写下界的。若按 semver 字面量严格判定（`1.7.4-beta.2 < 1.7.4`），
   会导致「声明当前版本的插件全部装不上」，作者只能靠反复试探一个更低的数字绕过。
   反向依然严格：`1.7.3` 绝不满足 `≥ 1.7.4`，`1.7.4-beta.2` 绝不满足 `≥ 1.7.5`。

### 4.3 类型身份：进程内插件最经典的翻车点

`PluginLoadContext.Load()` 的判定顺序**不能调换**：

```csharp
① 是 SDK 契约程序集？      → return null          // 交回 Default ALC，保证类型身份唯一
② 是宿主拥有的核心程序集？ → return null          // 不给插件目录任何覆盖机会
③ Default ALC 已经能提供？ → return null          // 共享框架、主程序集
④ 剩余才 LoadFromAssemblyPath(插件目录)          // 插件私有依赖
```

只要第 ① 步漏掉，插件目录里那份 `StarPie.Plugin.Abstractions.dll` 就会被加载成第二份类型定义，
于是 `created as IStarPiePlugin` 静默返回 `null`——编译全过、运行必挂、日志里只有一句「入口类型未实现契约」。
这也是示例 `csproj` 里 `<Private>false</Private>` 那一行存在的原因：从源头不产出这一份多余拷贝。

### 4.4 资源与超时

| 机制 | 参数 | 说明 |
|---|---|---|
| 动作超时 | Sequential 5s / Background 30s（可配） | 走 `.Wait(timeout)` + 1s 宽限期 |
| **诚实说明** | — | `Task` 取消是**协作式**的。插件里一个同步死循环（如 `while(true)` 不检查 token）无法被强杀，只能标记并隔离 |
| 熔断 | 连续失败 3 次 → `Faulted` 提示；5 次 → `Quarantined` 自动禁用 | 计数留在内存，每 50 次调用才落盘一次（`health.json`） |
| 安全模式 | 连续 2 次启动异常 → 自动禁用插件系统 | 避免一个坏插件把宿主拖进崩溃循环 |
| 内存 | 启动零加载 | 只有被绑定到扇区且被触发时才 `Enable` |

---

## 5. 配置项

### 5.1 宿主侧：`config.json` → `Plugins` 节

| 字段 | 默认 | 说明 |
|---|---|---|
| `EnablePluginSystem` | `true` | 总开关 |
| `PortableMode` | `false` | 插件目录跟随程序目录（绿色版） |
| `DeveloperMode` | `false` | 更详细日志、允许加载未签名插件、显示诊断信息 |
| `ExtraScanDirectories` | `[]` | 额外的插件搜索目录 |
| `PreloadOnStartup` | `false` | 启动后延迟 3s 预加载已启用插件（以启动耗时换首帧延迟） |
| `ActionTimeoutSeconds` | `5` | Sequential 动作超时 |
| `RenderFrameBudgetMs` | `4` | 插件在轮盘渲染路径上的时间预算（仅告警） |
| `MaxPluginMemoryWarningMb` | `80` | 单插件内存告警阈值 |
| `ShowIncompatiblePlugins` | `true` | 是否在列表里展示不兼容插件（灰显 + 原因） |

`AppConfig` 与 `ActionItem` 都加了 `[JsonExtensionData]`，作用是：旧版本打开新配置再保存时，
不会把读不懂的键（例如后续版本新增的插件字段）抹掉。

### 5.2 插件侧：`plugin.json`

完整字段与约束见 `samples/HelloAction/plugin.schema.json`（JSON Schema，可直接被 IDE 消费）。
必填 8 项：`schemaVersion` / `id` / `name` / `version` / `apiVersion` / `minHostVersion` /
`targetFramework` / `platform`。

---

## 6. 错误处理

### 6.1 识别失败原因码

`PluginScanFailure` 枚举共 20 项，覆盖：清单缺失/损坏/版本不支持、ID 非法或占用保留前缀、
入口程序集缺失、不是 .NET 程序集、Requires32Bit、TFM 过高、平台不匹配、未实现契约、
入口类型不可实例化、宿主版本越界、apiVersion 不匹配、SHA256 不符、签名不受信任等。

每一项都通过 `PluginScanFailureText.Title()` / `Hint()` 提供用户可读文案与修复建议。例如：

```
原因码：HostVersionOutOfRange
标题：宿主版本超出插件声明区间
详情：插件要求宿主 ≥ 1.7.4，当前为 1.7.4-beta.2。请升级 StarPie。
修复建议：当前 StarPie 版本不在插件声明的可运行区间内。请升级 StarPie，或联系作者放宽版本区间。
```

### 6.2 运行期失败的分级处置

| 场景 | 处置 |
|---|---|
| 识别失败 | 不进列表（或灰显），展示原因码与修复建议；**不加载** |
| 安装后启用失败 | 状态置 `Failed`，记录 `LastError`；连续 2 次 → 自动禁用 |
| 动作执行抛异常 | 拦在 `PluginInvoker`，返回失败结果；宿主动作线程不受影响 |
| 动作超时 | 标记失败并计数；达到阈值进入 `Faulted` / `Quarantined` |
| 插件贡献点注册冲突 | 事务整体回滚，插件不启用，原因回报给用户 |
| 卸载时文件被占用 | 挂起到 `.pending-delete-<guid>\`，下次启动清理 |
| ALC 未能回收 | 标记 `RequiresRestart`，由最终判定回调记日志 + 托盘提示「重启后彻底回收」 |
| 宿主 UI 路径 | **插件代码永不出现在鼠标钩子回调链上**；异常一律不外抛，不使用 MessageBox |

### 6.3 一个已修复的真实缺陷（记录以备后鉴）

自检最初报 `NotDotNetAssembly / BadImageFormatException: Read out of bounds`。
排查链路：在 catch 里输出异常类型与堆栈 → 定位到 `TypeDefTableReader.GetExtends`
← `TypeDefinition.get_BaseType()` ← `PluginScanner.TypeImplementsContract`。
根因是 `<Module>` 伪类型（见 4.1）。

修掉之后暴露出第二个问题：`需要重启才能释放 = True`。逐个假设验证后发现**两个同类的栈帧根因**：

1. `Unload()` 的局部变量 `IStarPiePlugin? plugin` 横跨了 `Teardown()` 的探测点；
2. `PluginSelfTest.Run` 的 `List<PluginActionRegistration> actions` 同样横跨了 `Disable` 调用。

修法不是继续找局部变量（那永远找不完），而是把架构改对：**结论只在栈展开之后下**——
同步快路径 + 线程池三轮延迟判定，配合把卸载各步骤拆进 `[NoInlining]` 独立方法。
日志证实了机制按设计工作（`延迟判定：插件程序集已成功回收，无需重启即可生效`），
并且插件目录能被即时删除，构成独立佐证。

---

## 7. 使用示例

### 7.1 插件作者：从零到跑通（5 步）

```bash
# 1. 建工程（照抄 samples/HelloAction/HelloAction.csproj 的三个关键配置）
dotnet new classlib -n MyPlugin
#    然后把 csproj 里的 TargetFramework 改成 net8.0-windows，并确认三点：
#    · TargetFramework 不得高于 net8.0-windows10.0.19041.0
#    · ProjectReference 指向 StarPie.Plugin.Abstractions，必须 <Private>false</Private>
#    · 只引用 SDK 与 BCL，绝不引用 StarPie.dll

# 2. 实现入口
#    见 samples/HelloAction/HelloActionPlugin.cs

# 3. 写清单
#    见 samples/HelloAction/plugin.json（IDE 会依据 plugin.schema.json 给出补全）

# 4. 构建
dotnet build -c Release
#    产物目录本身就是可分发的插件文件夹：
#      StarPie.Plugin.MyPlugin.dll + deps.json + plugin.json
#      ★ 不应出现 StarPie.Plugin.Abstractions.dll

# 5. 端到端自检（无界面，CI 友好）
StarPie.exe --plugin-selftest "bin/Release/net8.0-windows/StarPie.Plugin.MyPlugin.dll" report.txt
```

最小入口实现：

```csharp
using StarPie.Plugin;

public sealed class HelloActionPlugin : IStarPiePlugin
{
    /// <summary>订阅凭据。这不是「礼貌性清理」，而是卸载的前提条件（详见 §4.4）。</summary>
    private readonly List<IDisposable> _subscriptions = new();

    public void Initialize(IPluginContext context)
    {
        // 词条写短键即可，宿主自动加 plugin.<pluginId>. 前缀，不会与内置词条撞车
        context.I18n.Register("action.greet.name", "打个招呼", "Say Hello");

        // 图标：注册后拿到的完整 key 直接填进 ActionDescriptor.IconKey
        string iconKey = context.Icons.RegisterSvg(
            "wave",
            "M7 11.5V5.2a1.6 1.6 0 0 1 3.2 0v5.6c0 4.4-2.2 8.2-6.4 8.2s-6.4-2.8-6.4-6.2v-4a1.6 1.6 0 0 1 3.2 0");

        context.Actions.Register(new GreetContribution(context, iconKey));

        // 每一次订阅都必须留住 token —— 不释放的话插件的 ALC 永远回收不掉
        _subscriptions.Add(context.Events.OnLanguageChanged(code =>
            context.Log.Info($"收到语言切换事件：{code}")));
    }

    // 必须幂等：宿主可能既做兜底撤销、又依赖插件自觉清理，两条路径都会调到这里
    public void Shutdown()
    {
        foreach (IDisposable subscription in _subscriptions)
        {
            try { subscription.Dispose(); } catch { }
        }
        _subscriptions.Clear();
    }
}
```

动作贡献的最小实现：

```csharp
internal sealed class GreetContribution(IPluginContext context, string iconKey) : IActionContribution
{
    public ActionDescriptor Descriptor => new()
    {
        Id = "greet",                            // 完整 ID 为 <pluginId>.greet
        DisplayName = "打个招呼",
        DisplayNameKey = "action.greet.name",    // 词条优先，找不到时回退 DisplayName，避免把 key 显示给用户
        Category = "示例插件",
        IconKey = iconKey,
        Kind = ActionKind.Sequential,            // 串行占动作线程；Background 走线程池并发
        TimeoutSeconds = 3,                      // 0 表示用宿主默认值（Sequential 5s / Background 30s）
    };

    public IReadOnlyList<ParameterField> Parameters =>
    [
        new ParameterField
        {
            Key = "message", Label = "问候语", LabelKey = "field.message.label",
            Type = ParameterFieldType.Text, Required = true,
            DefaultValue = "你好，StarPie！", MaxLength = 200,
            HelpText = "这段文字会显示在托盘气泡里。",
        },
        new ParameterField
        {
            Key = "showBalloon", Label = "显示托盘气泡", Type = ParameterFieldType.Bool,
            DefaultValue = "true",
        },
        new ParameterField
        {
            Key = "tone", Label = "语气", Type = ParameterFieldType.Enum, DefaultValue = "friendly",
            Options =
            [
                new ParameterOption { Value = "friendly", Label = "友好" },
                new ParameterOption { Value = "formal",   Label = "正式" },
            ],
        },
    ];

    // 返回 null 表示通过；返回文案会被宿主原样展示在出错提示里。
    // 宿主在调用前会执行它，异常也会被拦下并转为失败结果。
    public string? Validate(IReadOnlyDictionary<string, string> parameters) =>
        parameters.TryGetValue("message", out string? m) && !string.IsNullOrWhiteSpace(m)
            ? null
            : "问候语不能为空。";

    // 轮盘悬停时的实时提示。必须极快、禁止 IO；返回空串表示不显示。
    public string Preview(IReadOnlyDictionary<string, string> parameters) =>
        parameters.TryGetValue("message", out string? m) ? m : "";

    public Task<ActionResult> ExecuteAsync(PluginActionInput input, CancellationToken cancellationToken)
    {
        string message = input.Parameter("message") ?? "你好，StarPie！";

        // input.Context 是宿主在调用瞬间采集的前台环境快照，无需自己 P/Invoke
        context.Log.Info(
            $"greet 被触发：tone={input.Parameter("tone")}，前台进程={input.Context.ForegroundProcessName}");

        if (input.Bool("showBalloon"))
        {
            context.Notify.Notify("Hello 示例插件", message);
        }

        return Task.FromResult(ActionResult.Ok());
    }
}
```

### 7.2 用户：安装与使用

1. 托盘右键 → **打开 StarPie 控制台** → 「插件」页
2. **从文件安装…** → 选中插件 `.dll`
3. 查看识别结果：名称、作者、许可证、能力声明、贡献点、SHA256、签名状态 → 确认安装
4. 在插件列表里点「启用」→ 插件动作出现在动作选择器中
5. 把它们绑到轮盘扇区，之后与内置动作完全一致地使用
6. 停用即时生效；若某个插件写得不干净（未注销订阅/起了线程），会提示「重启后彻底回收」

---

## 8. 已知限制与后续接缝

### 8.1 当前版本的限制（有意为之）

| 限制 | 原因 |
|---|---|
| 不做 CLR 沙箱 | 进程内的完整信任模型。能力声明用于**如实告知用户**，不是强制拦截。真正的沙箱需要独立进程 + IPC，与「零延迟」红线冲突 |
| 取消是协作式的 | 无法强杀插件的同步死循环，只能标记 + 隔离。已在文档与代码注释中如实说明 |
| `GlobalHook` 能力首版禁用 | 要求插件改用 `IPluginEvents` 由宿主代发事件，避免多条钩子链 |
| `styles` / `presets` 贡献点为 P1 | 清单里可声明，当前宿主忽略并给出提示 |
| 依赖只做提示 | `dependencies` 不自动解析与安装 |
| 卸载可能需重启 | 取决于插件是否写干净；做不干净则诚实告知 |

### 8.2 尚未接入的宿主 UI（下一轮工作）

| 项 | 现状 |
|---|---|
| `SlotViewModel` 聚合插件动作 | 插件动作已能通过 `PluginHost.GetPluginActionItems()` 取得，但槽位编辑器的动作类型下拉尚未并入 |
| 插件管理页 | 目前只有 `--plugin-selftest` 这条无界面通道，可视化页面待建 |
| `AGENTS.md` / `CHANGELOG.md` | 尚未登记本次架构变更 |

> 自检通道的存在使这些 UI 接缝可以独立推进：内核已有端到端证据，UI 只是消费 `PluginHost` 的公开方法。
