using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace WinPieGestures;

public class GestureController : IDisposable
{
	private struct POINT
	{
		public int x;

		public int y;
	}

	private readonly MouseHook _mouseHook;

	private readonly KeyboardHook? _keyboardHook;

	private RadialWindow? _radialWindow;

	private Point _startPoint;

	// 手势起点所在显示器的 DPI 缩放系数,用于将物理像素位移归一化为 DIP,
	// 保证多显示器混合 DPI 环境下滑动阈值与扇区命中判定一致。
	private double _currentDpiScaleX = 1.0;

	private double _currentDpiScaleY = 1.0;

	private volatile bool _isWaitingForThreshold;

	private volatile bool _isGestureActive;

	// 长按触发（可选）：钩子跑在独立后台线程，用 System.Threading.Timer（不依赖线程 Dispatcher），
	// 代数(Generation)防止旧回调触发到新手势；激活经主线程 Dispatcher 执行。
	private System.Threading.Timer? _longPressTimer;

	private int _longPressGeneration;

	private readonly object _longPressLock = new object();

	private bool _mouseTriggerDown;

	// 鼠标右键释放防抖：UP 先进入短暂稳定等待，若窗口内再次收到 DOWN，
	// 则判定为微动抖动并继续当前手势，不关闭轮盘、不执行动作。
	private readonly object _mouseReleaseDebounceLock = new object();

	private System.Threading.Timer? _mouseReleaseDebounceTimer;

	private int _mouseReleaseDebounceGeneration;

	private bool _mouseReleasePending;

	private Point _pendingMouseReleasePosition;

	private string _pendingMouseReleaseButton = "RightButton";

	// ---- 键盘触发穿透模式 ----
	// 触发键 KeyDown/KeyUp 一律原生放行，仅用持握时长+拖动阈值唤出轮盘。
	// _kbTriggerWaiting 区分"键盘触发正在等待阈值"与鼠标触发的等待态（后者仍需吞键）。
	private volatile bool _kbTriggerWaiting;

	// 触发键按下时刻（Environment.TickCount64），用于 200ms 持握门槛，防止快速敲击+甩动误唤轮盘。
	private long _kbTriggerDownTick;

	private const int KeyboardTriggerMinHoldMs = 200;

	// ---- 鼠标手势（画轨迹识别；延迟分段缓冲：短段过滤 + 相邻同向合并 + 完全匹配才触发）----
	private bool _gestureMode;

	private bool _gestureWaiting;

	private bool _gestureTracking;

	private Point _gesturePressPoint;

	private Point _gestureLastSample;

	private readonly List<(int dir, double len)> _gestureRuns = new List<(int, double)>();

	private int _gesturePendingDir = -1;

	private double _gesturePendingLen;

	// 手势轨迹浮层（可视化）
	private GestureTrailOverlay? _trail;

	private double _trailLastX = double.NaN;

	private double _trailLastY = double.NaN;

	private double _trailScaleX = 1.0;

	private double _trailScaleY = 1.0;

	/// <summary>轻点回放后，下一次该键事件放行（SendInput 重放的按下需穿透给应用）。</summary>
	private bool _gestureReplayPending;

	private WheelProfile? _activeProfile;

	private int _selectedSectorIndex = -1;

	private int _selectedSubSectorIndex = -1;

	private bool _lastEscapedState;

	private bool _lastShowSubTier;

	// Mouse hooks can outpace WPF rendering. Keep one pending visual update and
	// overwrite it with the newest state instead of queueing every intermediate
	// sector transition on the UI dispatcher.
	private readonly object _uiUpdateSync = new object();

	private long _gestureVersion;

	private bool _highlightUpdateScheduled;

	private int _pendingSectorIndex = -1;

	private int _pendingSubSectorIndex = -1;

	private bool _pendingEscape;

	private bool _pendingShowSubTier;

	private long _pendingGestureVersion;

	// ---- 音量"拖距调音"（次级轮盘）：音量加/减扇区越过子轮盘触发距离后，
	//      以拖出距离增量映射系统音量并实时写盘；外甩取消恢复基准音量 ----
	private bool _volumeAdjustActive;

	private bool _volumeGestureTookOver;

	private float _volumeBaseline = -1f;

	private double _volumeBaselineDist;

	private float _volumeLastPercent = -1f;

	// Last system OSD (volume flyout) refresh tick; throttled to avoid flicker on fast drags
	private long _volumeLastOsdTick;

	// Sector locked at the moment the volume adjust takes over. Once active, small angle
	// drift into a neighbouring (non-volume) sector must never cancel the ongoing adjust.
	private int _volumeLockedSector = -1;
	// Distance of the previous processed frame while adjusting. A sudden large
	// positive jump is treated as an intentional flick-out and cancels to baseline.
	private double _volumeFlickPrevDist = -1.0;
	// Latches true when this gesture was cancelled by a flick-out. A cancelled gesture
	// must not re-enter adjust while the trigger button is still held, otherwise the
	// volume snaps back to baseline and is immediately dragged again in the same press.
	private bool _volumeFlickCancelled;
	// Over-travel past a pinned 0%/100% volume: -1.0 while not pinned. Once the clamp
	// kicks in this stores the travel at that instant, so a continued outward drag
	// beyond the over-travel cancel distance aborts the gesture and restores baseline.
	private double _volumeMaxedOutDist = -1.0;

	private bool _volumePreviewScheduled;

	private int _pendingVolumePercent = -1;

	private long _pendingVolumePreviewVersion;

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool GetCursorPos(out POINT lpPoint);

	[DllImport("user32.dll")]
	[return: MarshalAs(UnmanagedType.Bool)]
	private static extern bool SetCursorPos(int X, int Y);

	[DllImport("user32.dll")]
	private static extern nint WindowFromPoint(POINT Point);

	[DllImport("user32.dll")]
	private static extern nint GetAncestor(nint hwnd, uint gaFlags);
	private const uint GA_ROOT = 2;

