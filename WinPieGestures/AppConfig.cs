using System;
using System.Collections.Generic;
using System.Reflection;

namespace WinPieGestures;

public class AppConfig
{
	public string Language { get; set; } = "Auto";

	public string TriggerButton { get; set; } = "RightButton";

	public TriggerConfig Trigger { get; set; } = new TriggerConfig();

	public double DragThreshold { get; set; } = 25.0;

	/// <summary>是否启用鼠标右键释放防抖。开启后，抬起需稳定保持指定毫秒数才结束轮盘并执行动作。</summary>
	public bool EnableMouseReleaseDebounce { get; set; } = true;

	/// <summary>鼠标右键释放稳定等待时间（毫秒），有效范围 1～100，默认 12。</summary>
	public int MouseReleaseDebounceMs { get; set; } = 12;

	/// <summary>控制台配置模式："Simple"（简单轻量模式，隐藏低频高级微调）或 "Pro"（高级全量模式，开放全部配置）。</summary>
	public string ConfigMode { get; set; } = "Simple";

	/// <summary>核心圆死区唤醒灵敏度（有效判定半径，像素）。光标在此半径内视为停留在中心核圆，可触发中心动作或静默取消。</summary>
	public double CoreDeadzoneRadius { get; set; } = 35.0;

	/// <summary>多层轮盘切换触发方式："Wheel"（鼠标滚轮上下滑动，推荐）、"Tab"（Tab 键切换）、"CustomKey"（自定义按键）。</summary>
	public string LayerSwitchTrigger { get; set; } = "Wheel";

	/// <summary>多层轮盘自定义按键触发的虚拟键码（当 LayerSwitchTrigger 为 "CustomKey" 时生效，默认 9 为 VK_TAB）。</summary>
	public uint LayerSwitchVkCode { get; set; } = 9;

	/// <summary>可选：长按触发按键（如右键）不动达到长按阈值后呼出轮盘，与拖动呼出共存。</summary>
	public bool LongPressTrigger { get; set; }

	/// <summary>长按响应时长（毫秒）。</summary>
	public double LongPressDelayMs { get; set; } = 450.0;

	// ---- 鼠标手势（画轨迹识别，最多三段图样）----
	public bool GestureEnabled { get; set; }

	/// <summary>手势触发键："RightButton"/"MiddleButton"/"XButton1"/"XButton2"。</summary>
	public string GestureTriggerButton { get; set; } = "MiddleButton";

	public List<GestureMapping> GestureMappings { get; set; } = new List<GestureMapping>();

	/// <summary>手势提示文字位置："Auto" 或 U/D/L/R/UL/UR/DL/DR（相对鼠标）。</summary>
	public string GestureHintPlacement { get; set; } = "Auto";

	/// <summary>手势段灵敏度：最小段长（像素）。越大越难把中途小拐弯误识别为方向段。</summary>
	public double GestureSegmentSensitivity { get; set; } = 16.0;

	/// <summary>取消（回到轮盘中心松手且未选中任何动作）后执行的自定义动作；默认关闭=仅关面板不执行。</summary>
	public bool EnableCancelAction { get; set; }

	public ActionItem CancelAction { get; set; } = new ActionItem { Type = "Hotkey", Name = "取消动作", Parameter = "" };

	/// <summary>平铺排除名单：进程 exe 名（不含扩展名），逗号/分号分隔。</summary>
	public string TileExcludeProcesses { get; set; } = "";

	/// <summary>平铺是否包含最小化窗口（true=还原后参与平铺）。</summary>
	public bool TileIncludeMinimized { get; set; }

	/// <summary>平铺"循环切换"参与范围：布局 key 逗号分隔（空=全部布局参与循环）。</summary>
	public string TileCycleLayouts { get; set; } = "";

	/// <summary>平铺屏幕边距：工作区四周留白（物理像素，0=贴边）。</summary>
	public int TileMarginTop { get; set; }
	public int TileMarginBottom { get; set; }
	public int TileMarginLeft { get; set; }
	public int TileMarginRight { get; set; }

