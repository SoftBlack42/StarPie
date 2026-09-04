<div align="center">

<img src="./assets/cover.v3.png" width="110" height="110" alt="StarPie Logo" />

# StarPie (星盘)

### 轻量、快捷的 Windows 鼠标轮盘手势与效率工具

**Lightweight, Fast & Configurable Radial Pie Menu for Windows 10 / 11**

[![Release Version](https://img.shields.io/badge/Release-v1.6.8-2563EB.svg?style=flat-square&logo=github)](https://github.com/SoftBlack42/StarPie/releases)
[![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20(x64)-0078D4.svg?style=flat-square&logo=windows)](https://microsoft.com/windows)
[![.NET](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4.svg?style=flat-square&logo=dotnet)](https://dotnet.microsoft.com/)
[![License](https://img.shields.io/badge/License-MIT-10B981.svg?style=flat-square)](LICENSE)
[![Tests](https://img.shields.io/badge/Tests-19%2F19%20Passed-success.svg?style=flat-square&logo=pytest)](tests/)
[![Language](https://img.shields.io/badge/Language-zh--CN%20%7C%20zh--TW%20%7C%20en%20%7C%20ja-8B5CF6.svg?style=flat-square)](#i18n)
[![Co-Authored](https://img.shields.io/badge/Co--Authored%20with-AI%20Agent-6366F1.svg?style=flat-square&logo=openai)](#acknowledgements)

<br/>

**[简体中文](README.md)** • **[English](README_EN.md)**

<br/>

[🌟 功能概览](#highlights) • [🚀 快速开始](#download) • [✨ 功能特性](#features) • [🎨 外观定制](#visuals) • [🌐 多语言](#i18n) • [🛠️ 本地构建](#build) • [💡 开发故事与维护说明](#acknowledgements) • [📋 更新日志](CHANGELOG.md)

</div>

---

## <a id="intro"></a>📖 简介

**StarPie (星盘)** 是一款专为 Windows 10 / 11 打造的轻量级鼠标轮盘手势（Radial / Pie Menu）效率工具。

在日常使用或专业建模软件中，可通过**鼠标右键 / 中键 / 侧键或键盘触发键**呼出快捷轮盘，也可使用独立的轨迹手势直接执行动作。当前版本支持 4 / 8 / 12 方位轮盘、多级子动作、专属程序配置（Per-App Profiles）、窗口管理与平铺、屏幕 OCR、快捷键录制、网址与命令调度，以及更完整的视觉形态定制，帮助将高频操作转化为自然的肌肉记忆。

> 💡 **设计重点**：
> - **低资源占用**：基于原生 C# WPF 构建，无浏览器内核打包，后台常驻内存以轻量化为目标，典型空闲场景约 **3 ～ 8 MB**；
> - **低延迟响应**：基于 Win32 `WH_MOUSE_LL` 底层事件流，响应迅速，不影响鼠标正常右键点击；
> - **绿色便携**：提供独立单文件版（内置 .NET 运行时，解压即用），配置保存于本地 `config.json`；

<details open>
<summary><b>🎬 演示视频 / Video Demo </b></summary>
<br/>

<div align="center">
  <a href="https://www.bilibili.com/video/BV1XjtA6KEGL" target="_blank">
    <img src="./attachments/video_cover.png" width="700" alt="StarPie 演示视频" />
  </a>
  <p>
    <a href="https://www.bilibili.com/video/BV1XjtA6KEGL"><b>📺 点击前往 Bilibili 观看原声讲解与实机演示</b></a>
  </p>
</div>

</details>

---

## <a id="highlights"></a>🌟 v1.6.8 功能概览

StarPie `v1.6.8` 在原有快捷轮盘基础上，进一步整合了**轨迹手势、窗口管理、屏幕 OCR、多程序配置与深度外观定制**，并继续以轻量、低延迟和肌肉记忆为主要设计方向。

| 功能方向 | 当前能力 |
| :--- | :--- |
| **轮盘交互** | 4 / 8 / 12 扇区、中心核圆动作、多级子轮盘、蜂窝扇与外甩取消 |
| **轨迹手势** | 最多 3 段、8 方向组合、轨迹浮层、分段灵敏度与释放提示 |
| **动作与窗口** | 快捷键、程序、网址、文件夹、命令、系统控制，以及窗口切换、平铺、跨屏、置顶与透明度 |
| **屏幕 OCR** | Windows 本地离线识别、AI 视觉接口与自定义 HTTP OCR |
| **配置体验** | 双栏聚焦编辑、实时轮盘画布、扇区拖拽换位、运行窗口捕捉与紧凑全览列表 |
| **外观定制** | 多种轮盘形态、独立一二级主题、单扇区字体 / 图标 / 文字位置覆盖与屏幕边缘适配 |

> 📋 更完整的版本变化请查看 [CHANGELOG.md](CHANGELOG.md)。

---

## <a id="features"></a>✨ 功能特性

### 1. ⚡ 鼠标手势快速呼出与动作触发

- 按住鼠标右键滑动超过设定阈值即呼出轮盘，滑向目标扇区后松开按键即可触发对应动作（热键、打开程序、打开文件夹或系统功能）；
- 普通右键单击依然正常弹出原生右键菜单，互不冲突；
- 支持右键、中键、侧键、键盘单键与组合修饰键作为触发键，并可选择拖动越阈值或长按延时呼出；
- 录制快捷键时可临时独占并暂停全局热键，避免 `Win + D`、`Alt + Tab` 等系统组合被意外执行。

<div align="center">
  <img src="./attachments/第一张.gif" width="680" alt="鼠标手势快速呼出与动作触发演示" />
  <br/><br/>
  <img src="./attachments/按键组合触发录制.gif" width="680" alt="按键组合触发录制演示" />
</div>

---

### 2. 🌟 多级级联子轮盘

- **多级轮盘级联交互**：支持在任意扇区方位自由扩展 1~4 个二级子动作。光标划向扇区并在扇区内停留时，外环以弹性动画平滑展开二级子扇区，向外滑入即可极速触发。
- **一二级主题与配色完全独立定制**：支持单独调节各级尺寸、字号与图标排版；二级轮盘既可**一键同步主轮盘**，也可**完全独立定制专属风格与配色**；
- 支持外圈子环与蜂窝扇两种二级形态，加入迟滞保持和防抖判定，降低边界移动时的闪烁与误收起；
- 一级与二级动作均支持拖拽对调，并可选择拖动一级动作时是否同步交换其二级子动作。

<div align="center">
  <img src="./attachments/第三张.gif" width="680" alt="二级轮盘展示" />
  <br/><br/>
  <img src="./attachments/蜂窝扇.gif" width="680" alt="蜂窝扇二级轮盘展示" />
</div>

---
### 3. 🚀 顺势外甩脱离取消 (Outer Escape Cancel)

- 若划出手势后不想执行任何动作，无需反向拉回中心核圆；
- 只需顺势向外快速滑动脱离轮盘边缘，轮盘自动进入半透明安全取消状态，松开右键不触发任何动作；
- 支持在设置中开启/关闭，并可通过滑块微调外甩距离灵敏度（140px ~ 320px）；`v1.6.5` 起还可为外甩取消配置独立动作与常用预设，中心核圆取消仍可保持静默。

<div align="center">
  <img src="./attachments/外甩取消.gif" width="680" alt="顺势外甩脱离取消演示" />
</div>

---

### <a id="visuals"></a>4. 🎨 多种轮盘形态与风格预设
- **4 种几何形态**：经典紧凑扇区 (Original)、独立悬浮圆形 (Circle)、圆角胶囊 (Capsule)、蜂巢六边形 (HexagonHive)；
- **多套预设主题**：跟随系统、浅色模式、深色模式、液态毛玻璃、抹茶森林、冰川透蓝、莫兰迪柔灰；
- 右侧提供 **实时交互预览画布**，支持缩放、平移、复位、点击选中与拖拽换位，调节参数即时可见；`v1.6.8` 增加屏幕边缘防溢出策略与 X / Y 安全边距。

<div align="center">
  <img src="./attachments/主题样式展示.gif" width="680" alt="轮盘形态与主题风格切换演示" />
  <br/><br/>
  <img src="./attachments/样式展示.gif" width="680" alt="多几何形态与视觉布局展示" />
  <br/><br/>
  <img src="./attachments轮盘样式.gif" width="680" alt="预设主题风格与画布实时渲染演示" />
</div>

---

### 5. 🎨 自定义高级配色与预设重命名
- 独立折叠面板，支持微调扇区底色、高亮光晕、边框线条与文字颜色；
- 支持十六进制颜色输入、色盘选取与屏幕吸色；
- 支持将当前颜色保存为自定义预设，并支持一键重命名与删除预设。

<div align="center">
  <img src="./attachments/04_custom_colors.gif" width="680" alt="自定义高级配色与预设管理演示" />
   <br/><br/>
  <img src="./attachments/中心图案调节.gif" width="680" alt="中心图案调节演示" />
  <br/><br/>
  <img src="./attachments/扇区样式定制.gif" width="680" alt="扇区样式定制演示" />
</div>

---

### 6. 🖼️ 自定义矢量 / 图片图标导入
- 图标库支持直接导入本地 **SVG 矢量文件** 与 **PNG / ICO / JPG** 图片；
- 导入图标自动保存在本地配置目录，支持在所有扇区中自由选用，并支持自定义图标重命名与删除。

<div align="center">
  <img src="./attachments/05_custom_icons.gif" width="680" alt="自定义图标导入与管理演示" />
</div>

---

### 7. 🎯 4 / 8 / 12 扇区方位自适应
- **4 键方位**：上下左右大角度，适合盲操；
- **8 键方位**：经典 8 向均衡布局（默认）；
- **12 键方位**：高密度功能映射，适合多动作工作流；中心核圆也可配置独立动作，并支持唤醒死区灵敏度。

<div align="center">
  <img src="./attachments/06_sector_counts.gif" width="680" alt="4/8/12扇区分割自适应演示" />
</div>

---

### 8. 💼 多程序专属配置方案

- 支持针对 Chrome、VS Code、Photoshop、SolidWorks 等不同前台程序分别设置专属轮盘配置；
- StarPie 会根据当前活动程序自动匹配对应方案，没有专属方案时回退到全局配置；
- 支持配置方案的新建、复制、删除与一键重命名，方便复用和维护不同工作流。

<div align="center">
  <img src="./attachments/07_per_app_profiles.gif" width="680" alt="多程序专属方案演示" />
</div>

---

### 9. 🎛️ 动作配置工作区、应用快捷录入与拖拽编辑

- 动作页采用“配置方案 + 聚焦编辑卡片 + 实时轮盘画布”的双栏工作区，也保留适合快速查看多个动作的紧凑全览列表；
- 提供智能应用选择器，可汇总已安装程序，并支持名称搜索与快速过滤；
- 提供运行窗口捕捉器，可直接选择当前桌面上的窗口或进程，减少手动查找可执行文件路径；
- 可直接点击画布选择目标扇区，并拖拽交换一级扇区、二级动作和中心核圆的位置；
- 中心核圆可启用独立动作，支持死区松开触发、常用预设及独立文字和图标排版；
- 每个扇区可独立覆盖布局模式、字体、字号、文字颜色、图标大小、文字位置及 X / Y 偏移。

<div align="center">
   <img src="./attachments/程序拖拽配置界面.gif" width="680" alt="程序拖拽配置界面演示" />
    <br/><br/>
  <img src="./attachments/07_1.gif" width="680" alt="应用程序智能检索与动作配置" />
</div>
---

### 10. 🛡️ 场景隔离、全屏防误触与多语言

- **全屏与游戏检测**：运行全屏独占应用或游戏时自动放行原生右键；
- **修饰键穿透**：支持按住 Ctrl / Shift / Alt 时绕过轮盘；
- **黑名单支持**：支持将指定进程加入排除名单；
- **多语言热切换**：内置简体中文、繁体中文、English、日本語，切换即时生效；`ScreenHelper` 统一处理多显示器、混合 DPI 与屏幕边缘坐标，减少副屏唤起漂移和轮盘溢出。

<div align="center">
  <img src="./attachments/08_settings_and_i18n.gif" width="680" alt="防误触与多语言设置演示" />
  <br/><br/>
  <img src="./attachments/边缘呼出防溢出.gif" width="680" alt="边缘呼出防溢出配置" />
</div>

---

### 11. ➡️ 独立轨迹手势与可视化提示

- 可为轨迹手势单独指定右键、中键或侧键，与轮盘触发键并行使用；
- 支持最多 3 段、8 方向的轨迹组合，通过短段过滤和相邻同向合并减少快速绘制时的微小抖动误判；
- 绘制时显示透明轨迹浮层、起点和释放提示，可调节分段灵敏度及提示文字方位；
- 未达到拖动阈值的轻点会回放为原生鼠标点击，不影响日常操作。

<div align="center">
<img src="./attachments/按键组合触发录制.gif" width="680" alt="按键组合触发录制演示" />
</div>

---

### 12. 🧰 更完整的动作类型体系

| 动作分类 | 主要用途 |
| :--- | :--- |
| **快捷热键** | 录制或拼装组合键，支持主按键搜索、Pause / Break 与独占录入 |
| **启动程序** | 启动 EXE、快捷方式或文件，可携带参数并选择常规权限启动 |
| **打开网址** | 使用系统默认、Chrome、Edge、Firefox 或自定义浏览器打开网址 |
| **打开文件夹** | 打开本地路径及桌面、下载等系统虚拟目录 |
| **运行命令** | 支持 CMD、PowerShell、WSL 及隐藏终端模式 |
| **屏幕 OCR** | 框选屏幕区域并调用本地、AI 或自定义 HTTP 识别 |
| **窗口管理** | 切换窗口、平铺、跨屏移动、置顶与透明度控制 |
| **系统控制** | 锁屏、音量、媒体、任务视图、虚拟桌面等系统功能 |

动作执行与显示图标已经解耦，同一动作可独立选择内置矢量图标、程序图标或自定义图片，不再被动作类型限制。

---

### 13. 🪟 窗口切换、管理与多种平铺布局

- **切换任务栏窗口**：按当前任务栏可见顺序绑定第 N 个运行窗口，图标、标题与激活目标使用同一快照；
- **窗口平铺**：内置左右、上下、主从、网格、单窗、横向堆叠、纵向堆叠、Columns、BSP、Auto Grid 等多种布局；
- 支持循环前进 / 后退布局、恢复平铺前位置、包含最小化窗口以及自定义进程排除名单；
- 可将当前活动窗口移动到下一显示器、切换总在最前或调整窗口透明度。

<div align="center">
  <img src="./attachments/进程窗口切换.png" width="680" alt="进程窗口切换演示" />
</div>

---

### 14. 📝 Windows 原生 OCR 与可扩展识别接口
- 框选任意屏幕区域后执行文字识别，支持 Windows 10 / 11 原生 `Windows.Media.Ocr` 离线引擎；
- 可选 AI 视觉接口或自定义 HTTP OCR 服务，并提供语言包环境诊断与配置面板；
- 识别结果支持自动复制、结果窗口展示、合并行、处理中日韩间空格以及浏览器搜索。

<div align="center">
  <img src="./attachments/OCR.gif" width="680" alt="OCR演示" />
    <br/><br/>
   <img src="./attachments/OCR接口配置.png" width="680" alt="OCR接口配置" />
</div>

---

### 15. 🔄 在线更新、诊断日志与贡献者信息

- 内置 GitHub Releases 更新检查，可选择更新通道、代理源、自定义代理并忽略指定版本；
- 系统日志采用后台队列异步写入，可从设置中快速打开日志目录和当日日志；
- 贡献者卡片默认使用离线名单，不在启动时主动联网，仅在用户手动刷新或检查更新时请求最新数据。

<div align="center">
  <img src="./attachments/系统内置更新与贡献展示.gif" width="680" alt="系统内置更新与贡献展示演示" />
</div>

---

## <a id="download"></a>🚀 快速开始与下载

### 当前主线版本：`v1.6.8`

| 版本包 | 适用场景 | 说明 | 下载入口 |
| :--- | :--- | :--- | :--- |
| **独立免安装单文件版 (推荐)** | 所有用户 | 内置 .NET 运行时，解压即可运行 | [⬇️ 下载 StarPie.exe (Standalone)](https://github.com/SoftBlack42/StarPie/releases) |
| **轻量便携版** | 已安装 .NET 8 运行时的用户 | 体积小巧，绿色便携 | [⬇️ 下载 StarPie 便携包](https://github.com/SoftBlack42/StarPie/releases) |
| **历史版本归档** | 版本回溯与对比 | 历史版本的二进制文件与说明 | [📂 浏览 Releases 归档](https://github.com/SoftBlack42/StarPie/releases) |

### 基础使用流程：
1. 下载并运行 `StarPie.exe`，程序会在系统托盘中后台运行；
2. 双击托盘图标或右键选择「偏好设置」，在「触发与场景」中录制轮盘触发键；
3. 按住触发键拖动超过阈值，或按配置长按，即可在光标附近唤出轮盘；
4. 滑至目标扇区后松开触发键执行动作，向外甩出则可取消或执行自定义取消动作；
5. 如需轨迹手势，可启用独立手势触发键，并在动作页映射 1～3 段方向组合。

---

## <a id="i18n"></a>🌐 多语言支持 (Internationalization)

可在设置页面的「⚙️ 高级与系统」中随时切换界面语言：

| 语言代码 | 显示名称 | 支持状态 |
| :--- | :--- | :---: |
| `zh-CN` | 🇨🇳 简体中文 | 🟢 完整支持 |
| `zh-TW` | 🇭🇰/🇹🇼 繁體中文 | 🟢 完整支持 |
| `en` | 🇺🇸 English | 🟢 完整支持 |
| `ja` | 🇯🇵 日本語 | 🟢 完整支持 |
| `Auto` | 🖥️ 跟随操作系统语言 | 🟢 完整支持 |

---

## <a id="build"></a>🛠️ 本地构建与开发

### 环境要求
- Windows 10 / 11 (x64)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- Python 3.10+ (仅运行自动化测试套件需要)

### 编译与运行
```bash
# 1. 克隆代码仓库
git clone https://github.com/SoftBlack42/StarPie.git
cd StarPie

# 2. 编译项目 (Release)
dotnet build WinPieGestures/WinPieGestures.csproj -c Release

# 3. 运行项目
dotnet run --project WinPieGestures/WinPieGestures.csproj

# 4. 发布轻量版（需要目标电脑安装 .NET 8 Desktop Runtime）
dotnet publish WinPieGestures/WinPieGestures.csproj -c Release -r win-x64 --no-self-contained -o releases/local/Lightweight

# 5. 发布独立版（自带 .NET 运行时）
dotnet publish WinPieGestures/WinPieGestures.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o releases/local/Standalone
```

### 运行自动化测试（当前包含 19 项 GUI 用例）
```bash
# 安装测试依赖
pip install pytest pywinauto

# 执行测试
python -m pytest tests/test_settings.py -v
```

---

## <a id="structure"></a>📂 项目结构

```text
StarPie/
├── .github/                         # CI 工作流与社区配置
├── WinPieGestures/                  # 核心工程（C# / .NET 8 / WPF）
│   ├── ActionExecutor.cs            # 热键、程序、网址、命令、系统动作调度
│   ├── ActionItem.cs                # 动作模型与单扇区排版覆盖
│   ├── MouseHook.cs                 # Win32 低级鼠标 Hook 专用线程
│   ├── KeyboardHook.cs              # Win32 低级键盘 Hook 与独占录制
│   ├── GestureController.cs         # 轮盘与轨迹手势状态机
│   ├── GestureMapping*.cs           # 轨迹组合映射与配置模型
│   ├── GestureTrailOverlay.cs       # 轨迹绘制和释放提示浮层
│   ├── RadialWindow.xaml(.cs)       # 轮盘透明窗口与运行时渲染
│   ├── SettingsWindow.xaml(.cs)     # 双栏画布、聚焦编辑与系统设置
│   ├── WindowTaskbarHelper.cs       # 任务栏顺序、窗口图标与切换快照
│   ├── WindowTiler.cs               # 窗口平铺、恢复、循环与跨屏控制
│   ├── WindowPickerWindow.xaml(.cs) # 活动窗口和进程捕捉器
│   ├── ScreenHelper.cs              # 多显示器、DPI 与边缘坐标处理
│   ├── ScreenSnipWindow.xaml(.cs)   # OCR 截屏区域选择
│   ├── OcrManager.cs                # 本地、AI 与 HTTP OCR 调度
│   ├── OcrSettingsDialog.xaml(.cs)  # OCR 引擎和接口设置
│   ├── OcrResultWindow.xaml(.cs)    # OCR 结果展示
│   ├── UpdateManager.cs             # GitHub Releases 更新检查
│   ├── AppLogger.cs                 # 异步运行日志
│   ├── ConfigManager.cs             # 配置持久化、导入导出与自启
│   ├── IconHelper.cs                # 内置 / 程序 / 自定义图标解析
│   └── WinPieGestures.csproj        # .NET 8 WPF 项目配置
├── releases/                        # 历史版本与发布归档
├── attachments/                     # README 截图、GIF 与待补演示素材
├── tests/                           # pywinauto GUI 自动化测试
├── CHANGELOG.md                     # 完整版本更新日志
├── CONTRIBUTING.md                  # 贡献指南
├── LICENSE                          # MIT 许可证
└── README.md                        # 中文主文档
```

---

## <a id="acknowledgements"></a>💡 开发故事与维护说明

### 🌟 灵感来源
本人是机械设计制造及其自动化专业的一名学生。在日常三维建模中经常使用 SolidWorks，觉得其内置的鼠标手势轮盘十分便利。

在接触到 AI Agent 辅助开发工具后，萌生了将这种轮盘操作迁移到 Windows 桌面全局的想法，希望能以此提升日常办公与操作的便利性。对于此前未接触过手势轮盘的朋友，这或许也是一种新颖高效的交互体验。

虽然开源社区已有同类轮盘项目，但在功能侧重点和交互细节上各有不同。从最初构想到发布，中途因学业课程与竞赛有所间断，与 AI Agent 协作断断续续历时约一周完成了当前版本。

项目中若有不够完善或考虑不周之处，还请多包涵。欢迎通过 GitHub Issue 提交 Bug 报告、使用反馈或改进建议！

### 🤖 人机协同开发说明
本项目由开发者主导架构设计、交互逻辑规划与系统调优，并由 AI 智能体（**AI Agent - Antigravity**）及开源贡献者协同完成代码构建、多语言支持、交互优化与 GUI 自动化测试验证。

### 📌 阶段性维护说明
- **当前状态**：截至 2026-09-04，`main` 已更新至 StarPie `v1.6.8`，轮盘、轨迹手势、窗口管理、OCR 与配置工作流仍在持续完善；
- **后续节奏**：项目继续采用开发者、社区贡献者与 AI Agent 协同迭代的方式维护，欢迎通过 Issue / Pull Request 提交功能建议、兼容性反馈与演示素材。

---

## <a id="license"></a>📄 开源许可证

本项目采用 [MIT License](LICENSE) 开源。