	[DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
	private static extern int GetClassName(nint hWnd, StringBuilder lpClassName, int nMaxCount);

	/// <summary>
	/// 检测物理坐标是否位于 Windows 任务栏、辅助屏任务栏、托盘通知区或溢出窗口之上。
	/// 避免星盘低级鼠标钩子截断任务栏右键菜单、跳转列表 (JumpList) 或托盘图标原生点击。
	/// </summary>
	private static bool IsPointOnTaskbar(Point physicalPt)
	{
		try
		{
			POINT pt = new POINT { x = (int)Math.Round(physicalPt.X), y = (int)Math.Round(physicalPt.Y) };
			nint hWnd = WindowFromPoint(pt);
			if (hWnd == IntPtr.Zero)
			{
				return false;
			}
			nint rootHwnd = GetAncestor(hWnd, GA_ROOT);
			if (rootHwnd == IntPtr.Zero)
			{
				rootHwnd = hWnd;
			}

			StringBuilder sb = new StringBuilder(128);
			if (GetClassName(rootHwnd, sb, 128) > 0)
			{
				string rootClass = sb.ToString();
				if (string.Equals(rootClass, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(rootClass, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(rootClass, "NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase) ||
				    string.Equals(rootClass, "TopLevelWindowForOverflowXamlIsland", StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
			}

			if (rootHwnd != hWnd)
			{
				sb.Clear();
				if (GetClassName(hWnd, sb, 128) > 0)
				{
					string childClass = sb.ToString();
					if (string.Equals(childClass, "Shell_TrayWnd", StringComparison.OrdinalIgnoreCase) ||
					    string.Equals(childClass, "Shell_SecondaryTrayWnd", StringComparison.OrdinalIgnoreCase) ||
					    string.Equals(childClass, "NotifyIconOverflowWindow", StringComparison.OrdinalIgnoreCase) ||
					    string.Equals(childClass, "TrayNotifyWnd", StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}
			return false;
		}
		catch
		{
			return false;
		}
	}

	public GestureController(MouseHook mouseHook, KeyboardHook? keyboardHook = null)
	{
		_mouseHook = mouseHook;
		_keyboardHook = keyboardHook;
		_mouseHook.OnTriggerButtonDown += Hook_OnTriggerButtonDown;
		_mouseHook.OnTriggerButtonUp += Hook_OnTriggerButtonUp;
		_mouseHook.OnMouseMove += Hook_OnMouseMove;
		_mouseHook.OnRawMouseButtonEvent += Hook_OnRawMouseButton;
		_mouseHook.OnMouseWheel += Hook_OnMouseWheel;
		if (_keyboardHook != null)
		{
			_keyboardHook.OnKeyDown += KeyboardHook_OnKeyDown;
			_keyboardHook.OnKeyUp += KeyboardHook_OnKeyUp;
		}
	}

	private long BeginGestureTracking()
	{
		lock (_uiUpdateSync)
		{
			_gestureVersion++;
			_highlightUpdateScheduled = false;
			_pendingSectorIndex = -1;
			_pendingSubSectorIndex = -1;
			_pendingEscape = false;
			_pendingShowSubTier = false;
			_pendingGestureVersion = _gestureVersion;
			_selectedSectorIndex = -1;
			_selectedSubSectorIndex = -1;
			_lastEscapedState = false;
			_lastShowSubTier = false;
			_volumeAdjustActive = false;
			_volumeGestureTookOver = false;
			_volumeBaseline = -1f;
			_volumeBaselineDist = 0.0;
			_volumeLastPercent = -1f;
			_volumeLastOsdTick = 0L;
			_volumeLockedSector = -1;
			_volumeFlickPrevDist = -1.0;
			_volumeFlickCancelled = false;
			_volumeMaxedOutDist = -1.0;
			_volumePreviewScheduled = false;
			_pendingVolumePercent = -1;
			_pendingVolumePreviewVersion = 0L;
			return _gestureVersion;
		}
	}

	private void CancelGestureTracking()
	{
		_kbTriggerWaiting = false;
		_isWaitingForThreshold = false;
		_isGestureActive = false;
		lock (_uiUpdateSync)
		{
			_gestureVersion++;
			_highlightUpdateScheduled = false;
			_pendingGestureVersion = _gestureVersion;
		}
		HideRadialUI();
		ActionExecutor.ReleaseStuckModifiers();
	}

	private (int Sector, int SubSector, WheelProfile? Profile, RadialWindow? Window, bool IsEscaped, long PresentationVersion) EndActiveGesture()
	{
		lock (_uiUpdateSync)
		{
			long presentationVersion = _gestureVersion;
			var result = (_selectedSectorIndex, _selectedSubSectorIndex, _activeProfile, _radialWindow, _lastEscapedState, presentationVersion);
			_isGestureActive = false;
			_isWaitingForThreshold = false;
			_gestureVersion++;
			_highlightUpdateScheduled = false;
			_pendingGestureVersion = _gestureVersion;
			ActionExecutor.ReleaseStuckModifiers();
			return result;
		}
	}

	private bool IsCurrentGesture(long version)
	{
		lock (_uiUpdateSync)
		{
			return version == _gestureVersion;
		}
	}

	private long GetCurrentGestureVersion()
	{
		lock (_uiUpdateSync)
		{
			return _gestureVersion;
		}
	}

	private void QueueHighlightUpdate(int sectorIndex, int subSectorIndex, bool isEscaped, bool showSubTier, long gestureVersion)
	{
		bool shouldSchedule;
		lock (_uiUpdateSync)
		{
			if (!_isGestureActive || gestureVersion != _gestureVersion)
			{
				return;
			}
			if (sectorIndex == _selectedSectorIndex &&
				subSectorIndex == _selectedSubSectorIndex &&
				isEscaped == _lastEscapedState &&
				showSubTier == _lastShowSubTier)
			{
				return;
			}

			int prevSector = _selectedSectorIndex;
			int prevSubSector = _selectedSubSectorIndex;
			bool prevEscaped = _lastEscapedState;
			bool prevShowSubTier = _lastShowSubTier;

			_selectedSectorIndex = sectorIndex;
			_selectedSubSectorIndex = subSectorIndex;
			_lastEscapedState = isEscaped;
			_lastShowSubTier = showSubTier;
			_pendingSectorIndex = sectorIndex;
			_pendingSubSectorIndex = subSectorIndex;
			_pendingEscape = isEscaped;
			_pendingShowSubTier = showSubTier;
			_pendingGestureVersion = gestureVersion;
			shouldSchedule = !_highlightUpdateScheduled;
			_highlightUpdateScheduled = true;

			// 音效反馈触发：扇区切换、二级展开与外甩取消
			if (!prevEscaped && isEscaped)
			{
				SoundEffectManager.Play(SoundType.GestureCancel);
			}
			else if (!prevShowSubTier && showSubTier)
			{
				SoundEffectManager.Play(SoundType.SubmenuExpand);
			}
			else if (showSubTier && prevSubSector != subSectorIndex && subSectorIndex >= 0)
			{
				SoundEffectManager.Play(SoundType.SectorHover);
			}
			else if (prevSector != sectorIndex && sectorIndex >= 0)
			{
				SoundEffectManager.Play(SoundType.SectorHover);
			}
		}

		if (!shouldSchedule)
		{
			return;
		}

		try
		{
			Application.Current.Dispatcher.BeginInvoke((Action)ApplyPendingHighlight, DispatcherPriority.Render);
		}
		catch
		{
			lock (_uiUpdateSync)
			{
				if (_pendingGestureVersion == gestureVersion)
				{
					_highlightUpdateScheduled = false;
				}
			}
		}
	}

	private void ApplyPendingHighlight()
	{
		int targetSector;
		int targetSubSector;
		bool targetEscape;
		bool targetShowSubTier;
		long targetGestureVersion;
		RadialWindow? radialWindow;

		lock (_uiUpdateSync)
		{
			if (!_highlightUpdateScheduled)
			{
				return;
			}
			targetSector = _pendingSectorIndex;
			targetSubSector = _pendingSubSectorIndex;
			targetEscape = _pendingEscape;
			targetShowSubTier = _pendingShowSubTier;
			targetGestureVersion = _pendingGestureVersion;
			if (!_isGestureActive || targetGestureVersion != _gestureVersion)
			{
				_highlightUpdateScheduled = false;
				return;
			}
			radialWindow = _radialWindow;
			// Keep the pending state until the activation callback creates the
			// window. That callback calls this method again after Show().
			if (radialWindow == null)
			{
				return;
			}
			_highlightUpdateScheduled = false;
		}

		if (!IsCurrentGesture(targetGestureVersion) || !ReferenceEquals(_radialWindow, radialWindow))
		{
			return;
		}
		radialWindow.SetOuterEscapeState(targetEscape);
		radialWindow.HighlightSector(targetSector, targetSubSector, targetShowSubTier);
	}

	// 判断指定扇区是否为系统音量加/减动作（用于外甩豁免：音量扇区在任意拖距都可调音）
	private bool IsVolumeSector(int sectorIndex)
	{
		if (_activeProfile == null || sectorIndex < 0)
		{
			return false;
		}
		ActionItem? action = _activeProfile.GetEffectiveAction(sectorIndex);
		if (action == null)
		{
			return false;
		}
		// 若该动作配置了二级级联子动作，首要用途为展开二级轮盘，绝不被误判为音量调音扇区
		if (action.SubActions != null && action.SubActions.Count > 0)
		{
			return false;
		}
		bool isSystem = string.Equals(action.Type, "System", StringComparison.OrdinalIgnoreCase);
		return isSystem && (string.Equals(action.Parameter, "volumeup", StringComparison.OrdinalIgnoreCase)
			|| string.Equals(action.Parameter, "volumedown", StringComparison.OrdinalIgnoreCase));
	}
	// 音量"拖距调音"：当前扇区为音量加/减且拖距越过子轮盘触发距离时接管为距离映射调音。
	// 以进入时的系统音量为基准，拖出距离增量映射音量（加音量向外=升，减音量向外=降），
	// 实时写系统音量；外甩取消或缩回触发距离内恢复基准音量。
	// 返回 true 表示本帧已接管音量调节（调用方应屏蔽二级子轮盘）。
	private bool ProcessVolumeAdjust(double dist, int sectorIndex, bool isEscaped, double subWheelTriggerDistance)
	{
		// Sector lock: once adjusting, keep the sector that started the adjust so angle drift
		// into a neighbouring non-volume sector cannot cancel a long drag. The cancel rule
		// below (escape or return inside the hysteresis radius) is the only way out.
		int refSector = (_volumeAdjustActive && _volumeLockedSector >= 0) ? _volumeLockedSector : sectorIndex;
		if (refSector < 0)
		{
			if (_volumeAdjustActive)
			{
				_volumeAdjustActive = false;
				_volumeLockedSector = -1;
				_volumeFlickPrevDist = -1.0;
				QueueVolumePreview(-1, GetCurrentGestureVersion());
			}
		return false;
		}
		ActionItem? action = (_activeProfile != null && refSector >= 0)
			? _activeProfile.GetEffectiveAction(refSector)
			: null;
		// 若该动作配置了二级级联子动作，首要用途为展开二级轮盘，绝不接管为音量拖拽调音
		if (action != null && action.SubActions != null && action.SubActions.Count > 0)
		{
			if (_volumeAdjustActive)
			{
				_volumeAdjustActive = false;
				_volumeLockedSector = -1;
				_volumeFlickPrevDist = -1.0;
				QueueVolumePreview(-1, GetCurrentGestureVersion());
			}
			return false;
		}
		bool isSystem = action != null && string.Equals(action.Type, "System", StringComparison.OrdinalIgnoreCase);
		bool isUp = isSystem && string.Equals(action.Parameter, "volumeup", StringComparison.OrdinalIgnoreCase);
		bool isDown = isSystem && string.Equals(action.Parameter, "volumedown", StringComparison.OrdinalIgnoreCase);
		if (!isUp && !isDown)
		{
			if (_volumeAdjustActive)
			{
				_volumeAdjustActive = false;
				_volumeFlickPrevDist = -1.0;
				QueueVolumePreview(-1, GetCurrentGestureVersion());
			}
			return false;
		}
		int direction = isUp ? 1 : -1;
		// 距离判定阈值来自设置界面（外观 → 音量拖距调音），带默认值兜底以防配置损坏：
		// cancelRatio = 缩回取消迟滞系数；flickFar = 甩出取消距离下限；flickJump = 单帧跳变阈值。
		double cancelRatio = (ConfigManager.CurrentConfig.VolumeCancelHysteresisRatio > 0.0
			&& ConfigManager.CurrentConfig.VolumeCancelHysteresisRatio < 1.0)
			? ConfigManager.CurrentConfig.VolumeCancelHysteresisRatio
			: 0.6;
		double flickFar = (ConfigManager.CurrentConfig.VolumeFlickFarDistance > 0.0)
			? ConfigManager.CurrentConfig.VolumeFlickFarDistance
			: 360.0;
		double flickJump = (ConfigManager.CurrentConfig.VolumeFlickCancelDistance > 0.0)
			? ConfigManager.CurrentConfig.VolumeFlickCancelDistance
			: 120.0;
		// Cancel rule: outer-swipe escape, or return inside the trigger radius. Once adjusting,
		// a tighter 60% hysteresis radius applies so small returns or edge jitter never snap volume back.
		bool insideCancelRadius = _volumeAdjustActive
			? dist < subWheelTriggerDistance * cancelRatio
			: dist < subWheelTriggerDistance;
		// Intentional flick-out: while adjusting, a single-frame distance jump beyond
		// the configured jump threshold while already past the configured far distance
		// counts as flicking away to cancel. Steady drags never trip it, preserving the
		// check8 immunity to angle drift that used to kill long drags.
		bool volumeFlickOut = false;
		if (_volumeAdjustActive && _volumeFlickPrevDist >= 0.0)
		{
			volumeFlickOut = dist > flickFar
			    && dist - _volumeFlickPrevDist > flickJump;
			_volumeFlickPrevDist = dist;
		}
		// A flick-out / over-travel cancel latched this gesture. Re-arm it only once
		// the pointer clearly returns (back inside 2x the trigger radius): the user can
		// then keep adjusting on the way back out. While the pointer stays far out the
		// latch holds, so the volume cannot snap back and re-drag around the cancel point.
		if (!_volumeAdjustActive && _volumeFlickCancelled && dist < subWheelTriggerDistance * 2.0)
		{
			_volumeFlickCancelled = false;
			_volumeMaxedOutDist = -1.0;
		}
		if (isEscaped || insideCancelRadius || volumeFlickOut)
		{
			// 外甩取消或缩回触发距离内：恢复基准音量并退出调音模式（本手势仍视为已接管，松手不再执行单步动作）
			if (_volumeAdjustActive)
			{
				_volumeAdjustActive = false;
				_volumeGestureTookOver = true;
				if (volumeFlickOut)
				{
					_volumeFlickCancelled = true;
				}
				_volumeFlickPrevDist = -1.0;
				_volumeLockedSector = -1;
				_volumeMaxedOutDist = -1.0;
				if (_volumeBaseline >= 0f)
				{
					SystemVolume.SetVolume(_volumeBaseline);
				}
				QueueVolumePreview(-1, GetCurrentGestureVersion());
			}
			return false;
		}
		if (!_volumeAdjustActive && !_volumeFlickCancelled)
		{
			// 首次越过触发距离：记录基准音量与基准拖距，进入调音模式
			if (!SystemVolume.TryGetVolume(out _volumeBaseline))
			{
				return false;
			}
			// 基线拖距固定为触发距离：从越过触发距离那一刻起算拖距，首帧不额外吃掉拖距
			_volumeBaselineDist = dist; // first enter (dist~trigger) matches the old fixed baseline; a mid-return re-enter starts from travel=0
			_volumeGestureTookOver = true;
			_volumeAdjustActive = true;
			_volumeLastPercent = -1f;
			_volumeLastOsdTick = 0L;
			_volumeLockedSector = sectorIndex;
			_volumeFlickPrevDist = dist;
			_volumeMaxedOutDist = -1.0;
			// Wake the native volume flyout so the system OSD shows while this gesture adjusts
			SystemVolume.ShowOsd(_volumeBaseline, isUp);
		}
		// A cancelled or latched gesture must not fall through into the incremental map:
		// active is already false here, so bail out instead of dragging on stale baseline.
		if (!_volumeAdjustActive)
		{
			return false;
		}
		// 增量映射：满程 200px 拖距映射 ±100%（约 2px/1%），灵敏度为原 285px 的约 1.4 倍；
		// 按整数百分比写盘，避免高频 COM 调用
		double travel = Math.Max(0.0, dist - _volumeBaselineDist);
		double fullTravel = 200.0;
		double delta = Math.Clamp(travel / fullTravel, 0.0, 1.0);
		int baselinePercent = (int)Math.Round(_volumeBaseline * 100.0, MidpointRounding.AwayFromZero);
		int targetPercent = Math.Clamp(baselinePercent + (int)Math.Round(delta * 100.0, MidpointRounding.AwayFromZero) * direction, 0, 100);
		// Volume pinned at 0%/100% while still dragging outward: meter the over-travel
		// and cancel the whole gesture (restore baseline) once it passes the threshold.
		bool pinnedAtEdge = (targetPercent <= 0 && direction < 0) || (targetPercent >= 100 && direction > 0);
		if (pinnedAtEdge)
		{
			if (_volumeMaxedOutDist < 0.0)
			{
				_volumeMaxedOutDist = travel;
			}
			else if (travel - _volumeMaxedOutDist > 150.0)
			{
				_volumeAdjustActive = false;
				_volumeFlickCancelled = true;
				_volumeFlickPrevDist = -1.0;
				_volumeLockedSector = -1;
				_volumeMaxedOutDist = -1.0;
				if (_volumeBaseline >= 0f)
				{
					SystemVolume.SetVolume(_volumeBaseline);
				}
				QueueVolumePreview(-1, GetCurrentGestureVersion());
				return false;
			}
		}
		else
		{
			_volumeMaxedOutDist = -1.0;
		}
		if (targetPercent != _volumeLastPercent)
		{
			_volumeLastPercent = targetPercent;
			SystemVolume.SetVolume(targetPercent / 100f);
			// Throttled OSD refresh: at most one key inject per 180ms keeps the flyout from strobing
			long nowTick = Environment.TickCount64;
			if (nowTick - _volumeLastOsdTick >= 180L)
			{
				_volumeLastOsdTick = nowTick;
				SystemVolume.ShowOsd(targetPercent / 100f, isUp);
			}
			QueueVolumePreview(targetPercent, GetCurrentGestureVersion());
		}
		return true;
	}

	// 音量预览节流：与高亮更新同锁、同 Render 优先级，覆盖式单挂起（模式见 QueueHighlightUpdate）
	private void QueueVolumePreview(int percent, long gestureVersion)
	{
		bool shouldSchedule;
		lock (_uiUpdateSync)
		{
			if (!_isGestureActive || gestureVersion != _gestureVersion)
			{
				return;
			}
			if (percent == _pendingVolumePercent)
			{
				return;
			}
			_pendingVolumePercent = percent;
			_pendingVolumePreviewVersion = gestureVersion;
			shouldSchedule = !_volumePreviewScheduled;
			_volumePreviewScheduled = true;
		}

		if (!shouldSchedule)
		{
			return;
		}

		try
		{
			Application.Current.Dispatcher.BeginInvoke((Action)ApplyVolumePreview, DispatcherPriority.Render);
		}
		catch
		{
			lock (_uiUpdateSync)
			{
				if (_pendingVolumePreviewVersion == gestureVersion)
				{
					_volumePreviewScheduled = false;
				}
			}
		}
	}

	private void ApplyVolumePreview()
	{
		int percent;
		long previewVersion;
		RadialWindow? radialWindow;
		lock (_uiUpdateSync)
		{
			if (!_volumePreviewScheduled)
			{
				return;
			}
			percent = _pendingVolumePercent;
			previewVersion = _pendingVolumePreviewVersion;
			if (!_isGestureActive || previewVersion != _gestureVersion)
			{
				_volumePreviewScheduled = false;
				return;
			}
			radialWindow = _radialWindow;
			if (radialWindow == null)
			{
				return;
			}
			_volumePreviewScheduled = false;
		}

		if (!IsCurrentGesture(previewVersion) || !ReferenceEquals(_radialWindow, radialWindow))
		{
			return;
		}
		radialWindow.SetVolumePreview(percent, percent >= 0);
	}

	private bool CheckIsIsolated(out string processName)
	{
		processName = ActiveWindowHelper.GetActiveWindowInfo(out nint fgHwnd);
		string cleanProcess = (processName ?? "").Trim().ToLowerInvariant();

		bool isWhitelisted = false;
		if (ConfigManager.CurrentConfig.WhitelistedProcesses != null)
		{
			foreach (string whitelistedProcess in ConfigManager.CurrentConfig.WhitelistedProcesses)
			{
				if (string.Equals(whitelistedProcess.Trim(), cleanProcess, StringComparison.OrdinalIgnoreCase))
				{
					isWhitelisted = true;
					break;
				}
			}
		}

		bool isBlacklisted = false;
		if (ConfigManager.CurrentConfig.BlacklistedProcesses != null)
		{
			foreach (string blacklistedProcess in ConfigManager.CurrentConfig.BlacklistedProcesses)
			{
				if (string.Equals(blacklistedProcess.Trim(), cleanProcess, StringComparison.OrdinalIgnoreCase))
				{
					isBlacklisted = true;
					break;
				}
			}
		}

		bool isProcessIsolated = false;
		if (string.Equals(ConfigManager.CurrentConfig.IsolationMode, "Whitelist", StringComparison.OrdinalIgnoreCase))
		{
			isProcessIsolated = !isWhitelisted;
		}
		else
		{
			isProcessIsolated = isBlacklisted;
		}

		TriggerConfig? triggerConfig = ConfigManager.CurrentConfig.Trigger;
		ModifierKeys currentModifiers = KeyboardHook.GetCurrentModifiers();
		bool disableCtrl = ConfigManager.CurrentConfig.DisableOnCtrl && currentModifiers.HasFlag(ModifierKeys.Control) && !(triggerConfig?.RequireCtrl == true);
		bool disableShift = ConfigManager.CurrentConfig.DisableOnShift && currentModifiers.HasFlag(ModifierKeys.Shift) && !(triggerConfig?.RequireShift == true);
		bool disableAlt = ConfigManager.CurrentConfig.DisableOnAlt && currentModifiers.HasFlag(ModifierKeys.Alt) && !(triggerConfig?.RequireAlt == true);
		bool isModifierSuppressed = disableCtrl | disableShift | disableAlt;

		bool isFullScreenSuppressed = false;
		if (ConfigManager.CurrentConfig.DisableOnFullScreen)
		{
			if (!isWhitelisted && FullScreenHelper.IsActiveWindowFullScreen(fgHwnd, cleanProcess))
			{
				isFullScreenSuppressed = true;
			}
		}

		return isProcessIsolated || isModifierSuppressed || isFullScreenSuppressed;
	}

	private bool IsModifierKey(uint vkCode)
	{
		if (vkCode != 17 && vkCode != 162 && vkCode != 163 && vkCode != 18 && vkCode != 164 && vkCode != 165 && vkCode != 16 && vkCode != 160 && vkCode != 161 && vkCode != 91)
		{
			return vkCode == 92;
		}
		return true;
	}

	private void Hook_OnTriggerButtonDown(object? sender, MouseEventArgs e)
	{
		//IL_0027: Unknown result type (might be due to invalid IL or missing references)
		//IL_002c: Unknown result type (might be due to invalid IL or missing references)
		//IL_0035: Unknown result type (might be due to invalid IL or missing references)
		//IL_0037: Unknown result type (might be due to invalid IL or missing references)
		//IL_0043: Unknown result type (might be due to invalid IL or missing references)
		//IL_0045: Unknown result type (might be due to invalid IL or missing references)
		//IL_0051: Unknown result type (might be due to invalid IL or missing references)
		//IL_0053: Unknown result type (might be due to invalid IL or missing references)
		//IL_005f: Unknown result type (might be due to invalid IL or missing references)
		//IL_0061: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		TriggerConfig triggerConfig = ConfigManager.CurrentConfig.Trigger ?? new TriggerConfig();
		if (triggerConfig.TriggerType != "Mouse")
		{
			return;
		}
		if (TryCancelPendingMouseReleaseAsBounce())
		{
			_mouseTriggerDown = true;
			if (_isWaitingForThreshold && ConfigManager.CurrentConfig.LongPressTrigger)
			{
				StartLongPressTimer();
			}
			e.Handled = true;
			return;
		}
		ModifierKeys currentModifiers = KeyboardHook.GetCurrentModifiers();
		if ((!triggerConfig.RequireCtrl || ((((int)currentModifiers & 2))) != 0) && (!triggerConfig.RequireShift || ((((int)currentModifiers & 4))) != 0) && (!triggerConfig.RequireAlt || ((((int)currentModifiers & 1))) != 0) && (!triggerConfig.RequireWin || ((((int)currentModifiers & 8))) != 0))
		{
			if (CheckIsIsolated(out string _) || IsPointOnTaskbar(e.Position))
			{
				// 隔离模式、黑名单或位于任务栏/托盘区域：绝对穿透放行，严禁调用 CancelGestureTracking() 及其包含的 ReleaseStuckModifiers()，杜绝注入虚假 KeyUp 破坏物理按键
				_isWaitingForThreshold = false;
				_isGestureActive = false;
				_mouseTriggerDown = false;
				CancelLongPressTimer();
				e.Handled = false;
				return;
			}
			string triggerBtn = triggerConfig.MouseButton ?? ConfigManager.CurrentConfig.TriggerButton ?? "RightButton";
			_startPoint = e.Position;
			var (scaleX, scaleY) = RadialWindow.GetMonitorDpiScale(_startPoint);
			_currentDpiScaleX = scaleX;
			_currentDpiScaleY = scaleY;
			BeginGestureTracking();
			_isWaitingForThreshold = true;
			_isGestureActive = false;
			_mouseTriggerDown = true;
			// 可选：长按不动超过阈值即呼出轮盘（与拖动呼出共存）
			// 核心保障：当触发键为鼠标左键时，自动保证长按呼出定时器启动，使得长按稳定唤醒轮盘，单机保持原生点击
			bool isLeftButtonTrigger = string.Equals(triggerBtn, "LeftButton", StringComparison.OrdinalIgnoreCase);
			if (ConfigManager.CurrentConfig.LongPressTrigger || isLeftButtonTrigger)
			{
				StartLongPressTimer();
			}
			e.Handled = true;
		}
	}

	// ==================== 鼠标手势 ====================

	/// <summary>
	/// 任意鼠标按键原始事件：手势触发键由此接管（与"轮盘触发键"可以不同）。
	/// 按下 → 开始画轨迹；抬起 → 识别执行（轻点透传原生点击）。
	/// </summary>
	private void Hook_OnRawMouseButton(object? sender, RawMouseEventArgs e)
	{
		if (!ConfigManager.CurrentConfig.GestureEnabled)
		{
			return;
		}
		string gestureButton = ConfigManager.CurrentConfig.GestureTriggerButton ?? "MiddleButton";
		var triggerConfig = ConfigManager.CurrentConfig.Trigger;
		string wheelBtn = triggerConfig?.MouseButton ?? ConfigManager.CurrentConfig.TriggerButton ?? "RightButton";
		// 冲突守卫：若手势按键与主轮盘触发键重叠，优先保证轮盘手势，手势让位，杜绝双重拦截
		if (string.Equals(gestureButton, wheelBtn, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		if (!string.Equals(e.MouseButton, gestureButton, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}
		if (e.IsButtonDown)
		{
			// SendInput 重放回来的按下：放行（不重新开始手势）
			if (_gestureReplayPending)
			{
				_gestureReplayPending = false;
				return;
			}
			if (CheckIsIsolated(out _) || IsPointOnTaskbar(e.Position))
			{
				_gestureMode = false;
				e.Handled = false;
				return;
			}
			BeginGesture(e.Position);
			e.Handled = true; // 拦截原生按下
			return;
		}
		if (!_gestureMode)
		{
			return;
		}
		bool hadPath = _gestureTracking;
		EndGesture(e.Position);
		string pattern = GetPreviewPattern();
		if (hadPath)
		{
			if (pattern.Length > 0)
			{
				ActionItem? ga = FindGestureAction(pattern);
				if (ga != null)
				{
					ActionItem action = ga;
					((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
					{
						ActionExecutor.Execute(action);
					}, (DispatcherPriority)5, Array.Empty<object>());
				}
			}
			// 已画但未映射/未成图样：吞掉不执行
		}
		else
		{
			// 轻点：SendInput 回放原生点击（回放事件放行，见 _gestureReplayPending）
			_gestureReplayPending = true;
			ThreadPool.QueueUserWorkItem(_ => _mouseHook.ReplayTriggerClick(gestureButton));
		}
		e.Handled = true; // 拦截原生抬起
	}

	/// <summary>方向码 → 图样字符（8 方向，方位码与图样选项一致：U/D/L/R/UL/UR/DL/DR，屏幕坐标 y 向下）。</summary>
	private static string GestureDirCode(int dir)
	{
		return dir switch
		{
			0 => "R",  // 右
			1 => "DR", // 右下
			2 => "D",  // 下
			3 => "DL", // 左下
			4 => "L",  // 左
			5 => "UL", // 左上
			6 => "U",  // 上
			_ => "UR"  // 右上
		};
	}

	private static int GestureQuantizeDir(double dx, double dy)
	{
		double deg = Math.Atan2(dy, dx) * (180.0 / Math.PI);
		if (deg < 0.0)
		{
			deg += 360.0;
		}
		return (int)Math.Round(deg / 45.0) % 8;
	}

	private void BeginGesture(Point pressPoint)
	{
		_gestureMode = true;
		_gestureWaiting = true;
		_gestureTracking = false;
		_gesturePressPoint = pressPoint;
		_gestureLastSample = pressPoint;
		_gestureRuns.Clear();
		_gesturePendingDir = -1;
		_gesturePendingLen = 0.0;
		var (tScaleX, tScaleY) = RadialWindow.GetMonitorDpiScale(pressPoint);
		_trailScaleX = tScaleX;
		_trailScaleY = tScaleY;
		_trailLastX = pressPoint.X;
		_trailLastY = pressPoint.Y;
		// 按下即清空并隐藏浮层：杜绝上一次手势轨迹在触发瞬间闪现
		DispatchUi(delegate
		{
			if (_trail != null)
			{
				_trail.ClearTrail();
				_trail.Hide();
			}
		});
		// 轨迹在越过阈值后才显示（见 ShowGestureTrail）
	}

	private void ShowGestureTrail()
	{
		Point p0 = _gesturePressPoint;
		double sx = _trailScaleX, sy = _trailScaleY;
		DispatchUi(delegate
		{
			if (_trail == null)
			{
				_trail = new GestureTrailOverlay();
			}
			// 顺序关键：先清空 → 画新起点 → 最后才 Show。
			// Show 会同步触发一次 WM_PAINT（嵌套消息泵），若此时画布还是旧内容就会闪现一次，故必须先清先画后显示。
			_trail.ClearTrail();
			_trail.PositionAt(p0.X, p0.Y, sx, sy);
			_trail.BeginAt(p0.X, p0.Y, sx, sy);
			_trail.Show();
		});
	}

	/// <summary>
	/// 移动采样（延迟分段缓冲）：每步把位移并入当前方向行程；
	/// 方向切换时把上一行程存入缓冲列表。短段/曲线抖动的过滤在构建图样时统一做，
	/// 因此"画的线可以弯曲"，微拐弯不会产生方向段。
	/// </summary>
	private void FeedGesturePoint(Point current)
	{
		double dx = current.X - _gestureLastSample.X;
		double dy = current.Y - _gestureLastSample.Y;
		double dist = Math.Sqrt(dx * dx + dy * dy);
		if (dist < 2.0)
		{
			return; // 微抖动忽略
		}
		int dir = GestureQuantizeDir(dx, dy);
		if (dir == _gesturePendingDir)
		{
			_gesturePendingLen += dist;
		}
		else
		{
			if (_gesturePendingLen > 0.0)
			{
				_gestureRuns.Add((_gesturePendingDir, _gesturePendingLen));
				if (_gestureRuns.Count > 12)
				{
					_gestureRuns.RemoveAt(0);
				}
			}
			_gesturePendingDir = dir;
			_gesturePendingLen = dist;
		}
		_gestureLastSample = current;
		// 轨迹：距离够才加一次，避免污染 Hook 消息调度的消息队列；同时更新"松手将执行"提示
		if (Math.Abs(current.X - _trailLastX) + Math.Abs(current.Y - _trailLastY) >= 4.0)
		{
			_trailLastX = current.X;
			_trailLastY = current.Y;
			double tx = current.X, ty = current.Y;
			string hint = BuildGestureHint(GetPreviewPattern());
			string placement = ConfigManager.CurrentConfig.GestureHintPlacement ?? "Auto";
			if (placement == "Auto")
			{
				int curDir = GestureQuantizeDir(current.X - _gesturePressPoint.X, current.Y - _gesturePressPoint.Y);
				placement = GestureDirCode((curDir + 4) % 8); // 提示放在运动反方向，避免被手遮挡
			}
			string hintPlacement = placement;
			DispatchUi(delegate
			{
				_trail?.AddPoint(tx, ty, _trailScaleX, _trailScaleY);
				_trail?.UpdateHint(hint, tx, ty, _trailScaleX, _trailScaleY, hintPlacement);
			});
		}
	}

	/// <summary>
	/// 构建最终图样：过滤掉长度低于灵敏度的短段（屏蔽过短的线段/曲线抖动），
	/// 相邻同向合并，最多 3 段；只有完整匹配映射才触发。
	/// </summary>
	private string GetPreviewPattern()
	{
		double segMin = ConfigManager.CurrentConfig.GestureSegmentSensitivity > 6.0 ? ConfigManager.CurrentConfig.GestureSegmentSensitivity : 12.0;
		List<(int dir, double len)> runs = new List<(int, double)>(_gestureRuns);
		if (_gesturePendingDir >= 0 && _gesturePendingLen > 0.0)
		{
			runs.Add((_gesturePendingDir, _gesturePendingLen));
		}
		List<int> dirs = new List<int>();
		foreach (var (dir, len) in runs)
		{
			if (len < segMin)
			{
				continue; // 短段屏蔽
			}
			if (dirs.Count > 0 && dirs[dirs.Count - 1] == dir)
			{
				continue; // 相邻同向合并
			}
			dirs.Add(dir);
			if (dirs.Count >= 3)
			{
				break;
			}
		}
		return string.Join("-", dirs.Select(GestureDirCode));
	}

	/// <summary>图样 → 箭头文本（如 "D-R" → "↓→"）。</summary>
	private static string GesturePatternGlyph(string pattern)
	{
		return pattern
			.Replace("UL", "↖")
			.Replace("UR", "↗")
			.Replace("DL", "↙")
			.Replace("DR", "↘")
			.Replace("U", "↑")
			.Replace("D", "↓")
			.Replace("L", "←")
			.Replace("R", "→");
	}

	/// <summary>提示文本：图样箭头 + 映射动作名/参数；未映射只显示图样。</summary>
	private string BuildGestureHint(string pattern)
	{
		string glyph = GesturePatternGlyph(pattern);
		ActionItem? a = FindGestureAction(pattern);
		if (a == null)
		{
			return glyph;
		}
		string label = (!string.IsNullOrEmpty(a.Name) && a.Name != "手势动作") ? a.Name : (a.Parameter ?? "");
		if (string.IsNullOrEmpty(label))
		{
			label = a.Type ?? "";
		}
		return string.IsNullOrEmpty(label) ? glyph : $"{glyph}  {label}";
	}

	private void EndGesture(Point current)
	{
		// 收尾：把最后一段并入缓冲（过滤与合并交给 GetPreviewPattern）
		{
			double dx = current.X - _gestureLastSample.X;
			double dy = current.Y - _gestureLastSample.Y;
			double dist = Math.Sqrt(dx * dx + dy * dy);
			if (dist >= 2.0)
			{
				int dir = GestureQuantizeDir(dx, dy);
				if (dir == _gesturePendingDir)
				{
					_gesturePendingLen += dist;
				}
				else
				{
					if (_gesturePendingLen > 0.0)
					{
						_gestureRuns.Add((_gesturePendingDir, _gesturePendingLen));
					}
					_gesturePendingDir = dir;
					_gesturePendingLen = dist;
				}
			}
			if (_gesturePendingLen > 0.0)
			{
				_gestureRuns.Add((_gesturePendingDir, _gesturePendingLen));
			}
		}
		_gesturePendingDir = -1;
		_gesturePendingLen = 0.0;
		_gestureMode = false;
		_gestureWaiting = false;
		_gestureTracking = false;
		_trailLastX = double.NaN;
		_trailLastY = double.NaN;
		DispatchUi(delegate
		{
			if (_trail != null)
			{
				_trail.ClearTrail();
				_trail.Hide();
			}
		});
	}

	private void DispatchUi(Action action)
	{
		try
		{
			((DispatcherObject)Application.Current).Dispatcher.BeginInvoke(action, DispatcherPriority.Background);
		}
		catch
		{
		}
	}

	/// <summary>查找手势图样映射的动作。</summary>
	private ActionItem? FindGestureAction(string pattern)
	{
		if (ConfigManager.CurrentConfig.GestureMappings != null)
		{
			foreach (GestureMapping m in ConfigManager.CurrentConfig.GestureMappings)
			{
				if (string.Equals(m.Pattern, pattern, StringComparison.OrdinalIgnoreCase))
				{
					return m.Action;
				}
			}
		}
		return null;
	}

	/// <summary>启动长按触发计时（按下时，仅鼠标触发）。</summary>
	private void StartLongPressTimer()
	{
		CancelLongPressTimer();
		double delay = ConfigManager.CurrentConfig.LongPressDelayMs > 0.0 ? ConfigManager.CurrentConfig.LongPressDelayMs : 450.0;
		int generation;
		lock (_longPressLock)
		{
			generation = ++_longPressGeneration;
			_longPressTimer = new System.Threading.Timer(delegate
			{
				LongPressTimerCallback(generation);
			}, null, TimeSpan.FromMilliseconds(delay), System.Threading.Timeout.InfiniteTimeSpan);
		}
	}

	private void CancelLongPressTimer()
	{
		lock (_longPressLock)
		{
			_longPressGeneration++;
			if (_longPressTimer != null)
			{
				_longPressTimer.Dispose();
				_longPressTimer = null;
			}
		}
	}

	/// <summary>长按达阈值：在按下处呼出轮盘（光标仍在中心，移动后再选择）。线程池回调 → 主线程 UI。</summary>
	private void LongPressTimerCallback(int generation)
	{
		lock (_longPressLock)
		{
			if (generation != _longPressGeneration)
			{
				return; // 已被取消/替换的旧回调
			}
			_longPressTimer = null;
		}
		bool isMouseWaiting = _mouseTriggerDown && _isWaitingForThreshold;
		bool isKbWaiting = _kbTriggerWaiting && _isWaitingForThreshold;
		if ((!isMouseWaiting && !isKbWaiting) || _isGestureActive)
		{
			return;
		}
		try
		{
			_isWaitingForThreshold = false;
			_isGestureActive = true;
			if (isKbWaiting)
			{
				_kbTriggerWaiting = false;
				GetCursorPos(out var lpPoint);
				_startPoint = new Point((double)lpPoint.x, (double)lpPoint.y);
				var (scaleX, scaleY) = RadialWindow.GetMonitorDpiScale(_startPoint);
				_currentDpiScaleX = scaleX;
				_currentDpiScaleY = scaleY;
			}
			string processName = ActiveWindowHelper.GetActiveWindowProcessName();
			WheelProfile profile = ConfigManager.GetProfileForProcess(processName);
			long gestureVersion = GetCurrentGestureVersion();
			Point startPoint = _startPoint;
			((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				try
				{
					if (!_isGestureActive)
					{
						return;
					}
					if (ShowRadialUI(startPoint, profile, gestureVersion))
					{
						ProcessMove(startPoint);
						ApplyPendingHighlight();
						ApplyVolumePreview();
					}
				}
				catch (Exception ex)
				{
					AppLogger.LogError("ShowRadialUI failed in LongPressTimerCallback", ex);
					// 激活失败：恢复状态，保证拖拽等其它触发途径不受影响
					_isWaitingForThreshold = true;
					_isGestureActive = false;
				}
			}, DispatcherPriority.Normal, Array.Empty<object>());
		}
		catch
		{
			// 预检/分派失败：恢复状态
			_isWaitingForThreshold = true;
			_isGestureActive = false;
		}
	}

	private void Hook_OnTriggerButtonUp(object? sender, MouseEventArgs e)
	{
		TriggerConfig triggerConfig = ConfigManager.CurrentConfig.Trigger ?? new TriggerConfig();
		if (triggerConfig.TriggerType != "Mouse")
		{
			return;
		}
		// 手势键抬起已由 Raw 事件处理，这里直接吞掉
		if (_gestureMode)
		{
			e.Handled = true;
			return;
		}

		string triggerButton = triggerConfig.MouseButton ?? ConfigManager.CurrentConfig.TriggerButton ?? "RightButton";
		bool ownsTriggerState = _mouseTriggerDown || _isGestureActive || _isWaitingForThreshold;
		bool shouldDebounce = ownsTriggerState &&
			ConfigManager.CurrentConfig.EnableMouseReleaseDebounce &&
			string.Equals(triggerButton, "RightButton", StringComparison.OrdinalIgnoreCase);

		if (shouldDebounce)
		{
			_mouseTriggerDown = false;
			CancelLongPressTimer();
			int debounceMs = Math.Clamp(ConfigManager.CurrentConfig.MouseReleaseDebounceMs, 1, 100);
			ScheduleMouseReleaseDebounce(e.Position, triggerButton, debounceMs);
			e.Handled = true;
			return;
		}

		e.Handled = CompleteMouseTriggerRelease(e.Position, triggerButton);
	}

	private bool CompleteMouseTriggerRelease(Point releasePosition, string triggerButton)
	{
		bool wasTriggerDown = _mouseTriggerDown;
		_mouseTriggerDown = false;
		CancelLongPressTimer();

		if (!wasTriggerDown && !_isGestureActive && !_isWaitingForThreshold)
		{
			// StarPie 未接管对应按下时，绝不可吞掉孤立的物理抬起事件。
			return false;
		}

		if (_isWaitingForThreshold)
		{
			CancelGestureTracking();
			_isWaitingForThreshold = false;
			ThreadPool.QueueUserWorkItem(_ => _mouseHook.ReplayTriggerClick(triggerButton));
			return true;
		}

		if (!_isGestureActive)
		{
			// 等待状态已结束但手势未激活时补发原生点击，避免丢键。
			ThreadPool.QueueUserWorkItem(_ => _mouseHook.ReplayTriggerClick(triggerButton));
			return true;
		}

		var finalState = EndActiveGesture();
		int finalSector = finalState.Sector;
		int finalSubSector = finalState.SubSector;
		WheelProfile? finalProfile = finalState.Profile;
		RadialWindow? endedWindow = finalState.Window;
		bool isEscaped = finalState.IsEscaped;
		long endedPresentationVersion = finalState.PresentationVersion;
		bool volumeTookOver = _volumeGestureTookOver;
		float volumeBaseline = _volumeBaseline;
		// 手势结束：同步复位音量接管状态，杜绝 took/active/baseline 残留污染下一手势。
		_volumeAdjustActive = false;
		_volumeGestureTookOver = false;
		_volumeBaseline = -1f;
		_volumeBaselineDist = 0.0;
		_volumeLastPercent = -1f;
		_volumeLastOsdTick = 0L;
		_volumeLockedSector = -1;
		_volumeFlickPrevDist = -1.0;
		_volumeFlickCancelled = false;
		_volumeMaxedOutDist = -1.0;
		((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
		{
			if (volumeTookOver)
			{
				if (isEscaped && volumeBaseline >= 0f)
				{
					SystemVolume.SetVolume(volumeBaseline);
				}
				if (endedWindow?.PresentationVersion == endedPresentationVersion)
				{
					endedWindow.SetVolumePreview(-1, isActive: false);
				}
			}
			CloseGestureWindow(endedWindow, endedPresentationVersion);
			if (volumeTookOver)
			{
				return;
			}
			ActionItem? targetAction = null;
			if (!isEscaped && finalProfile != null)
			{
				if (finalSector >= 0)
				{
					targetAction = finalProfile.GetEffectiveAction(finalSector, finalSubSector);
				}
				else if (finalSector == -1)
				{
					targetAction = finalProfile.GetEffectiveCenterAction();
				}
			}
			if (targetAction == null)
			{
				ActionItem? cancelAction = ConfigManager.CurrentConfig?.CancelAction;
				if (isEscaped &&
					ConfigManager.CurrentConfig?.EnableCancelAction == true &&
					cancelAction != null && !string.IsNullOrEmpty(cancelAction.Type))
				{
					targetAction = cancelAction;
				}
			}
			if (targetAction != null)
			{
				ActionExecutor.EnqueueAction(targetAction);
			}
		}, DispatcherPriority.Normal, Array.Empty<object>());
		return true;
	}

	private void ScheduleMouseReleaseDebounce(Point releasePosition, string triggerButton, int debounceMs)
	{
		lock (_mouseReleaseDebounceLock)
		{
			if (_mouseReleasePending)
			{
				return;
			}
			_mouseReleasePending = true;
			_pendingMouseReleasePosition = releasePosition;
			_pendingMouseReleaseButton = triggerButton;
			int generation = ++_mouseReleaseDebounceGeneration;
			_mouseReleaseDebounceTimer?.Dispose();
			_mouseReleaseDebounceTimer = new System.Threading.Timer(
				_ => CompletePendingMouseRelease(generation),
				null,
				TimeSpan.FromMilliseconds(debounceMs),
				System.Threading.Timeout.InfiniteTimeSpan);
		}
	}

	private void CompletePendingMouseRelease(int generation)
	{
		System.Threading.Timer? completedTimer = null;
		lock (_mouseReleaseDebounceLock)
		{
			if (!_mouseReleasePending || generation != _mouseReleaseDebounceGeneration)
			{
				return;
			}
			_mouseReleasePending = false;
			completedTimer = _mouseReleaseDebounceTimer;
			_mouseReleaseDebounceTimer = null;
			CompleteMouseTriggerRelease(_pendingMouseReleasePosition, _pendingMouseReleaseButton);
		}
		completedTimer?.Dispose();
	}

	private bool TryCancelPendingMouseReleaseAsBounce()
	{
		System.Threading.Timer? timerToCancel;
		lock (_mouseReleaseDebounceLock)
		{
			if (!_mouseReleasePending)
			{
				return false;
			}
			_mouseReleasePending = false;
			_mouseReleaseDebounceGeneration++;
			timerToCancel = _mouseReleaseDebounceTimer;
			_mouseReleaseDebounceTimer = null;
		}
		timerToCancel?.Dispose();
		return true;
	}

	private void CancelMouseReleaseDebounce()
	{
		System.Threading.Timer? timerToCancel;
		lock (_mouseReleaseDebounceLock)
		{
			_mouseReleasePending = false;
			_mouseReleaseDebounceGeneration++;
			timerToCancel = _mouseReleaseDebounceTimer;
			_mouseReleaseDebounceTimer = null;
		}
		timerToCancel?.Dispose();
	}

	private void Hook_OnMouseWheel(object? sender, MouseWheelHookEventArgs e)
	{
		if (_isGestureActive && _radialWindow != null && _activeProfile != null)
		{
			_activeProfile.EnsureLayers();
			if (_activeProfile.Layers.Count > 1)
			{
				string trigger = ConfigManager.CurrentConfig?.LayerSwitchTrigger ?? "Wheel";
				if (string.Equals(trigger, "Wheel", StringComparison.OrdinalIgnoreCase))
				{
					int count = _activeProfile.Layers.Count;
					int curIdx = _activeProfile.ActiveLayerIndex;
					int nextIdx;
					if (e.Delta > 0)
					{
						nextIdx = (curIdx - 1 + count) % count;
					}
					else
					{
						nextIdx = (curIdx + 1) % count;
					}
					_activeProfile.ActiveLayerIndex = nextIdx;
					_activeProfile.SyncRootPropertiesFromActiveLayer();

					RadialWindow? rw = _radialWindow;
					if (rw != null)
					{
						DispatchUi(() => rw.SwitchToLayer(nextIdx));
					}
					e.Handled = true;
					return;
				}
			}
		}
	}

	private void KeyboardHook_OnKeyDown(object? sender, GlobalKeyEventArgs e)
	{
		if (_isGestureActive)
		{
			// ESC 按键即刻取消手势轮盘并吞键，防止干扰前台应用
			if (e.VkCode == 27)
			{
				SoundEffectManager.Play(SoundType.GestureCancel);
				CancelGestureTracking();
				e.Handled = true;
				return;
			}
		}

		if (_isGestureActive && _radialWindow != null && _activeProfile != null)
		{
			_activeProfile.EnsureLayers();
			if (_activeProfile.Layers.Count > 1)
			{
				string trigger = ConfigManager.CurrentConfig?.LayerSwitchTrigger ?? "Wheel";
				bool isLayerSwitchKey = false;
				if (string.Equals(trigger, "Tab", StringComparison.OrdinalIgnoreCase) && e.VkCode == 9)
				{
					isLayerSwitchKey = true;
				}
				else if (string.Equals(trigger, "CustomKey", StringComparison.OrdinalIgnoreCase) && e.VkCode == (ConfigManager.CurrentConfig?.LayerSwitchVkCode ?? 9))
				{
					isLayerSwitchKey = true;
				}

				if (isLayerSwitchKey)
				{
					int count = _activeProfile.Layers.Count;
					int curIdx = _activeProfile.ActiveLayerIndex;
					int nextIdx = (curIdx + 1) % count;
					_activeProfile.ActiveLayerIndex = nextIdx;
					_activeProfile.SyncRootPropertiesFromActiveLayer();

					RadialWindow? rw = _radialWindow;
					if (rw != null)
					{
						DispatchUi(() => rw.SwitchToLayer(nextIdx));
					}
					e.Handled = true;
					return;
				}
			}
		}
		//IL_0028: Unknown result type (might be due to invalid IL or missing references)
		//IL_002d: Unknown result type (might be due to invalid IL or missing references)
		//IL_0036: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_0044: Unknown result type (might be due to invalid IL or missing references)
		//IL_0046: Unknown result type (might be due to invalid IL or missing references)
		//IL_0052: Unknown result type (might be due to invalid IL or missing references)
		//IL_0054: Unknown result type (might be due to invalid IL or missing references)
		//IL_0060: Unknown result type (might be due to invalid IL or missing references)
		//IL_0062: Unknown result type (might be due to invalid IL or missing references)
		//IL_00da: Unknown result type (might be due to invalid IL or missing references)
		//IL_00df: Unknown result type (might be due to invalid IL or missing references)
		TriggerConfig triggerConfig = ConfigManager.CurrentConfig.Trigger ?? new TriggerConfig();
		if (triggerConfig.TriggerType != "Keyboard")
		{
			return;
		}
		ModifierKeys modifiers = e.Modifiers;
		if ((!triggerConfig.RequireCtrl || ((((int)modifiers & 2))) != 0) && (!triggerConfig.RequireShift || ((((int)modifiers & 4))) != 0) && (!triggerConfig.RequireAlt || ((((int)modifiers & 1))) != 0) && (!triggerConfig.RequireWin || ((((int)modifiers & 8))) != 0) && (triggerConfig.VkCode == 0 || IsModifierKey(triggerConfig.VkCode) || e.VkCode == triggerConfig.VkCode))
		{
			if (_isGestureActive)
			{
				// 轮盘激活期间吞掉触发键的自动重复，防止连发漏进前台应用。
				e.Handled = true;
				return;
			}
			if (_isWaitingForThreshold && !_kbTriggerWaiting)
			{
				// 鼠标触发正在等待阈值，保持原有吞键语义。
				e.Handled = true;
				return;
			}
			if (_kbTriggerWaiting)
			{
				// 键盘触发等待期（含自动重复）：穿透放行，原生输入零干扰。
				return;
			}
			if (CheckIsIsolated(out string _))
			{
				// 隔离模式与黑名单：绝对穿透放行，严禁调用 CancelGestureTracking() 及其包含的 ReleaseStuckModifiers()
				_isWaitingForThreshold = false;
				_isGestureActive = false;
				_kbTriggerWaiting = false;
				CancelLongPressTimer();
				e.Handled = false;
				return;
			}
			GetCursorPos(out var lpPoint);
			_startPoint = new Point((double)lpPoint.x, (double)lpPoint.y);
			var (dpiX, dpiY) = RadialWindow.GetMonitorDpiScale(_startPoint);
			_currentDpiScaleX = dpiX;
			_currentDpiScaleY = dpiY;
			BeginGestureTracking();
			_isWaitingForThreshold = true;
			_isGestureActive = false;
			_kbTriggerWaiting = true;
			_kbTriggerDownTick = Environment.TickCount64;
			if (ConfigManager.CurrentConfig.LongPressTrigger)
			{
				StartLongPressTimer();
			}
			// 穿透模式：不吞键，原生 KeyDown 立即到达前台应用。
		}
	}

	private void KeyboardHook_OnKeyUp(object? sender, GlobalKeyEventArgs e)
	{
		TriggerConfig triggerConfig = ConfigManager.CurrentConfig.Trigger ?? new TriggerConfig();
		if (triggerConfig.TriggerType != "Keyboard")
		{
			return;
		}
		bool flag = false;
		if (triggerConfig.VkCode != 0 && e.VkCode == triggerConfig.VkCode)
		{
			flag = true;
		}
		if (triggerConfig.RequireCtrl && (e.VkCode == 17 || e.VkCode == 162 || e.VkCode == 163))
		{
			flag = true;
		}
		if (triggerConfig.RequireShift && (e.VkCode == 16 || e.VkCode == 160 || e.VkCode == 161))
		{
			flag = true;
		}
		if (triggerConfig.RequireAlt && (e.VkCode == 18 || e.VkCode == 164 || e.VkCode == 165))
		{
			flag = true;
		}
		if (triggerConfig.RequireWin && (e.VkCode == 91 || e.VkCode == 92))
		{
			flag = true;
		}
		if (!flag)
		{
			return;
		}
		CancelLongPressTimer();
		if (_isWaitingForThreshold)
		{
			// 穿透模式：轻点（未达长按或拖动阈值）时 Down/Up 均已原生放行，
			// 直接取消跟踪即可，无需吞键补发。
			_kbTriggerWaiting = false;
			_isWaitingForThreshold = false;
			_isGestureActive = false;
		}
		else
		{
			if (!_isGestureActive)
			{
				// 穿透模式：无手势进行，Down 从未被吞，KeyUp 原生放行。
				_kbTriggerWaiting = false;
				return;
			}
			var finalState = EndActiveGesture();
			int finalSector = finalState.Sector;
			int finalSubSector = finalState.SubSector;
			WheelProfile? finalProfile = finalState.Profile;
			RadialWindow? endedWindow = finalState.Window;
			bool isEscaped = finalState.IsEscaped;
			long endedPresentationVersion = finalState.PresentationVersion;
			bool volumeTookOver = _volumeGestureTookOver;
			float volumeBaseline = _volumeBaseline;
			_volumeAdjustActive = false;
			_volumeGestureTookOver = false;
			_volumeBaseline = -1f;
			_volumeBaselineDist = 0.0;
			_volumeLastPercent = -1f;
			_volumeLastOsdTick = 0L;
			_volumeLockedSector = -1;
			_volumeFlickPrevDist = -1.0;
			_volumeFlickCancelled = false;
			_volumeMaxedOutDist = -1.0;
			((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
			{
				if (volumeTookOver)
				{
					// 本手势已接管音量调节：外甩取消则恢复基准音量，正常松手保持最终音量；
					// 隐藏音量预览并跳过全部动作执行（不再注入单键音量键）
					if (isEscaped && volumeBaseline >= 0f)
					{
						SystemVolume.SetVolume(volumeBaseline);
					}
					if (endedWindow?.PresentationVersion == endedPresentationVersion)
					{
						endedWindow.SetVolumePreview(-1, isActive: false);
					}
				}
				CloseGestureWindow(endedWindow, endedPresentationVersion);
				if (volumeTookOver)
				{
					return;
				}
				ActionItem? targetAction = null;
				if (!isEscaped && finalProfile != null)
				{
					if (finalSector >= 0)
					{
						targetAction = finalProfile.GetEffectiveAction(finalSector, finalSubSector);
					}
					else if (finalSector == -1)
					{
						targetAction = finalProfile.GetEffectiveCenterAction();
					}
				}
				if (targetAction == null)
				{
					// 仅"外甩取消"（释放时处于外甩状态且未选中任何动作）时执行自定义取消动作；
					// 回到中心取消按钮松手仍为默认静默关闭。
					ActionItem? cancelAction = ConfigManager.CurrentConfig?.CancelAction;
					if (_lastEscapedState &&
						ConfigManager.CurrentConfig?.EnableCancelAction == true &&
						cancelAction != null && !string.IsNullOrEmpty(cancelAction.Type))
					{
						targetAction = cancelAction;
					}
				}
				if (targetAction != null)
				{
					SoundEffectManager.Play(SoundType.ActionExecute);
					ActionExecutor.EnqueueAction(targetAction);
				}
				else
				{
					SoundEffectManager.Play(SoundType.GestureCancel);
				}
			}, DispatcherPriority.Normal, Array.Empty<object>());
			// 穿透模式：激活前的 Down 已原生放行，KeyUp 放行与其配对，
			// 避免前台应用按键状态卡死。
			e.Handled = false;
		}
	}

	private void Hook_OnMouseMove(object? sender, MouseEventArgs e)
	{
		//IL_0019: Unknown result type (might be due to invalid IL or missing references)
		//IL_001e: Unknown result type (might be due to invalid IL or missing references)
		//IL_0033: Unknown result type (might be due to invalid IL or missing references)
		//IL_0038: Unknown result type (might be due to invalid IL or missing references)
		//IL_00d6: Unknown result type (might be due to invalid IL or missing references)
		//IL_0087: Unknown result type (might be due to invalid IL or missing references)
		//IL_008c: Unknown result type (might be due to invalid IL or missing references)
		//IL_009f: Unknown result type (might be due to invalid IL or missing references)
		//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
		// 鼠标手势：采集轨迹
		if (_gestureMode)
		{
			if (_gestureWaiting)
			{
				double gScaleX = (_currentDpiScaleX > 0.0) ? _currentDpiScaleX : 1.0;
				double gScaleY = (_currentDpiScaleY > 0.0) ? _currentDpiScaleY : 1.0;
				double gdx = (e.Position.X - _gesturePressPoint.X) / gScaleX;
				double gdy = (e.Position.Y - _gesturePressPoint.Y) / gScaleY;
				double gDist = Math.Sqrt(gdx * gdx + gdy * gdy);
				if (gDist >= ConfigManager.CurrentConfig.DragThreshold)
				{
					_gestureWaiting = false;
					_gestureTracking = true;
					_gestureLastSample = e.Position;
					ShowGestureTrail();
				}
			}
			else if (_gestureTracking)
			{
				FeedGesturePoint(e.Position);
			}
			return;
		}
		if (_isWaitingForThreshold)
		{
			Point position = e.Position;
			double scaleX = (_currentDpiScaleX > 0.0) ? _currentDpiScaleX : 1.0;
			double scaleY = (_currentDpiScaleY > 0.0) ? _currentDpiScaleY : 1.0;
			double num = (position.X - _startPoint.X) / scaleX;
			position = e.Position;
			double num2 = (position.Y - _startPoint.Y) / scaleY;
			double num3 = num * num + num2 * num2;
			double dragThreshold = ConfigManager.CurrentConfig.DragThreshold;
			if (num3 >= dragThreshold * dragThreshold)
			{
				if (_kbTriggerWaiting && Environment.TickCount64 - _kbTriggerDownTick < KeyboardTriggerMinHoldMs)
				{
					// 穿透模式持握门槛：触发键按下未满 200ms 不唤出轮盘，
					// 快速敲击/甩动按普通按键处理；位移持续累积，满门槛后自然激活。
					return;
				}
				_kbTriggerWaiting = false;
				_isWaitingForThreshold = false;
				_isGestureActive = true;
				CancelLongPressTimer(); // 拖动先于长按触发
				string activeWindowProcessName = ActiveWindowHelper.GetActiveWindowProcessName();
				_activeProfile = ConfigManager.GetProfileForProcess(activeWindowProcessName);
				Point center = _startPoint;
				WheelProfile profile = _activeProfile;
				Point initialPos = e.Position;
				long gestureVersion = GetCurrentGestureVersion();
				// Publish the threshold-crossing position before the UI callback is
				// queued. Later move events replace it with the newest state.
				ProcessMove(initialPos);
				((DispatcherObject)Application.Current).Dispatcher.BeginInvoke((Delegate)(Action)delegate
				{
					try
					{
						if (!_isGestureActive)
						{
							return;
						}
						if (ShowRadialUI(center, profile, gestureVersion))
						{
							ApplyPendingHighlight();
						ApplyVolumePreview();
						}
					}
					catch (Exception ex)
					{
						AppLogger.LogError("ShowRadialUI failed in Hook_OnMouseMove", ex);
						_isWaitingForThreshold = true;
						_isGestureActive = false;
						_kbTriggerWaiting = false;
					}
				}, DispatcherPriority.Normal, Array.Empty<object>());
			}
		}
		else if (_isGestureActive)
		{
			ProcessMove(e.Position);
		}
	}

	private void ProcessMove(Point currentPoint)
	{
		double moveScaleX = (_currentDpiScaleX > 0.0) ? _currentDpiScaleX : 1.0;
		double moveScaleY = (_currentDpiScaleY > 0.0) ? _currentDpiScaleY : 1.0;
		double num = (currentPoint.X - _startPoint.X) / moveScaleX;
		double num2 = (currentPoint.Y - _startPoint.Y) / moveScaleY;
		double num3 = Math.Sqrt(num * num + num2 * num2);
		int num4 = -1;
		int num5 = -1;
		bool flag = false;
		double num6 = (ConfigManager.CurrentConfig.CoreDeadzoneRadius > 0.0)
			? ConfigManager.CurrentConfig.CoreDeadzoneRadius
			: Math.Min(ConfigManager.CurrentConfig.CoreRadius, ConfigManager.CurrentConfig.DragThreshold * 0.6);
		if (num6 <= 0.0)
		{
			num6 = 15.0;
		}
		bool flag2 = false;
		double num7 = ((ConfigManager.CurrentConfig.SubWheelTriggerDistance > 20.0) ? ConfigManager.CurrentConfig.SubWheelTriggerDistance : 95.0);
		if (num3 >= num6)
		{
			double wheelRadius = ConfigManager.CurrentConfig.WheelRadius;
			bool enableMultiTier = ConfigManager.CurrentConfig.EnableMultiTier;
			double num8 = ((ConfigManager.CurrentConfig.SubWheelOuterRadius > 0.0) ? ConfigManager.CurrentConfig.SubWheelOuterRadius : (wheelRadius * 1.55));
			double num9 = (enableMultiTier ? (num8 + 20.0) : wheelRadius);
			// 角度与初判扇区提前到外甩判定之前：外甩短路需要知道当前指向扇区；
			// 音量加/减扇区豁免外甩短路，保证"拖距调音"在任意拖距都可调（外甩不再吞掉音量手势）。
			double num11 = Math.Atan2(num2, num) * (180.0 / Math.PI);
			if (num11 < 0.0)
			{
				num11 += 360.0;
			}
			int num12 = _activeProfile?.SectorCount ?? 8;
			if (num12 <= 0)
			{
				num12 = 8;
			}
			double num13 = 360.0 / (double)num12;
			num4 = (int)Math.Floor((num11 + num13 / 2.0) / num13) % num12;
			bool isVolumeSector = IsVolumeSector(num4) || _volumeAdjustActive;
			if (ConfigManager.CurrentConfig.EnableOuterEscapeCancel && !isVolumeSector)
			{
				double num10 = ((ConfigManager.CurrentConfig.OuterEscapeDistance > 0.0) ? ConfigManager.CurrentConfig.OuterEscapeDistance : (num9 * 1.5));
				if (num3 > num10)
				{
					flag = true;
					num4 = -1;
					num5 = -1;
				}
			}
			if (!flag)
			{
				bool isFan = ConfigManager.CurrentConfig.SubmenuStyle == "Fan";

				// 蜂窝扇二级轮盘防抖与扇区保持锁定 (Hysteresis & Parent Sector Lock)
				if (enableMultiTier && isFan && _lastShowSubTier && _selectedSectorIndex >= 0 && _selectedSectorIndex < (_activeProfile?.Actions.Count ?? 0))
				{
					double parentCenterAngle = (double)_selectedSectorIndex * num13;
					double angleDiff = Math.Abs(NormalizeAngleDeg(num11 - parentCenterAngle));
					double exitDist = Math.Max(25.0, num7 - 22.0);

					// 在蜂窝扇已激活状态下，若光标距离未明显缩回内圈且处于展开扇面内（±num13 * 0.90），
					// 锁定当前主扇区，彻底杜绝划向两侧子扇区时因极角越界而切回一级轮盘或跳变扇区
					if (num3 >= exitDist && angleDiff <= num13 * 0.90)
					{
						num4 = _selectedSectorIndex;
					}
				}

				if (enableMultiTier && _activeProfile != null && num4 >= 0 && num4 < _activeProfile.Actions.Count)
				{
					ActionItem? actionItem = _activeProfile.GetEffectiveAction(num4);
					if (actionItem != null && actionItem.SubActions != null && actionItem.SubActions.Count > 0)
					{
						if (isFan)
						{
							double fanTriggerDist = (_lastShowSubTier && _selectedSectorIndex == num4)
								? Math.Max(25.0, num7 - 22.0)
								: num7;

							if (num3 >= fanTriggerDist)
							{
								flag2 = true;
								num5 = HitTestFanSubs(currentPoint, _startPoint, num4, actionItem.SubActions.Count);
							}
						}
						else
						{
							if (num3 >= num7 || (ConfigManager.CurrentConfig.AutoExpandSubRingsOnPopup && ConfigManager.CurrentConfig.SubmenuStyle == "Wheel"))
							{
								flag2 = true;
							}
							double num14 = ((ConfigManager.CurrentConfig.SubWheelInnerGap >= 0.0) ? ConfigManager.CurrentConfig.SubWheelInnerGap : 4.0);
							double num15 = wheelRadius + num14 + 2.0;
							if (num3 >= num15)
							{
								int count = actionItem.SubActions.Count;
								double num16 = (double)num4 * num13 - num13 / 2.0;
								double num17;
								for (num17 = num11 - num16; num17 < 0.0; num17 += 360.0)
								{
								}
								while (num17 >= 360.0)
								{
									num17 -= 360.0;
								}
								if (num17 <= num13)
								{
									num5 = Math.Clamp((int)(num17 / (num13 / (double)count)), 0, count - 1);
								}
							}
						}
					}
				}
			}
		}
		// 音量"拖距调音"：音量加/减扇区越过子轮盘触发距离即接管为距离映射调音，屏蔽二级子轮盘
		if (ProcessVolumeAdjust(num3, num4, flag, num7))
		{
			flag2 = false;
			num5 = -1;
		}
		QueueHighlightUpdate(num4, num5, flag, flag2, GetCurrentGestureVersion());
	}

	private bool ShowRadialUI(Point center, WheelProfile profile, long gestureVersion)
	{
		profile.EnsureLayers();
		profile.ActiveLayerIndex = 0;
		profile.SyncRootPropertiesFromActiveLayer();
		_activeProfile = profile;

		if (ProfileRequiresTaskbarPrefetch(profile))
		{
			try
			{
				WindowTaskbarHelper.Prefetch();
			}
			catch
			{
			}
		}

		RadialWindow window;
		lock (_uiUpdateSync)
		{
			if (!_isGestureActive || gestureVersion != _gestureVersion)
			{
				return false;
			}
			window = _radialWindow ??= new RadialWindow(center, profile);
		}

		try
		{
			window.Present(center, profile, ConfigManager.ConfigurationRevision, gestureVersion);
			lock (_uiUpdateSync)
			{
				if (!_isGestureActive || gestureVersion != _gestureVersion || !ReferenceEquals(_radialWindow, window))
				{
					window.Dismiss(gestureVersion);
					return false;
				}
			}

			Point actualCenter = window.ActualPhysicalCenter;
			if (Math.Abs(actualCenter.X - _startPoint.X) > 1.0 || Math.Abs(actualCenter.Y - _startPoint.Y) > 1.0)
			{
				_startPoint = actualCenter;
				var (newDpiX, newDpiY) = RadialWindow.GetMonitorDpiScale(_startPoint);
				if (newDpiX > 0.0 && newDpiY > 0.0)
				{
					_currentDpiScaleX = newDpiX;
					_currentDpiScaleY = newDpiY;
				}
				SetCursorPos((int)Math.Round(actualCenter.X), (int)Math.Round(actualCenter.Y));
				ProcessMove(actualCenter);
			}
			SoundEffectManager.Play(SoundType.WheelPopup);
			return true;
		}
		catch
		{
			lock (_uiUpdateSync)
			{
				if (ReferenceEquals(_radialWindow, window))
				{
					_radialWindow = null;
				}
			}
			try
			{
				window.CloseFast();
			}
			catch
			{
			}
			throw;
		}
	}

	private void HideRadialUI()
	{
		RadialWindow? window;
		long presentationVersion;
		lock (_uiUpdateSync)
		{
			window = _radialWindow;
			presentationVersion = window?.PresentationVersion ?? -1;
		}
		if (window == null)
		{
			return;
		}
		try
		{
			window.Dismiss(presentationVersion);
		}
		catch
		{
		}
	}

	private void CloseGestureWindow(RadialWindow? gestureWindow, long presentationVersion)
	{
		if (gestureWindow == null)
		{
			return;
		}
		ActionExecutor.ReleaseStuckModifiers();
		try
		{
			gestureWindow.Dismiss(presentationVersion);
		}
		catch
		{
		}
	}

	private int HitTestFanSubs(Point currentPoint, Point centerPoint, int parentIndex, int subCount)
	{
		if (parentIndex < 0 || subCount <= 0) return -1;

		double hitScaleX = (_currentDpiScaleX > 0.0) ? _currentDpiScaleX : 1.0;
		double hitScaleY = (_currentDpiScaleY > 0.0) ? _currentDpiScaleY : 1.0;
		double dx = (currentPoint.X - centerPoint.X) / hitScaleX;
		double dy = (currentPoint.Y - centerPoint.Y) / hitScaleY;
		double dist = Math.Sqrt(dx * dx + dy * dy);
		
		double outer = ConfigManager.CurrentConfig.WheelRadius;
		double inner = ConfigManager.CurrentConfig.InnerRadius;
		
		if (dist < inner + (outer - inner) * 0.40)
		{
			return -1;
		}

		int n = _activeProfile?.SectorCount ?? 8;
		double sectorSize = 360.0 / n;
		double midRad = parentIndex * sectorSize * (Math.PI / 180.0);
		
		int activeCount = Math.Min(RadialWindow.FanSubmenuSlotCount, subCount);
		if (activeCount == 1)
		{
			return 0;
		}

		double mouseAngle = Math.Atan2(dy, dx);
		
		int bestSub = 0;
		double bestAngleDiff = double.MaxValue;
		
		double ux = Math.Cos(midRad), uy = Math.Sin(midRad);
		double vx = -Math.Sin(midRad), vy = Math.Cos(midRad);
		double R = (inner + outer) / 2.0;

		for (int j = 0; j < activeCount; j++)
		{
			int slot = RadialWindow.GetFanSlotIndex(j, activeCount);
			var (du, dv) = RadialWindow.GetFanSubOffsetForShape(ConfigManager.CurrentConfig.Shape, slot);
			
			double px = ux * (du * R) + vx * (dv * R);
			double py = uy * (du * R) + vy * (dv * R);
			
			double itemAngle = Math.Atan2(py, px);
			double diff = Math.Abs(NormalizeAngleRad(mouseAngle - itemAngle));

			// 防抖与当前子扇区粘滞保持：若当前项正是上一帧选中的子扇区，给予微量阻尼偏置，杜绝边缘处高频抖动跳变
			if (_selectedSubSectorIndex == j)
			{
				diff -= 0.08; // 约 4.5 度的防抖偏置
			}

			if (diff < bestAngleDiff)
			{
				bestAngleDiff = diff;
				bestSub = j;
			}
		}
		
		return bestSub;
	}

	private static double NormalizeAngleRad(double angle)
	{
		while (angle > Math.PI) angle -= 2.0 * Math.PI;
		while (angle < -Math.PI) angle += 2.0 * Math.PI;
		return angle;
	}

	private static double NormalizeAngleDeg(double angle)
	{
		while (angle > 180.0) angle -= 360.0;
		while (angle < -180.0) angle += 360.0;
		return angle;
	}


	public void Dispose()
	{
		CancelMouseReleaseDebounce();
		CancelLongPressTimer();
		_mouseHook.OnTriggerButtonDown -= Hook_OnTriggerButtonDown;
		_mouseHook.OnTriggerButtonUp -= Hook_OnTriggerButtonUp;
		_mouseHook.OnMouseMove -= Hook_OnMouseMove;
		_mouseHook.OnRawMouseButtonEvent -= Hook_OnRawMouseButton;
		_mouseHook.OnMouseWheel -= Hook_OnMouseWheel;
		if (_keyboardHook != null)
		{
			_keyboardHook.OnKeyDown -= KeyboardHook_OnKeyDown;
			_keyboardHook.OnKeyUp -= KeyboardHook_OnKeyUp;
		}

		RadialWindow? window;
		lock (_uiUpdateSync)
		{
			window = _radialWindow;
			_radialWindow = null;
			_isGestureActive = false;
			_highlightUpdateScheduled = false;
		}
		if (window != null)
		{
			try
			{
				if (window.Dispatcher.CheckAccess())
				{
					window.CloseFast();
				}
				else
				{
					window.Dispatcher.Invoke(window.CloseFast);
				}
			}
			catch
			{
			}
		}
	}

	private static bool ProfileRequiresTaskbarPrefetch(WheelProfile? profile)
	{
		if (profile == null) return false;
		if (ActionRequiresTaskbar(profile.CenterAction)) return true;
		if (profile.Actions != null)
		{
			for (int i = 0; i < profile.Actions.Count; i++)
			{
				if (ActionRequiresTaskbar(profile.Actions[i])) return true;
			}
		}
		if (profile.Layers != null)
		{
			for (int l = 0; l < profile.Layers.Count; l++)
			{
				var layer = profile.Layers[l];
				if (layer == null) continue;
				if (ActionRequiresTaskbar(layer.CenterAction)) return true;
				if (layer.Actions != null)
				{
					for (int i = 0; i < layer.Actions.Count; i++)
					{
						if (ActionRequiresTaskbar(layer.Actions[i])) return true;
					}
				}
			}
		}
		if (ConfigManager.CurrentConfig?.EnableGlobalInheritance == true && !string.Equals(profile.ProcessName, "Global", StringComparison.OrdinalIgnoreCase))
		{
			var globalProf = ConfigManager.GetGlobalProfile();
			if (globalProf != null && ProfileRequiresTaskbarPrefetch(globalProf)) return true;
		}
		return false;
	}

	private static bool ActionRequiresTaskbar(ActionItem? action)
	{
		if (action == null) return false;
		string type = action.Type ?? "";
		if (type.Equals("SwitchWindow", StringComparison.OrdinalIgnoreCase) ||
		    type.Equals("Taskbar", StringComparison.OrdinalIgnoreCase) ||
		    type.Equals("Tile", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		string param = action.Parameter ?? "";
		if (param.StartsWith("Taskbar", StringComparison.OrdinalIgnoreCase) ||
		    param.StartsWith("SwitchWindow", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}
		if (action.SubActions != null)
		{
			for (int i = 0; i < action.SubActions.Count; i++)
			{
				if (ActionRequiresTaskbar(action.SubActions[i])) return true;
			}
		}
		return false;
	}
}