	/// <summary>平铺窗口之间的间距（物理像素，0=紧贴）。</summary>
	public int TileGap { get; set; }

	public string AnimationSpeed { get; set; } = "Balanced";

	public double CustomAnimationDurationMs { get; set; } = 80.0;

	public bool EnableOuterEscapeCancel { get; set; }

	public double OuterEscapeDistance { get; set; } = 186.0;

	public string AppTheme { get; set; } = "System";

	/// <summary>设置控制台界面整体缩放比例（1.0 = 100%，有效范围 0.8 ~ 2.0，按 5% 步进对齐）。</summary>
	public double SettingsUiScale { get; set; } = 1.0;

	public string Theme { get; set; } = "Light";

	public string UiStyle { get; set; } = "Glassmorphism";
	public string SubmenuStyle { get; set; } = "Wheel";

	public bool EnableMultiTier { get; set; } = true;

	/// <summary>当启用外圈子环二级菜单时，呼出轮盘是否直接同时展开所有一二级轮盘（无需划出触发距离）。</summary>
	public bool AutoExpandSubRingsOnPopup { get; set; } = true;

	/// <summary>在设置控制台拖拽对调一级扇区时，是否连同其绑定的二级级联子动作一块换位（默认 true）。</summary>
	public bool LinkSubActionsWhenDragging { get; set; } = true;

	/// <summary>动作配置交互画布是否开启“图文并茂”复合展示（即使未配置自定义图标，也直观呈现动作名称文本，默认 true）。</summary>
	public bool MappingsCanvasShowText { get; set; } = true;

	/// <summary>手势动作页（Tab 2）右侧交互画布列宽度（0 表示自适应比例 1*，大于 0 表示用户自定义拖拽宽度）。</summary>
	public double MappingsCanvasColumnWidth { get; set; } = 0.0;

	/// <summary>是否开启应用专属方案继承/叠加 Global 全局方案（当应用方案槽位留空未配置时自动透传全局动作）。</summary>
	public bool EnableGlobalInheritance { get; set; } = true;

	/// <summary>当前正在使用的配置方案名称（如 "默认配置"、"CAD建模方案" 等），对应 AppData/Configs/<Name>.json。</summary>
	public string ActiveConfigProfileName { get; set; } = "默认配置";

	// ---- 轮盘交互音效系统 (Audio Haptic Feedback) ----
	/// <summary>是否开启轮盘交互音效（提供机械触觉/清脆盲操反馈，默认 true）。</summary>
	public bool EnableSoundEffects { get; set; } = true;

	/// <summary>交互音效全局音量（0.0 ~ 1.0，默认 0.6 即 60%）。</summary>
	public double SoundVolume { get; set; } = 0.6;

	/// <summary>音效主题风格："Mechanical"（机械手感）、"Crisp"（现代清脆）、"Bubble"（轻盈气泡）、"Minimalist"（极简短音）。默认 "Mechanical"。</summary>
	public string SoundTheme { get; set; } = "Mechanical";

	/// <summary>细项开关：轮盘呼出音效。</summary>
	public bool SoundOnPopup { get; set; } = true;

	/// <summary>细项开关：扇区切换高亮音效。</summary>
	public bool SoundOnHover { get; set; } = true;

	/// <summary>细项开关：二级子菜单展开音效。</summary>
	public bool SoundOnExpand { get; set; } = true;

	/// <summary>细项开关：动作执行确认音效。</summary>
	public bool SoundOnExecute { get; set; } = true;

	/// <summary>细项开关：外甩/脱离取消音效。</summary>
	public bool SoundOnCancel { get; set; } = true;


	public double SubWheelRadiusRatio { get; set; } = 1.55;

	public double SubWheelTriggerDistance { get; set; } = 95.0;
	/// <summary>音量拖距调音：缩回中心取消的迟滞系数（触发距离 × 该系数）。越大需缩回越多才取消，防止边缘抖动误取消。</summary>
	public double VolumeCancelHysteresisRatio { get; set; } = 0.6;

	/// <summary>音量拖距调音：甩出取消的距离下限（px）。低于此距离即使快速移动也不视为甩出。</summary>
	public double VolumeFlickFarDistance { get; set; } = 360.0;

	/// <summary>音量拖距调音：甩出取消的单帧距离跳变阈值（px）。超过此值视为快速甩动。</summary>
	public double VolumeFlickCancelDistance { get; set; } = 120.0;

	public double SubWheelOuterRadius { get; set; } = 210.0;

	public double SubWheelInnerGap { get; set; } = 4.0;

	public double SubWheelCornerRadius { get; set; } = 4.0;

	public double SubWheelIconSize { get; set; } = 18.0;

	public double SubWheelFontSize { get; set; } = 9.5;

	public bool UseIndependentSubWheelTheme { get; set; }

	public string SubWheelUiStyle { get; set; } = "ClassicRing";

	public string SubWheelTheme { get; set; } = "FollowPrimary";

	public string SubWheelCustomSectorBg { get; set; } = "#9016161A";

	public string SubWheelCustomSectorBorder { get; set; } = "#35FFFFFF";

	public string SubWheelCustomHighlightBg { get; set; } = "#E06C4DFF";

	public string SubWheelCustomHighlightBorder { get; set; } = "#A0FFFFFF";

	public string SubWheelCustomText { get; set; } = "#E0FFFFFF";

	public string SubWheelHighlightGlowPreset { get; set; } = "FollowPrimary";

	public string SubWheelHighlightGlowColor { get; set; } = "";

	public double SubWheelHighlightGlowRadius { get; set; } = 24.0;

	public double SubWheelHighlightGlowOpacity { get; set; } = 0.85;

	public bool AutoStartAsAdmin { get; set; }

	public bool ShowText { get; set; } = true;

	public bool ShowSelectedActionText { get; set; }

	// ==================== 多层轮盘指示徽标配置 ====================
	/// <summary>是否在切换轮盘层时显示浮动提示徽标（默认 true）</summary>
	public bool ShowLayerIndicator { get; set; } = true;

	/// <summary>层级指示徽标预设风格（"Dark", "Aurora", "Purple", "Light", "FollowTheme", "Custom"）</summary>
	public string LayerIndicatorStyle { get; set; } = "Dark";

	/// <summary>层级指示徽标背景底色（Hex 色值，默认 "#E60F172A"）</summary>
	public string LayerIndicatorBg { get; set; } = "#E60F172A";

	/// <summary>层级指示徽标边框色彩（Hex 色值，默认 "#38BDF8"）</summary>
	public string LayerIndicatorBorder { get; set; } = "#38BDF8";

	/// <summary>层级指示徽标文字与图标颜色（Hex 色值，默认 "#FFFFFF"）</summary>
	public string LayerIndicatorTextColor { get; set; } = "#FFFFFF";

	/// <summary>层级指示徽标前置图标（"🌟", "❄️", "🌀", "⚡", "🎯", "💎", "None"）</summary>
	public string LayerIndicatorIcon { get; set; } = "🌟";

	/// <summary>层级指示徽标字体字号（默认 11.5 px）</summary>
	public double LayerIndicatorFontSize { get; set; } = 11.5;

	/// <summary>层级指示徽标圆角半径（默认 12 px）</summary>
	public double LayerIndicatorCornerRadius { get; set; } = 12.0;

	/// <summary>层级指示徽标垂直显示偏移（默认 10 px）</summary>
	public double LayerIndicatorOffsetY { get; set; } = 10.0;

	/// <summary>层级指示徽标停留时长（毫秒，默认 1200ms）</summary>
	public double LayerIndicatorDurationMs { get; set; } = 1200.0;

	public double WheelRadius { get; set; } = 138.0;

	public double InnerRadius { get; set; } = 52.0;

	public double CoreRadius { get; set; } = 50.0;

	public string Shape { get; set; } = "Original";

	public double SectorGap { get; set; } = 2.0;

	public double SectorCornerRadius { get; set; } = 4.0;

	public string IconLayoutMode { get; set; } = "IconOnly";

	public string SectorTextPlacement { get; set; } = "Below";

	public double SectorTextOffsetX { get; set; } = 0.0;

	public double SectorTextOffsetY { get; set; } = 0.0;

	public string WheelFontFamily { get; set; } = "Microsoft YaHei UI, Segoe UI";

	public double SectorIconSize { get; set; } = 20.0;

	public double SectorFontSize { get; set; } = 11.0;

	public string CoreFontFamily { get; set; } = "Microsoft YaHei UI, Segoe UI";

	public double CoreFontSize { get; set; } = 13.0;

	public string CoreTextColor { get; set; } = "#FFFFFFFF";

	/// <summary>中心文字是否自动跟随配色主题取对比色；关闭后才使用 CoreTextColor 的手动指定色。</summary>
	public bool CoreTextColorAuto { get; set; } = true;

	public string CoreTitle { get; set; } = "StarPie";

	public string CoreSubtitle { get; set; } = "RMB Drag";

	public bool ShowCoreIcon { get; set; } = true;

	public string CoreIconType { get; set; } = "Exit";

	public string CoreCustomIconKey { get; set; } = "";

	public string CoreCustomIconSvg { get; set; } = "";

	public string CoreCustomImagePath { get; set; } = "";

	public string CoreCustomImageStretch { get; set; } = "UniformToFill";

	public double CoreIconScale { get; set; } = 1.0;

	public double CoreImageOffsetX { get; set; }

	public double CoreImageOffsetY { get; set; }

	public string HighlightGlowPreset { get; set; } = "Auto";

	public string HighlightGlowColor { get; set; } = "";

	public double HighlightGlowRadius { get; set; } = 24.0;

	public double HighlightGlowOpacity { get; set; } = 0.85;

	public string CustomSectorBg { get; set; } = "#F0F8FAFC";

	public string CustomSectorBorder { get; set; } = "#3064748B";

	public string CustomHighlightBg { get; set; } = "#FF2563EB";

	public string CustomHighlightBorder { get; set; } = "#FF60A5FA";

	public string CustomText { get; set; } = "#FF0F172A";

	public List<CustomColorPreset> CustomColorPresets { get; set; } = new List<CustomColorPreset>();

	public string WheelBgImagePath { get; set; } = "";

	public double WheelBgOpacity { get; set; } = 0.8;

	public string WheelBgStretch { get; set; } = "UniformToFill";

	public string CoreBgImagePath { get; set; } = "";

	public double CoreBgOpacity { get; set; } = 1.0;

	public string CoreBgStretch { get; set; } = "UniformToFill";

	public string HighlightTexturePath { get; set; } = "";

	public double HighlightTextureOpacity { get; set; } = 0.7;

	public List<WheelProfile> Profiles { get; set; } = new List<WheelProfile>();

	public string IsolationMode { get; set; } = "Blacklist";

	public List<string> BlacklistedProcesses { get; set; } = new List<string> { "mstsc.exe", "paint.exe" };

	public List<string> WhitelistedProcesses { get; set; } = new List<string>();

	public bool DisableOnCtrl { get; set; }

	public bool DisableOnShift { get; set; }

	public bool DisableOnAlt { get; set; }

	public bool DisableOnFullScreen { get; set; } = true;

	public bool AutoCheckUpdate { get; set; } = true;

	public string UpdateChannel { get; set; } = 
		System.Reflection.Assembly.GetExecutingAssembly().GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Contains("beta", System.StringComparison.OrdinalIgnoreCase) == true
		? "Beta"
		: "Stable";

	public string UpdateProxySource { get; set; } = "ghproxy";

	public string CustomProxyUrl { get; set; } = "";

	public string LastCheckUpdateTime { get; set; } = "";

	public string IgnoredVersion { get; set; } = "";

	/// <summary>平铺窗口高级设置卡片是否展开（默认收起折叠）</summary>
	public bool TileSettingsExpanded { get; set; } = false;

	/// <summary>是否启用屏幕边缘呼出智能防溢出与光标自动对齐（默认关闭）</summary>
	public bool EnableEdgeCollisionAvoidance { get; set; } = false;

	/// <summary>屏幕边缘呼出防溢出策略："ClampShift" (智能贴边安全防溢出 - 默认), "ScreenCenter" (屏幕中心呼出), "None" (原生不处理)</summary>
	public string EdgeOverflowPolicy { get; set; } = "ClampShift";

	/// <summary>屏幕边缘呼出 X 轴水平安全边距 (像素，默认 16px)</summary>
	public double EdgeSafeMarginX { get; set; } = 16.0;

	/// <summary>屏幕边缘呼出 Y 轴垂直安全边距 (像素，默认 16px)</summary>
	public double EdgeSafeMarginY { get; set; } = 16.0;

	/// <summary>屏幕边缘呼出安全边距 (像素，保留兼容)</summary>
	public double EdgeSafeMargin { get; set; } = 16.0;

	/// <summary>OCR 截屏文字识别引擎全局配置</summary>
	public OcrSettings OcrConfig { get; set; } = new OcrSettings();
}

public class OcrSettings
{
	/// <summary>OCR 服务提供商："Local" (Windows原生离线), "Ai" (OpenAI兼容多模态), "Cloud" (商业云端), "Custom" (自定义HTTP微服务)</summary>
	public string Provider { get; set; } = "Local";

	/// <summary>本地 OCR 首选语言（如 "zh-Hans", "zh-Hant", "en-US", "ja-JP"）</summary>
	public string LocalLanguage { get; set; } = "zh-Hans";

	/// <summary>AI 多模态大模型接口端点 (如 "https://api.openai.com/v1")</summary>
	public string AiEndpoint { get; set; } = "https://api.openai.com/v1";

	/// <summary>AI 多模态大模型密钥 (sk-...)</summary>
	public string AiApiKey { get; set; } = "";

	/// <summary>AI 模型名称 (如 "gpt-4o-mini", "Qwen/Qwen2.5-VL-72B-Instruct")</summary>
	public string AiModel { get; set; } = "gpt-4o-mini";

	/// <summary>AI 识别输出模式："text" (纯文字提取), "latex" (LaTeX公式), "markdown" (Markdown表格), "translate" (自动译为中文)</summary>
	public string AiPromptMode { get; set; } = "text";

	/// <summary>商业云端 OCR 服务商："Baidu", "Tencent", "Aliyun"</summary>
	public string CloudProvider { get; set; } = "Baidu";

	public string CloudApiKey { get; set; } = "";

	public string CloudSecretKey { get; set; } = "";

	/// <summary>自定义私有化 HTTP OCR 微服务接口 URL (如 "http://127.0.0.1:1224/api/ocr")</summary>
	public string CustomHttpUrl { get; set; } = "http://127.0.0.1:1224/api/ocr";

	public string CustomHttpFormat { get; set; } = "base64";

	/// <summary>识别完成后是否自动复制到系统剪贴板</summary>
	public bool AutoCopyToClipboard { get; set; } = true;

	/// <summary>识别完成后是否弹出结果悬浮窗口</summary>
	public bool ShowResultWindow { get; set; } = true;

	/// <summary>识别完成后是否直接在默认浏览器中搜索</summary>
	public bool SearchInBrowser { get; set; } = false;

	/// <summary>自动合并断句段落</summary>
	public bool MergeLines { get; set; } = true;

	/// <summary>自动去除中文字符间多余空格</summary>
	public bool RemoveSpacesBetweenCjk { get; set; } = true;
}

