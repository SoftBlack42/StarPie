using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace WinPieGestures;

public partial class RadialWindow : Window
{
	[StructLayout(LayoutKind.Sequential)]
	public struct POINT
	{
		public int x;
		public int y;
	}

	private const uint MONITOR_DEFAULTTONEAREST = 2;
	private const uint SWP_NOSIZE = 0x0001;
	private const uint SWP_NOACTIVATE = 0x0010;
	private const uint SWP_NOZORDER = 0x0004;
	private const int WM_DPICHANGED = 0x02E0;
	private const int GWL_EXSTYLE = -20;
	private const nint WS_EX_NOACTIVATE = 0x08000000;
	private const nint WS_EX_TOOLWINDOW = 0x00000080;

	[StructLayout(LayoutKind.Sequential)]
	public struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool GetWindowRect(nint hWnd, out RECT lpRect);

	private double _wheelCanvasSize = 360.0;
	private double _canvasCenter = 180.0;

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
	private static extern nint GetWindowLongPtr64(nint hWnd, int nIndex);

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	private static extern nint GetWindowLong32(nint hWnd, int nIndex);

	private static nint GetWindowLongPtr(nint hWnd, int nIndex)
	{
		return (IntPtr.Size == 8) ? GetWindowLongPtr64(hWnd, nIndex) : GetWindowLong32(hWnd, nIndex);
	}

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
	private static extern nint SetWindowLongPtr64(nint hWnd, int nIndex, nint dwNewLong);

	[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
	private static extern nint SetWindowLong32(nint hWnd, int nIndex, nint dwNewLong);

	private static nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong)
	{
		return (IntPtr.Size == 8) ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : SetWindowLong32(hWnd, nIndex, dwNewLong);
	}

	[DllImport("user32.dll")]
	public static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("SHCore.dll", SetLastError = true)]
	public static extern int GetDpiForMonitor(nint hMonitor, int dpiType, out uint dpiX, out uint dpiY);

	[DllImport("user32.dll", SetLastError = true)]
	public static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

	/// <summary>
	/// 获取指定物理像素坐标所在显示器的 DPI 缩放系数(多显示器混合 DPI 环境下逐屏获取)。
	/// </summary>
	public static (double scaleX, double scaleY) GetMonitorDpiScale(Point physicalPoint)
	{
		try
		{
			POINT pt = new POINT { x = (int)Math.Round(physicalPoint.X), y = (int)Math.Round(physicalPoint.Y) };
			nint hMonitor = MonitorFromPoint(pt, MONITOR_DEFAULTTONEAREST);
			if (hMonitor != IntPtr.Zero)
			{
				if (GetDpiForMonitor(hMonitor, 0, out uint dpiX, out uint dpiY) == 0 && dpiX > 0 && dpiY > 0)
				{
					return (dpiX / 96.0, dpiY / 96.0);
				}
			}
		}
		catch
		{
		}
		return (1.0, 1.0);
	}

	/// <summary>
	/// 以物理像素将窗口严格居中对齐到 _centerPoint(鼠标物理坐标)所在的显示器上。
	/// 在 PerMonitorV2 混合 DPI 环境下，SetWindowPos 直接以物理像素精确定位，杜绝副屏跨屏漂移。
	/// </summary>
	/// <summary>
		/// SwitchWindow 动作：显示任务栏第 N 窗口的当前图标（参数缺失/非法默认第 1 个）；获取失败返回 null（调用方回退默认程序图标）。
		/// </summary>
		private static FrameworkElement? BuildSwitchWindowIcon(string parameter, double size, bool showText)
		{
			int n = 1;
			if (int.TryParse(parameter?.Trim(), out int parsed) && parsed > 0)
			{
				n = parsed;
			}
			BitmapSource? windowIcon = WindowTaskbarHelper.GetNthWindowIcon(n);
			if (windowIcon == null)
			{
				return null;
			}
			return new Image
			{
				Source = windowIcon,
				Width = size,
				Height = size,
				Stretch = Stretch.Uniform,
				Margin = new Thickness(0.0, 0.0, 0.0, showText ? 2 : 0),
				HorizontalAlignment = HorizontalAlignment.Center
			};
		}

		/// <summary>以物理像素将窗口居中到 _centerPoint 所在显示器（PerMonitorV2 混合 DPI 副屏定位）。</summary>
		private void PositionWindowOnTargetMonitor()
		{
			nint handle = new WindowInteropHelper(this).Handle;
			if (handle == IntPtr.Zero)
			{
				return;
			}
			ScreenContext screenCtx = ScreenHelper.GetScreenContextAtPoint(_centerPoint);
			double scaleX = screenCtx.DpiScale.DpiScaleX;
			double scaleY = screenCtx.DpiScale.DpiScaleY;
			int physicalWidth = (int)Math.Round(_wheelCanvasSize * scaleX);
			int physicalHeight = (int)Math.Round(_wheelCanvasSize * scaleY);
			int physicalLeft = (int)Math.Round(_centerPoint.X - physicalWidth / 2.0);
			int physicalTop = (int)Math.Round(_centerPoint.Y - physicalHeight / 2.0);
			SetWindowPos(handle, IntPtr.Zero, physicalLeft, physicalTop, physicalWidth, physicalHeight, SWP_NOACTIVATE | SWP_NOZORDER);
		}

		/// <summary>
		/// 纯物理像素校正：实测窗口当前物理中心与目标点 _centerPoint 的物理偏差，SetWindowPos 直接修正。
		/// 全程零 DPI 换算，天然免疫跨屏 DPI 缩放造成的漂移（dotnet/wpf#3105）。
		/// </summary>
		private void CenterOnPhysically(double targetX, double targetY)
		{
			try
			{
				nint handle = new WindowInteropHelper(this).Handle;
				if (handle == IntPtr.Zero || !GetWindowRect(handle, out RECT rect))
				{
					return;
				}
				double currentCenterX = (rect.Left + rect.Right) / 2.0;
				double currentCenterY = (rect.Top + rect.Bottom) / 2.0;
				double deltaX = targetX - currentCenterX;
				double deltaY = targetY - currentCenterY;
				if (Math.Abs(deltaX) < 1.0 && Math.Abs(deltaY) < 1.0)
				{
					return;
				}
				int targetLeft = (int)Math.Round(rect.Left + deltaX);
				int targetTop = (int)Math.Round(rect.Top + deltaY);
				SetWindowPos(handle, IntPtr.Zero, targetLeft, targetTop, 0, 0, SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE);
			}
			catch
			{
			}
		}

		protected override void OnSourceInitialized(EventArgs e)
		{
			base.OnSourceInitialized(e);
			if (PresentationSource.FromVisual(this) is HwndSource source)
			{
				source.AddHook(WndProc);
				nint handle = source.Handle;
				nint exStyle = GetWindowLongPtr(handle, GWL_EXSTYLE);
				SetWindowLongPtr(handle, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
			}
			PositionWindowOnTargetMonitor();
		}

		private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
		{
			if (msg == WM_DPICHANGED)
			{
				// 严禁标记 handled = true！必须允许 WPF HwndSource 内部更新 VisualTree 的 DPI 缩放矩阵
				Dispatcher.BeginInvoke(new Action(() =>
				{
					PositionWindowOnTargetMonitor();
					CenterOnPhysically(_centerPoint.X, _centerPoint.Y);
				}), DispatcherPriority.Render);
			}
			return IntPtr.Zero;
		}

		protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
		{
			base.OnDpiChanged(oldDpi, newDpi);
			PositionWindowOnTargetMonitor();
			Dispatcher.BeginInvoke(new Action(() =>
			{
				CenterOnPhysically(_centerPoint.X, _centerPoint.Y);
			}), DispatcherPriority.Render);
		}

		/// <summary>
		/// 极速同步销毁窗口，清除任何正在运行的透明度动画与可见性，杜绝外甩或取消时半透明残影滞留桌面
		/// </summary>
		public void CloseFast()
		{
			try
			{
				BeginAnimation(UIElement.OpacityProperty, null);
				Visibility = Visibility.Collapsed;
				Close();
			}
			catch
			{
			}
		}

	protected override void OnClosed(EventArgs e)
	{
		try
		{
			BeginAnimation(UIElement.OpacityProperty, null);
			if (PresentationSource.FromVisual(this) is HwndSource source)
			{
				source.RemoveHook(WndProc);
			}
			_isDisposed = true;
			StopPresentationAnimations();
			WheelCanvas?.Children.Clear();
			_sectorPaths?.Clear();
			_contentPanels?.Clear();
			_sectorTransforms?.Clear();
			_containerTransforms?.Clear();
			_sectorAngles?.Clear();
			_subSectorPaths?.Clear();
			_subContentContainers?.Clear();
			_subSectorTransforms?.Clear();
			_subContainerTransforms?.Clear();
			_subSectorAngles?.Clear();
			_subTierCache?.Clear();
		}
		catch
		{
		}
		base.OnClosed(e);
	}

	private sealed class SubTierVisuals
	{
		public List<System.Windows.Shapes.Path> Paths { get; }

		public List<Grid> Containers { get; }

		public List<TranslateTransform> PathTransforms { get; }

		public List<TranslateTransform> ContainerTransforms { get; }

		public List<double> Angles { get; }

		public SubTierVisuals(
			List<System.Windows.Shapes.Path> paths,
			List<Grid> containers,
			List<TranslateTransform> pathTransforms,
			List<TranslateTransform> containerTransforms,
			List<double> angles)
		{
			Paths = paths;
			Containers = containers;
			PathTransforms = pathTransforms;
			ContainerTransforms = containerTransforms;
			Angles = angles;
		}
	}

	private int _currentHighlightedSector;

	private int _currentHighlightedSubSector;

	private int _activeSubTierParentSector;

	private Point _centerPoint;

	private WheelProfile _profile;

	private readonly List<System.Windows.Shapes.Path> _sectorPaths;

	private readonly List<StackPanel> _contentPanels;

	private readonly List<TranslateTransform> _sectorTransforms;

	private readonly List<TranslateTransform> _containerTransforms;

	private readonly List<double> _sectorAngles;

	private readonly List<System.Windows.Shapes.Path> _subSectorPaths;

	private readonly List<Grid> _subContentContainers;

	private readonly List<TranslateTransform> _subSectorTransforms;

	private readonly List<TranslateTransform> _subContainerTransforms;

	private readonly List<double> _subSectorAngles;

	private readonly Dictionary<int, SubTierVisuals> _subTierCache = new Dictionary<int, SubTierVisuals>();

	private IRadialStyleRenderer _styleRenderer;

	private IRadialStyleRenderer? _subStyleRenderer;

	private Brush _defaultSectorBrush;

	private Brush _highlightSectorBrush;

	private Brush _sectorBorderBrush;

	private Brush _highlightBorderBrush;

	private Brush _textColorBrush;

	private static readonly Dictionary<string, double> _coreImageLuminanceCache = new Dictionary<string, double>();

	private Brush _coreBgBrush;

	private Brush _coreBorderBrush;

	private Brush _subDefaultSectorBrush;

	private Brush _subHighlightSectorBrush;

	private Brush _subSectorBorderBrush;

	private Brush _subHighlightBorderBrush;

	private Brush _subTextColorBrush;

	private double _innerRadius;

	private double _outerRadius;

	private double _borderThickness;

	private double _highlightBorderThickness;

	private bool _isOuterEscaped;

	private Visibility _defaultCoreExitIconVisibility = Visibility.Collapsed;

	private Visibility _defaultCoreCustomImageVisibility = Visibility.Collapsed;

	private double _defaultCoreExitIconOpacity = 1.0;

	private double _defaultCoreCustomImageOpacity = 1.0;

	private Effect? _defaultCoreCustomImageEffect;

	public Point ActualPhysicalCenter { get; private set; }

	public long PresentationVersion { get; private set; }

	private long _renderedConfigurationRevision = -1;

	private WheelProfile? _renderedProfile;

	private int _renderedLayerIndex = -1;

	private long _requestedConfigurationRevision;

	private bool _hasRenderedContent;

	private bool _isDisposed;

	private static Point ComputeClampedPhysicalCenter(Point centerPoint, double canvasSize)
	{
		if (ConfigManager.CurrentConfig?.EnableEdgeCollisionAvoidance != true)
		{
			return centerPoint;
		}

		string policy = ConfigManager.CurrentConfig?.EdgeOverflowPolicy ?? "ClampShift";
		if (string.Equals(policy, "None", StringComparison.OrdinalIgnoreCase))
		{
			return centerPoint;
		}

		ScreenContext screenCtx = ScreenHelper.GetScreenContextAtPoint(centerPoint);
		double scaleX = Math.Max(0.1, screenCtx.DpiScale.DpiScaleX);
		double scaleY = Math.Max(0.1, screenCtx.DpiScale.DpiScaleY);

		// 计算轮盘的真实有效可见半径（基于主轮盘外径及多级展开子轮盘外径，而非整个巨幅透明阴影画布）
		double wheelRadius = (ConfigManager.CurrentConfig != null && ConfigManager.CurrentConfig.WheelRadius > 0.0)
			? ConfigManager.CurrentConfig.WheelRadius
			: 138.0;
		bool enableMultiTier = ConfigManager.CurrentConfig?.EnableMultiTier == true;
		double ratio = (ConfigManager.CurrentConfig?.SubWheelRadiusRatio > 1.1) ? ConfigManager.CurrentConfig.SubWheelRadiusRatio : 1.55;
		double subMaxR = (ConfigManager.CurrentConfig?.SubWheelOuterRadius > 0.0)
			? ConfigManager.CurrentConfig.SubWheelOuterRadius
			: (wheelRadius * ratio);
		double visualRadiusDip = enableMultiTier ? Math.Max(wheelRadius, subMaxR) : wheelRadius;

		double visualRadiusPxX = visualRadiusDip * scaleX;
		double visualRadiusPxY = visualRadiusDip * scaleY;

		double marginDipX = (ConfigManager.CurrentConfig?.EdgeSafeMarginX >= 0)
			? ConfigManager.CurrentConfig.EdgeSafeMarginX
			: ((ConfigManager.CurrentConfig?.EdgeSafeMargin > 0) ? ConfigManager.CurrentConfig.EdgeSafeMargin : 16.0);
		double marginDipY = (ConfigManager.CurrentConfig?.EdgeSafeMarginY >= 0)
			? ConfigManager.CurrentConfig.EdgeSafeMarginY
			: ((ConfigManager.CurrentConfig?.EdgeSafeMargin > 0) ? ConfigManager.CurrentConfig.EdgeSafeMargin : 16.0);
		double marginX = marginDipX * scaleX;
		double marginY = marginDipY * scaleY;

		// 目标屏幕可用物理工作区 (已排除任务栏等)
		double workLeft = screenCtx.PhysicalWorkArea.Left;
		double workRight = screenCtx.PhysicalWorkArea.Right;
		double workTop = screenCtx.PhysicalWorkArea.Top;
		double workBottom = screenCtx.PhysicalWorkArea.Bottom;

		// 轮盘可见区域在屏幕工作区内的允许物理中心范围
		double minCenterX = workLeft + marginX + visualRadiusPxX;
		double maxCenterX = workRight - marginX - visualRadiusPxX;
		double minCenterY = workTop + marginY + visualRadiusPxY;
		double maxCenterY = workBottom - marginY - visualRadiusPxY;

		// 判断当前中心点是否会导致轮盘可见边缘发生越界或侵入安全边距
		bool overflows = centerPoint.X < minCenterX
					  || centerPoint.X > maxCenterX
					  || centerPoint.Y < minCenterY
					  || centerPoint.Y > maxCenterY;

		if (!overflows)
		{
			return centerPoint;
		}

		if (string.Equals(policy, "ScreenCenter", StringComparison.OrdinalIgnoreCase))
		{
			double screenCenterX = (workLeft + workRight) / 2.0;
			double screenCenterY = (workTop + workBottom) / 2.0;
			return new Point(screenCenterX, screenCenterY);
		}
		else // ClampShift
		{
			double clampedX = centerPoint.X;
			double clampedY = centerPoint.Y;

			if (maxCenterX >= minCenterX)
			{
				clampedX = Math.Clamp(centerPoint.X, minCenterX, maxCenterX);
			}
			else
			{
				clampedX = (workLeft + workRight) / 2.0;
			}

			if (maxCenterY >= minCenterY)
			{
				clampedY = Math.Clamp(centerPoint.Y, minCenterY, maxCenterY);
			}
			else
			{
				clampedY = (workTop + workBottom) / 2.0;
			}

			return new Point(clampedX, clampedY);
		}
	}

	public RadialWindow(Point centerPoint, WheelProfile profile)
	{
		_currentHighlightedSector = -999;
		_currentHighlightedSubSector = -1;
		_activeSubTierParentSector = -1;
		_sectorPaths = new List<System.Windows.Shapes.Path>();
		_contentPanels = new List<StackPanel>();
		_sectorTransforms = new List<TranslateTransform>();
		_containerTransforms = new List<TranslateTransform>();
		_sectorAngles = new List<double>();
		_subSectorPaths = new List<System.Windows.Shapes.Path>();
		_subContentContainers = new List<Grid>();
		_subSectorTransforms = new List<TranslateTransform>();
		_subContainerTransforms = new List<TranslateTransform>();
		_subSectorAngles = new List<double>();
		_innerRadius = 52.0;
		_outerRadius = 138.0;
		_borderThickness = 1.0;
		_highlightBorderThickness = 1.5;
		InitializeComponent();
		IsHitTestVisible = false;
		_centerPoint = centerPoint;
		_profile = profile;
		_requestedConfigurationRevision = ConfigManager.ConfigurationRevision;
		InitializeThemeAndStyle();
		ApplyLayoutFromCurrentConfiguration(centerPoint);
		CoreTextPanel.Visibility = Visibility.Collapsed;
		base.Loaded += RadialWindow_Loaded;
		UpdateProfileLabels();
		UpdateCenterIconVisuals();
	}

	/// <summary>
	/// 复用同一个透明 Window/HWND 呈现新手势。配置修订、方案引用或活动层变化时才重建视觉树；
	/// 其余呼出只重置交互状态、更新物理中心/DPI 并重新播放入场动画。
	/// </summary>
	public void Present(Point centerPoint, WheelProfile profile, long configurationRevision, long presentationVersion)
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.Invoke(() => Present(centerPoint, profile, configurationRevision, presentationVersion));
			return;
		}
		if (_isDisposed)
		{
			throw new InvalidOperationException("Cannot present a disposed RadialWindow.");
		}

		bool wasLoaded = IsLoaded;
		bool needsRebuild = !_hasRenderedContent ||
			_renderedConfigurationRevision != configurationRevision ||
			!ReferenceEquals(_renderedProfile, profile) ||
			_renderedLayerIndex != profile.ActiveLayerIndex;

		_centerPoint = centerPoint;
		_profile = profile;
		_requestedConfigurationRevision = configurationRevision;
		PresentationVersion = presentationVersion;
		ResetPresentationState();
		ApplyLayoutFromCurrentConfiguration(centerPoint);
		// 先将入场动画的初始状态写入视觉树，再显示 HWND，避免先绘制完整圆盘后才切换到动画起始帧造成闪烁。
		PrepareIntroAnimation();

		if (wasLoaded && needsRebuild)
		{
			RebuildVisualsFromCurrentConfiguration(configurationRevision);
		}

		if (!IsVisible)
		{
			// HWND 只在首次呈现时创建；后续手势不再 Hide/Show 透明窗口，避免 DWM 复用旧合成帧造成闪烁。
			Show();
		}
		PositionWindowOnTargetMonitor();
		Dispatcher.BeginInvoke(new Action(() =>
		{
			if (!_isDisposed && PresentationVersion == presentationVersion)
			{
				CenterOnPhysically(_centerPoint.X, _centerPoint.Y);
			}
		}), DispatcherPriority.Render);

		// 内容层在上一帧完成前保持隐藏；下一次 Render 回调再显示并启动动效，
		// 避免复用透明 HWND 时 DWM 先显示旧的完整视觉树。
		Dispatcher.BeginInvoke(new Action(() =>
		{
			if (_isDisposed || !IsVisible || PresentationVersion != presentationVersion)
			{
				return;
			}
			MainGrid.Visibility = Visibility.Visible;
			Dispatcher.BeginInvoke(new Action(() =>
			{
				if (!_isDisposed && IsVisible && PresentationVersion == presentationVersion)
				{
					StartIntroAnimation(presentationVersion);
				}
			}), DispatcherPriority.Render);
		}), DispatcherPriority.Render);
	}

	/// <summary>隐藏当前手势，但保留 Window/HWND 供下一次呼出复用。旧手势的延迟回调不得隐藏新手势。</summary>
	public void Dismiss(long expectedPresentationVersion)
	{
		if (!Dispatcher.CheckAccess())
		{
			Dispatcher.BeginInvoke(new Action(() => Dismiss(expectedPresentationVersion)), DispatcherPriority.Send);
			return;
		}
		if (_isDisposed || PresentationVersion != expectedPresentationVersion)
		{
			return;
		}
		StopPresentationAnimations();
		// 保持透明 HWND 常驻，只隐藏内容层；不再 Hide/Show 窗口，避免 DWM 在重新显示时闪出上一帧。
		Opacity = 1.0;
		MainGrid.Visibility = Visibility.Hidden;
		MainGrid.Opacity = 0.0;
		WindowScale.ScaleX = 0.65;
		WindowScale.ScaleY = 0.65;
		ClearSubTier();
		CoreVolumeText.Visibility = Visibility.Collapsed;
		CoreSelectionTextPanel.Visibility = Visibility.Collapsed;
		CoreSelectionOverlay.Visibility = Visibility.Collapsed;
	}

	private void ApplyLayoutFromCurrentConfiguration(Point requestedCenter)
	{
		double wheelRadius = ConfigManager.CurrentConfig.WheelRadius;
		double coreRadius = ConfigManager.CurrentConfig.CoreRadius;
		bool enableMultiTier = ConfigManager.CurrentConfig.EnableMultiTier;
		double ratio = ConfigManager.CurrentConfig.SubWheelRadiusRatio > 1.1
			? ConfigManager.CurrentConfig.SubWheelRadiusRatio
			: 1.55;
		double subMaxR = ConfigManager.CurrentConfig.SubWheelOuterRadius > 0.0
			? ConfigManager.CurrentConfig.SubWheelOuterRadius
			: wheelRadius * ratio;
		double maxEffectiveR = enableMultiTier ? Math.Max(wheelRadius, subMaxR + 25.0) : wheelRadius;
		_wheelCanvasSize = maxEffectiveR * 2.0 + 40.0;
		_canvasCenter = _wheelCanvasSize / 2.0;

		_centerPoint = ComputeClampedPhysicalCenter(requestedCenter, _wheelCanvasSize);
		ActualPhysicalCenter = _centerPoint;
		ScreenContext screenCtx = ScreenHelper.GetScreenContextAtPoint(_centerPoint);
		double scaleX = Math.Max(0.1, screenCtx.DpiScale.DpiScaleX);
		double scaleY = Math.Max(0.1, screenCtx.DpiScale.DpiScaleY);
		WindowStartupLocation = WindowStartupLocation.Manual;
		Left = _centerPoint.X / scaleX - _wheelCanvasSize / 2.0;
		Top = _centerPoint.Y / scaleY - _wheelCanvasSize / 2.0;
		Width = _wheelCanvasSize;
		Height = _wheelCanvasSize;
		MainGrid.Width = _wheelCanvasSize;
		MainGrid.Height = _wheelCanvasSize;
		WheelCanvas.Width = _wheelCanvasSize;
		WheelCanvas.Height = _wheelCanvasSize;

		double coreOffset = _canvasCenter - coreRadius;
		Canvas.SetLeft(CoreGrid, coreOffset);
		Canvas.SetTop(CoreGrid, coreOffset);
		CoreGrid.Width = coreRadius * 2.0;
		CoreGrid.Height = coreRadius * 2.0;
		Panel.SetZIndex(CoreGrid, 5);
		OuterEllipse.Width = wheelRadius * 2.0 + 8.0;
		OuterEllipse.Height = wheelRadius * 2.0 + 8.0;
	}

	private void UpdateProfileLabels()
	{
		CoreTitle.Text = _profile.ProcessName == "Global" ? I18n.T("CoreGlobalActions") : _profile.ProcessName;
		CoreSubtitle.Text = string.Format(I18n.T("CoreSectorActions"), _profile.SectorCount);
	}

	private void ResetPresentationState()
	{
		StopPresentationAnimations();
		ResetSectorVisuals();
		_currentHighlightedSector = -999;
		_currentHighlightedSubSector = -1;
		_activeSubTierParentSector = -1;
		_isOuterEscaped = false;
		_subTierCache.Clear();
		ClearSubTier();
		CoreScale.ScaleX = 1.0;
		CoreScale.ScaleY = 1.0;
		CoreVolumeText.Visibility = Visibility.Collapsed;
		CoreSelectionTextPanel.Visibility = Visibility.Collapsed;
		CoreSelectionOverlay.Visibility = Visibility.Collapsed;
		LayerIndicatorBadge.Visibility = Visibility.Collapsed;
		Opacity = 1.0;
	}

	private void StopPresentationAnimations()
	{
		BeginAnimation(UIElement.OpacityProperty, null);
		MainGrid.BeginAnimation(UIElement.OpacityProperty, null);
		WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
		WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
		CoreScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
		CoreScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
		_layerBadgeStoryboard?.Stop();
	}

	private void ResetSectorVisuals()
	{
		for (int i = 0; i < _sectorPaths.Count; i++)
		{
			System.Windows.Shapes.Path path = _sectorPaths[i];
			path.BeginAnimation(UIElement.OpacityProperty, null);
			path.Opacity = 1.0;
			path.Fill = _defaultSectorBrush;
			path.Stroke = _sectorBorderBrush;
			path.StrokeThickness = _borderThickness;
			Panel.SetZIndex(path, 0);
			_styleRenderer?.ApplySectorHighlight(path, isHighlighted: false);

			if (i < _sectorTransforms.Count)
			{
				TranslateTransform transform = _sectorTransforms[i];
				transform.BeginAnimation(TranslateTransform.XProperty, null);
				transform.BeginAnimation(TranslateTransform.YProperty, null);
				transform.X = 0.0;
				transform.Y = 0.0;
			}
			if (i < _containerTransforms.Count)
			{
				TranslateTransform transform = _containerTransforms[i];
				transform.BeginAnimation(TranslateTransform.XProperty, null);
				transform.BeginAnimation(TranslateTransform.YProperty, null);
				transform.X = 0.0;
				transform.Y = 0.0;
			}
			if (i < _contentPanels.Count)
			{
				StackPanel panel = _contentPanels[i];
				TextBlock? text = panel.Children.OfType<TextBlock>().FirstOrDefault();
				System.Windows.Shapes.Path? icon = panel.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
				if (text != null)
				{
					text.Foreground = _textColorBrush;
					text.FontWeight = FontWeights.Medium;
				}
				if (icon != null)
				{
					icon.Fill = _textColorBrush;
				}
			}
		}

		CoreExitIcon.Fill = _textColorBrush;
		CoreExitIcon.Visibility = _defaultCoreExitIconVisibility;
		CoreExitIcon.Opacity = _defaultCoreExitIconOpacity;
		_styleRenderer?.ApplyExitHighlight(CoreExitIcon, isHighlighted: false);
		CoreCustomImageEllipse.Visibility = _defaultCoreCustomImageVisibility;
		CoreCustomImageEllipse.Opacity = _defaultCoreCustomImageOpacity;
		CoreCustomImageEllipse.Effect = _defaultCoreCustomImageEffect;
	}

	private void ClearRenderedVisualsForRebuild()
	{
		ClearSubTier();
		List<UIElement> removable = new List<UIElement>();
		foreach (UIElement child in WheelCanvas.Children)
		{
			if (child != CoreGrid && child != OuterEllipse)
			{
				removable.Add(child);
			}
		}
		foreach (UIElement child in removable)
		{
			WheelCanvas.Children.Remove(child);
		}
		_sectorPaths.Clear();
		_contentPanels.Clear();
		_sectorTransforms.Clear();
		_containerTransforms.Clear();
		_sectorAngles.Clear();
		_subTierCache.Clear();
	}

	private static SolidColorBrush TintBrush(Brush brush, byte alpha)
	{
		SolidColorBrush solid = brush as SolidColorBrush;
		Color color = (solid != null) ? solid.Color : Colors.White;
		return new SolidColorBrush(Color.FromArgb(alpha, color.R, color.G, color.B));
	}

	private static bool IsBrushDark(Brush brush)
	{
		SolidColorBrush solid = brush as SolidColorBrush;
		if (solid == null)
		{
			return false;
		}
		Color color = solid.Color;
		double luminance = 0.2126 * (color.R / 255.0) + 0.7152 * (color.G / 255.0) + 0.0722 * (color.B / 255.0);
		return luminance < 0.5;
	}

	/// <summary>中心文字取色：手动模式用配置指定色；自动模式跟随配色主题笔刷，中心铺图时改按图片平均亮度取对比色。</summary>
	private Brush ResolveCoreTextBrush(double? coreImageLuminance)
	{
		AppConfig config = ConfigManager.CurrentConfig;
		if (config != null && !config.CoreTextColorAuto && !string.IsNullOrWhiteSpace(config.CoreTextColor))
		{
			try
			{
				return new SolidColorBrush((Color)ColorConverter.ConvertFromString(config.CoreTextColor));
			}
			catch
			{
			}
		}
		if (coreImageLuminance.HasValue)
		{
			return (coreImageLuminance.Value < 0.5)
				? new SolidColorBrush(Color.FromRgb(248, 250, 252))
				: new SolidColorBrush(Color.FromRgb(15, 23, 42));
		}
		return _textColorBrush ?? Brushes.White;
	}

	/// <summary>缩略解码后取样中心背景图平均亮度(0~1)，按路径+写入时间缓存，避免每次弹出轮盘重复解码。</summary>
	private static double? GetImageAverageLuminance(string path)
	{
		try
		{
			string key = path + "|" + File.GetLastWriteTimeUtc(path).Ticks;
			double cached;
			if (_coreImageLuminanceCache.TryGetValue(key, out cached))
			{
				return cached;
			}
			BitmapImage thumb = new BitmapImage();
			thumb.BeginInit();
			thumb.UriSource = new Uri(path, UriKind.Absolute);
			thumb.DecodePixelWidth = 24;
			thumb.CacheOption = BitmapCacheOption.OnLoad;
			thumb.EndInit();
			FormatConvertedBitmap converted = new FormatConvertedBitmap(thumb, PixelFormats.Bgra32, null, 0.0);
			int width = converted.PixelWidth;
			int height = converted.PixelHeight;
			if (width <= 0 || height <= 0)
			{
				return null;
			}
			byte[] pixels = new byte[width * height * 4];
			converted.CopyPixels(pixels, width * 4, 0);
			double sum = 0.0;
			for (int i = 0; i + 3 < pixels.Length; i += 4)
			{
				sum += 0.2126 * (pixels[i + 2] / 255.0) + 0.7152 * (pixels[i + 1] / 255.0) + 0.0722 * (pixels[i] / 255.0);
			}
			double luminance = sum / (width * height);
			if (_coreImageLuminanceCache.Count > 32)
			{
				_coreImageLuminanceCache.Clear();
			}
			_coreImageLuminanceCache[key] = luminance;
			return luminance;
		}
		catch
		{
			return null;
		}
	}

	private void InitializeThemeAndStyle()
	{
		string text = ConfigManager.CurrentConfig.Theme ?? "System";
		string text2 = ConfigManager.CurrentConfig.UiStyle ?? "ClassicRing";
		_innerRadius = ConfigManager.CurrentConfig.InnerRadius;
		_outerRadius = ConfigManager.CurrentConfig.WheelRadius;
		if (_innerRadius >= _outerRadius)
		{
			_innerRadius = Math.Max(0.0, _outerRadius - 20.0);
		}
		_styleRenderer = StyleRendererFactory.CreateRenderer(text2);
		_styleRenderer.Initialize(text, ConfigManager.CurrentConfig);
		_defaultSectorBrush = _styleRenderer.DefaultSectorBrush;
		_highlightSectorBrush = _styleRenderer.HighlightSectorBrush;
		_sectorBorderBrush = _styleRenderer.SectorBorderBrush;
		_highlightBorderBrush = _styleRenderer.HighlightBorderBrush;
		_textColorBrush = _styleRenderer.TextColorBrush;
		_coreBgBrush = _styleRenderer.CoreBgBrush;
		_coreBorderBrush = _styleRenderer.CoreBorderBrush;
		_borderThickness = _styleRenderer.BorderThickness;
		_highlightBorderThickness = _styleRenderer.HighlightBorderThickness;
		string text3 = ConfigManager.CurrentConfig.SubWheelUiStyle ?? text2;
		string text4 = ConfigManager.CurrentConfig.SubWheelTheme ?? text;
		if (string.IsNullOrEmpty(text4) || text4 == "FollowPrimary")
		{
			text4 = text;
		}
		if (string.IsNullOrEmpty(text3) || text3 == "FollowPrimary")
		{
			text3 = text2;
		}
		if (ConfigManager.CurrentConfig.UseIndependentSubWheelTheme || text3 != text2 || text4 != text)
		{
			try
			{
				_subStyleRenderer = StyleRendererFactory.CreateRenderer(text3);
				_subStyleRenderer.Initialize(text4, ConfigManager.CurrentConfig);
				_subDefaultSectorBrush = _subStyleRenderer.DefaultSectorBrush;
				_subHighlightSectorBrush = _subStyleRenderer.HighlightSectorBrush;
				_subSectorBorderBrush = _subStyleRenderer.SectorBorderBrush;
				_subHighlightBorderBrush = _subStyleRenderer.HighlightBorderBrush;
				_subTextColorBrush = _subStyleRenderer.TextColorBrush;
				if (text4 == "Custom")
				{
					if (!string.IsNullOrWhiteSpace(ConfigManager.CurrentConfig.SubWheelCustomSectorBg))
					{
						_subDefaultSectorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ConfigManager.CurrentConfig.SubWheelCustomSectorBg));
					}
					if (!string.IsNullOrWhiteSpace(ConfigManager.CurrentConfig.SubWheelCustomSectorBorder))
					{
						_subSectorBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ConfigManager.CurrentConfig.SubWheelCustomSectorBorder));
					}
					if (!string.IsNullOrWhiteSpace(ConfigManager.CurrentConfig.SubWheelCustomHighlightBg))
					{
						_subHighlightSectorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ConfigManager.CurrentConfig.SubWheelCustomHighlightBg));
					}
					if (!string.IsNullOrWhiteSpace(ConfigManager.CurrentConfig.SubWheelCustomHighlightBorder))
					{
						_subHighlightBorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ConfigManager.CurrentConfig.SubWheelCustomHighlightBorder));
					}
					if (!string.IsNullOrWhiteSpace(ConfigManager.CurrentConfig.SubWheelCustomText))
					{
						_subTextColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(ConfigManager.CurrentConfig.SubWheelCustomText));
					}
				}
				return;
			}
			catch
			{
				_subStyleRenderer = _styleRenderer;
				_subDefaultSectorBrush = _defaultSectorBrush;
				_subHighlightSectorBrush = _highlightSectorBrush;
				_subSectorBorderBrush = _sectorBorderBrush;
				_subHighlightBorderBrush = _highlightBorderBrush;
				_subTextColorBrush = _textColorBrush;
				return;
			}
		}
		_subStyleRenderer = _styleRenderer;
		_subDefaultSectorBrush = _defaultSectorBrush;
		_subHighlightSectorBrush = _highlightSectorBrush;
		_subSectorBorderBrush = _sectorBorderBrush;
		_subHighlightBorderBrush = _highlightBorderBrush;
		_subTextColorBrush = _textColorBrush;
	}

	private void RadialWindow_Loaded(object sender, RoutedEventArgs e)
	{
		RebuildVisualsFromCurrentConfiguration(_requestedConfigurationRevision);
		PositionWindowOnTargetMonitor();
		Dispatcher.BeginInvoke(new Action(() =>
		{
			if (!_isDisposed)
			{
				CenterOnPhysically(_centerPoint.X, _centerPoint.Y);
			}
		}), DispatcherPriority.Render);
	}

	private void RebuildVisualsFromCurrentConfiguration(long configurationRevision)
	{
		InitializeThemeAndStyle();
		ApplyLayoutFromCurrentConfiguration(_centerPoint);
		UpdateProfileLabels();
		ClearRenderedVisualsForRebuild();

		double coreRadius = ConfigManager.CurrentConfig.CoreRadius;
		CoreEllipse.Fill = _coreBgBrush;
		CoreEllipse.Stroke = _coreBorderBrush;
		string coreBackgroundPath = ConfigManager.CurrentConfig.CoreBgImagePath ?? "";
		double? coreImageLuminance = null;
		if (!string.IsNullOrEmpty(coreBackgroundPath) && File.Exists(coreBackgroundPath))
		{
			try
			{
				BitmapImage image = new BitmapImage(new Uri(coreBackgroundPath, UriKind.Absolute));
				CoreEllipse.Fill = new ImageBrush(image)
				{
					Stretch = ParseStretch(ConfigManager.CurrentConfig.CoreBgStretch),
					Opacity = ConfigManager.CurrentConfig.CoreBgOpacity
				};
				coreImageLuminance = GetImageAverageLuminance(coreBackgroundPath);
			}
			catch
			{
			}
		}
		Brush centerTextBrush = ResolveCoreTextBrush(coreImageLuminance);
		Effect coreTextShadow = IsBrushDark(centerTextBrush) ? null : (Effect)base.Resources["TextShadow"];
		CoreTitle.Foreground = TintBrush(centerTextBrush, 208);
		CoreSubtitle.Foreground = TintBrush(centerTextBrush, 128);
		CoreExitIcon.Fill = centerTextBrush;
		CoreExitIcon.Width = coreRadius * 0.42;
		CoreExitIcon.Height = coreRadius * 0.42;
		CoreTitle.FontSize = Math.Max(8.0, coreRadius / 5.0);
		CoreSubtitle.FontSize = Math.Max(6.0, coreRadius / 7.0);
		CoreTitle.Visibility = Visibility.Collapsed;
		CoreSubtitle.Visibility = Visibility.Collapsed;
		UpdateCenterIconVisuals();
		CoreSelectionOverlay.Visibility = Visibility.Collapsed;
		CoreSelectionTextPanel.Visibility = Visibility.Collapsed;
		CoreSelectionTextPanel.Width = Math.Max(32.0, Math.Min(coreRadius * 1.75, 180.0));
		double coreFontSize = ConfigManager.CurrentConfig.CoreFontSize > 0.0
			? ConfigManager.CurrentConfig.CoreFontSize
			: Math.Max(8.0, Math.Min(16.0, coreRadius / 4.0));
		CoreSelectionText.FontSize = coreFontSize;
		CoreVolumeText.FontSize = coreFontSize;
		if (!string.IsNullOrEmpty(ConfigManager.CurrentConfig.CoreFontFamily))
		{
			try
			{
				CoreSelectionText.FontFamily = new FontFamily(ConfigManager.CurrentConfig.CoreFontFamily);
			}
			catch
			{
			}
		}
		CoreSelectionText.Foreground = centerTextBrush;
		CoreSelectionText.Effect = coreTextShadow;
		CoreVolumeText.Foreground = centerTextBrush;
		CoreVolumeText.Effect = coreTextShadow;
		CoreSelectionOverlay.Fill = CreateFrostedCoreBrush(_coreBgBrush);
		Panel.SetZIndex(CoreSelectionOverlay, 20);
		Panel.SetZIndex(CoreSelectionTextPanel, 21);
		RenderStyleDecorations();
		RenderSectors();

		_renderedConfigurationRevision = configurationRevision;
		_renderedProfile = _profile;
		_renderedLayerIndex = _profile.ActiveLayerIndex;
		_hasRenderedContent = true;
	}

	private void PrepareIntroAnimation()
	{
		StopPresentationAnimations();
		// 透明 HWND 保持最终不透明度，内容层从透明/缩小状态开始入场，避免窗口级合成帧切换。
		Opacity = 1.0;
		MainGrid.Visibility = Visibility.Hidden;
		MainGrid.Opacity = 0.0;
		WindowScale.ScaleX = 0.65;
		WindowScale.ScaleY = 0.65;
	}

	private void StartIntroAnimation(long presentationVersion)
	{
		if (_isDisposed || !IsVisible || PresentationVersion != presentationVersion)
		{
			return;
		}

		BackEase easingFunction = new BackEase
		{
			EasingMode = EasingMode.EaseOut,
			Amplitude = 0.35
		};
		DoubleAnimation scaleX = new DoubleAnimation(0.65, 1.0, new Duration(TimeSpan.FromMilliseconds(110.0)))
		{
			EasingFunction = easingFunction
		};
		DoubleAnimation scaleY = new DoubleAnimation(0.65, 1.0, new Duration(TimeSpan.FromMilliseconds(110.0)))
		{
			EasingFunction = easingFunction
		};
		DoubleAnimation contentOpacity = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(90.0)));
		WindowScale.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
		WindowScale.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
		MainGrid.BeginAnimation(UIElement.OpacityProperty, contentOpacity);
	}

	internal void UpdateCenterIconVisuals()
	{
		double coreRadius = ConfigManager.CurrentConfig.CoreRadius;
		double num7 = ((ConfigManager.CurrentConfig.CoreIconScale > 0.0) ? ConfigManager.CurrentConfig.CoreIconScale : 1.0);
		double coreImageOffsetX = ConfigManager.CurrentConfig.CoreImageOffsetX;
		double coreImageOffsetY = ConfigManager.CurrentConfig.CoreImageOffsetY;
		TranslateTransform renderTransform = ((coreImageOffsetX != 0.0 || coreImageOffsetY != 0.0) ? new TranslateTransform(coreImageOffsetX, coreImageOffsetY) : null);
		bool showCoreIcon = ConfigManager.CurrentConfig.ShowCoreIcon;
		string text2 = ConfigManager.CurrentConfig.CoreIconType ?? "Exit";

		bool hasCustomPattern = IconHelper.HasCustomCenterPattern(ConfigManager.CurrentConfig);
		bool centerRendered = false;

		// 1. 若开启了自定义中心图案，则优先显示自定义图案（即使用户同时开启了中心核圆动作，也优先展示自定义图案）
		if (hasCustomPattern)
		{
			bool isCustomType = text2 == "Custom";
			IconHelper.CustomIconItem? customIconItem = null;
			if (isCustomType && !string.IsNullOrEmpty(ConfigManager.CurrentConfig.CoreCustomIconKey))
			{
				customIconItem = IconHelper.GetCustomIcons().FirstOrDefault((IconHelper.CustomIconItem c) => string.Equals(c.Key, ConfigManager.CurrentConfig.CoreCustomIconKey, StringComparison.OrdinalIgnoreCase));
			}
			bool flag = customIconItem != null && !customIconItem.IsSvg && File.Exists(customIconItem.FilePath);
			bool flag2 = !string.IsNullOrEmpty(ConfigManager.CurrentConfig.CoreCustomImagePath) && File.Exists(ConfigManager.CurrentConfig.CoreCustomImagePath);
			bool isImagePattern = ((text2 == "Image") | flag) || (flag2 && text2 != "Custom" && text2 != "Exit");
			string? text3 = (flag ? customIconItem?.FilePath : (flag2 ? ConfigManager.CurrentConfig.CoreCustomImagePath : null));

			ImageSource? loadedCoreImgSource = null;
			if (isImagePattern && !string.IsNullOrEmpty(text3))
			{
				loadedCoreImgSource = IconHelper.GetCustomImageSource(text3);
			}

			if (loadedCoreImgSource != null)
			{
				try
				{
					double num10 = coreRadius * 1.85;
					CoreCustomImageEllipse.Width = num10;
					CoreCustomImageEllipse.Height = num10;
					CoreCustomImageEllipse.RenderTransform = null;
					ImageBrush imageBrush = new ImageBrush(loadedCoreImgSource)
					{
						Stretch = Stretch.UniformToFill,
						AlignmentX = AlignmentX.Center,
						AlignmentY = AlignmentY.Center
					};
					TransformGroup transformGroup = new TransformGroup();
					if (Math.Abs(num7 - 1.0) > 0.001)
					{
						transformGroup.Children.Add(new ScaleTransform(num7, num7, num10 / 2.0, num10 / 2.0));
					}
					if (Math.Abs(coreImageOffsetX) > 0.001 || Math.Abs(coreImageOffsetY) > 0.001)
					{
						transformGroup.Children.Add(new TranslateTransform(coreImageOffsetX, coreImageOffsetY));
					}
					if (transformGroup.Children.Count > 0)
					{
						imageBrush.Transform = transformGroup;
					}
					RenderOptions.SetBitmapScalingMode((DependencyObject)(object)imageBrush, BitmapScalingMode.HighQuality);
					RenderOptions.SetEdgeMode((DependencyObject)(object)CoreCustomImageEllipse, EdgeMode.Unspecified);
					CoreCustomImageEllipse.Fill = imageBrush;
					CoreCustomImageEllipse.Visibility = Visibility.Visible;
					CoreExitIcon.Visibility = Visibility.Collapsed;
					centerRendered = true;
				}
				catch
				{
					CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
					CoreExitIcon.Visibility = Visibility.Collapsed;
				}
			}
			else
			{
				CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
				Geometry? coreIconGeometry = IconHelper.GetCoreIconGeometry(text2, ConfigManager.CurrentConfig.CoreCustomIconKey, ConfigManager.CurrentConfig.CoreCustomIconSvg);
				if (coreIconGeometry != null)
				{
					CoreExitIcon.Data = coreIconGeometry;
					CoreExitIcon.Width = coreRadius * 0.42 * num7;
					CoreExitIcon.Height = coreRadius * 0.42 * num7;
					CoreExitIcon.RenderTransform = renderTransform;
					CoreExitIcon.Fill = _textColorBrush;
					CoreExitIcon.Visibility = Visibility.Visible;
					centerRendered = true;
				}
			}
		}

		ActionItem? centerAction = _profile?.GetEffectiveCenterAction();
		if (!centerRendered && centerAction != null)
		{
			double actionIconDim = coreRadius * 0.48; // 正中居中尺寸
			double actionImgDim = coreRadius * 0.95;

			// 2.1 自定义矢量 SVG
			if (!string.IsNullOrEmpty(centerAction.CustomIconSvg))
			{
				try
				{
					CoreExitIcon.Data = Geometry.Parse(centerAction.CustomIconSvg);
					CoreExitIcon.Width = actionIconDim;
					CoreExitIcon.Height = actionIconDim;
					CoreExitIcon.RenderTransform = null; // 确保不产生偏移！
					CoreExitIcon.Fill = _textColorBrush;
					CoreExitIcon.Visibility = Visibility.Visible;
					CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
					centerRendered = true;
				}
				catch { }
			}

			// 2.2 继承应用程序图标
			if (!centerRendered && !string.IsNullOrEmpty(centerAction.InheritAppIconPath))
			{
				try
				{
					BitmapSource? appIcon = IconHelper.GetIcon(centerAction.InheritAppIconPath);
					if (appIcon != null)
					{
						CoreCustomImageEllipse.Width = actionImgDim;
						CoreCustomImageEllipse.Height = actionImgDim;
						CoreCustomImageEllipse.RenderTransform = null; // 确保不产生偏移！
						ImageBrush imgBrush = new ImageBrush(appIcon)
						{
							Stretch = Stretch.Uniform,
							AlignmentX = AlignmentX.Center,
							AlignmentY = AlignmentY.Center
						};
						RenderOptions.SetBitmapScalingMode(imgBrush, BitmapScalingMode.HighQuality);
						CoreCustomImageEllipse.Fill = imgBrush;
						CoreCustomImageEllipse.Visibility = Visibility.Visible;
						CoreExitIcon.Visibility = Visibility.Collapsed;
						centerRendered = true;
					}
				}
				catch { }
			}

			// 2.3 图标关键字 (custom: 或内置矢量)
			if (!centerRendered && !string.IsNullOrEmpty(centerAction.IconKey))
			{
				if (centerAction.IconKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
				{
					IconHelper.CustomIconItem? customIconItem = IconHelper.GetCustomIcons().FirstOrDefault(c => string.Equals(c.Key, centerAction.IconKey, StringComparison.OrdinalIgnoreCase));
					if (customIconItem != null)
					{
						if (customIconItem.IsSvg && !string.IsNullOrEmpty(customIconItem.SvgData))
						{
							try
							{
								CoreExitIcon.Data = Geometry.Parse(customIconItem.SvgData);
								CoreExitIcon.Width = actionIconDim;
								CoreExitIcon.Height = actionIconDim;
								CoreExitIcon.RenderTransform = null; // 确保不产生偏移！
								CoreExitIcon.Fill = _textColorBrush;
								CoreExitIcon.Visibility = Visibility.Visible;
								CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
								centerRendered = true;
							}
							catch { }
						}
						else if (File.Exists(customIconItem.FilePath))
						{
							try
							{
								ImageSource? customSrc = IconHelper.GetCustomImageSource(customIconItem.FilePath);
								if (customSrc != null)
								{
									CoreCustomImageEllipse.Width = actionImgDim;
									CoreCustomImageEllipse.Height = actionImgDim;
									CoreCustomImageEllipse.RenderTransform = null; // 确保不产生偏移！
									ImageBrush imgBrush = new ImageBrush(customSrc)
									{
										Stretch = Stretch.Uniform,
										AlignmentX = AlignmentX.Center,
										AlignmentY = AlignmentY.Center
									};
									RenderOptions.SetBitmapScalingMode(imgBrush, BitmapScalingMode.HighQuality);
									CoreCustomImageEllipse.Fill = imgBrush;
									CoreCustomImageEllipse.Visibility = Visibility.Visible;
									CoreExitIcon.Visibility = Visibility.Collapsed;
									centerRendered = true;
								}
							}
							catch { }
						}
					}
				}
				else
				{
					string svg = IconHelper.GetSvgPathByKey(centerAction.IconKey);
					if (!string.IsNullOrEmpty(svg))
					{
						try
						{
							CoreExitIcon.Data = Geometry.Parse(svg);
							CoreExitIcon.Width = actionIconDim;
							CoreExitIcon.Height = actionIconDim;
							CoreExitIcon.RenderTransform = null; // 确保不产生偏移！
							CoreExitIcon.Fill = _textColorBrush;
							CoreExitIcon.Visibility = Visibility.Visible;
							CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
							centerRendered = true;
						}
						catch { }
					}
				}
			}

			// 2.4 启动目标应用程序原生提取图标
			if (!centerRendered && string.Equals(centerAction.Type, "Launch", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(centerAction.Parameter))
			{
				try
				{
					BitmapSource? appIcon = IconHelper.GetIcon(centerAction.Parameter);
					if (appIcon != null)
					{
						CoreCustomImageEllipse.Width = actionImgDim;
						CoreCustomImageEllipse.Height = actionImgDim;
						CoreCustomImageEllipse.RenderTransform = null; // 确保不产生偏移！
						ImageBrush imgBrush = new ImageBrush(appIcon)
						{
							Stretch = Stretch.Uniform,
							AlignmentX = AlignmentX.Center,
							AlignmentY = AlignmentY.Center
						};
						RenderOptions.SetBitmapScalingMode(imgBrush, BitmapScalingMode.HighQuality);
						CoreCustomImageEllipse.Fill = imgBrush;
						CoreCustomImageEllipse.Visibility = Visibility.Visible;
						CoreExitIcon.Visibility = Visibility.Collapsed;
						centerRendered = true;
					}
				}
				catch { }
			}

			// 2.5 动作类型保底默认图标
			if (!centerRendered && !string.IsNullOrEmpty(centerAction.Type))
			{
				string defaultKey = centerAction.Type switch
				{
					"WebUrl" or "Url" => "Explorer",
					"Folder" or "OpenFolder" => "Folder",
					"Launch" => "Terminal",
					"Hotkey" => "Command",
					"Command" => "Terminal",
					"SwitchWindow" => "Tile",
					"System" => "Settings",
					_ => "Command"
				};
				string fallbackSvg = IconHelper.GetSvgPathByKey(defaultKey);
				if (!string.IsNullOrEmpty(fallbackSvg))
				{
					try
					{
						CoreExitIcon.Data = Geometry.Parse(fallbackSvg);
						CoreExitIcon.Width = actionIconDim;
						CoreExitIcon.Height = actionIconDim;
						CoreExitIcon.RenderTransform = null; // 确保不产生偏移！
						CoreExitIcon.Fill = _textColorBrush;
						CoreExitIcon.Visibility = Visibility.Visible;
						CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
						centerRendered = true;
					}
					catch { }
				}
			}
		}

		// 3. 既无自定义图案又未配置动作，保底展示默认 Exit 图标或隐藏
		if (!centerRendered)
		{
			if (showCoreIcon)
			{
				CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
				Geometry? coreIconGeometry = IconHelper.GetCoreIconGeometry("Exit");
				if (coreIconGeometry != null)
				{
					CoreExitIcon.Data = coreIconGeometry;
				}
				CoreExitIcon.Width = coreRadius * 0.42 * num7;
				CoreExitIcon.Height = coreRadius * 0.42 * num7;
				CoreExitIcon.RenderTransform = renderTransform;
				CoreExitIcon.Visibility = Visibility.Visible;
			}
			else
			{
				CoreCustomImageEllipse.Visibility = Visibility.Collapsed;
				CoreExitIcon.Visibility = Visibility.Collapsed;
			}
		}

		_defaultCoreExitIconVisibility = CoreExitIcon.Visibility;
		_defaultCoreCustomImageVisibility = CoreCustomImageEllipse.Visibility;
		_defaultCoreExitIconOpacity = CoreExitIcon.Opacity;
		_defaultCoreCustomImageOpacity = CoreCustomImageEllipse.Opacity;
		_defaultCoreCustomImageEffect = CoreCustomImageEllipse.Effect;
	}

	private Storyboard? _layerBadgeStoryboard;

	public void SwitchToLayer(int layerIndex)
	{
		if (_profile == null || _profile.Layers == null || _profile.Layers.Count == 0)
		{
			return;
		}
		if (layerIndex < 0 || layerIndex >= _profile.Layers.Count)
		{
			return;
		}

		_profile.ActiveLayerIndex = layerIndex;
		_profile.SyncRootPropertiesFromActiveLayer();

		_currentHighlightedSector = -999;
		_currentHighlightedSubSector = -1;
		_activeSubTierParentSector = -1;
		_subTierCache.Clear();

		RenderSectors();
		UpdateCenterIconVisuals();
		_renderedLayerIndex = layerIndex;

		if (_profile.Layers.Count > 1 && LayerIndicatorBadge != null && LayerIndicatorText != null)
		{
			AppConfig config = ConfigManager.CurrentConfig;
			if (config?.ShowLayerIndicator == false)
			{
				LayerIndicatorBadge.Visibility = Visibility.Collapsed;
				return;
			}

			ApplyLayerIndicatorStyle();

			WheelLayer curLayer = _profile.Layers[layerIndex];
			LayerIndicatorText.Text = $"{curLayer.Name} ({layerIndex + 1}/{_profile.Layers.Count})";
			LayerIndicatorBadge.Visibility = Visibility.Visible;

			_layerBadgeStoryboard?.Stop();
			_layerBadgeStoryboard = new Storyboard();

			DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, new Duration(TimeSpan.FromMilliseconds(80)));
			Storyboard.SetTarget(fadeIn, LayerIndicatorBadge);
			Storyboard.SetTargetProperty(fadeIn, new PropertyPath(UIElement.OpacityProperty));
			_layerBadgeStoryboard.Children.Add(fadeIn);

			double durationMs = Math.Max(300.0, config?.LayerIndicatorDurationMs ?? 1200.0);
			DoubleAnimation fadeOut = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(260)))
			{
				BeginTime = TimeSpan.FromMilliseconds(durationMs)
			};
			Storyboard.SetTarget(fadeOut, LayerIndicatorBadge);
			Storyboard.SetTargetProperty(fadeOut, new PropertyPath(UIElement.OpacityProperty));
			_layerBadgeStoryboard.Children.Add(fadeOut);

			_layerBadgeStoryboard.Begin();
		}
	}

	private static bool TryParseSolidBrush(string? hex, out SolidColorBrush brush)
	{
		brush = Brushes.Transparent;
		if (string.IsNullOrWhiteSpace(hex)) return false;
		try
		{
			var colorObj = System.Windows.Media.ColorConverter.ConvertFromString(hex);
			if (colorObj is Color c)
			{
				brush = new SolidColorBrush(c);
				brush.Freeze();
				return true;
			}
		}
		catch
		{
		}
		return false;
	}

	private void ApplyLayerIndicatorStyle()
	{
		if (LayerIndicatorBadge == null || LayerIndicatorText == null || LayerIndicatorIcon == null)
		{
			return;
		}

		AppConfig config = ConfigManager.CurrentConfig;
		if (config == null) return;

		if (!config.ShowLayerIndicator)
		{
			LayerIndicatorBadge.Visibility = Visibility.Collapsed;
			return;
		}

		double offsetY = Math.Max(0.0, config.LayerIndicatorOffsetY);
		LayerIndicatorBadge.Margin = new Thickness(0, offsetY, 0, 0);

		double cornerRadius = Math.Max(0.0, config.LayerIndicatorCornerRadius);
		LayerIndicatorBadge.CornerRadius = new CornerRadius(cornerRadius);

		double fontSize = Math.Max(8.0, config.LayerIndicatorFontSize);
		LayerIndicatorText.FontSize = fontSize;
		LayerIndicatorIcon.FontSize = fontSize + 0.5;

		string iconStr = config.LayerIndicatorIcon;
		if (string.Equals(iconStr, "None", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(iconStr))
		{
			LayerIndicatorIcon.Visibility = Visibility.Collapsed;
		}
		else
		{
			LayerIndicatorIcon.Text = iconStr;
			LayerIndicatorIcon.Visibility = Visibility.Visible;
		}

		if (string.Equals(config.LayerIndicatorStyle, "FollowTheme", StringComparison.OrdinalIgnoreCase))
		{
			LayerIndicatorBadge.Background = _coreBgBrush ?? new SolidColorBrush(Color.FromArgb(230, 15, 23, 42));
			LayerIndicatorBadge.BorderBrush = _highlightBorderBrush ?? new SolidColorBrush(Color.FromRgb(56, 189, 248));
			LayerIndicatorText.Foreground = _textColorBrush ?? Brushes.White;
			LayerIndicatorIcon.Foreground = _highlightBorderBrush ?? Brushes.White;
		}
		else
		{
			if (TryParseSolidBrush(config.LayerIndicatorBg, out var bgBrush))
			{
				LayerIndicatorBadge.Background = bgBrush;
			}
			else
			{
				LayerIndicatorBadge.Background = new SolidColorBrush(Color.FromArgb(230, 15, 23, 42));
			}

			if (TryParseSolidBrush(config.LayerIndicatorBorder, out var borderBrush))
			{
				LayerIndicatorBadge.BorderBrush = borderBrush;
			}
			else
			{
				LayerIndicatorBadge.BorderBrush = new SolidColorBrush(Color.FromRgb(56, 189, 248));
			}

			if (TryParseSolidBrush(config.LayerIndicatorTextColor, out var textBrush))
			{
				LayerIndicatorText.Foreground = textBrush;
				LayerIndicatorIcon.Foreground = textBrush;
			}
			else
			{
				LayerIndicatorText.Foreground = Brushes.White;
				LayerIndicatorIcon.Foreground = Brushes.White;
			}
		}
	}

	private void RenderStyleDecorations()
	{
		_ = ConfigManager.CurrentConfig.UiStyle;
		double cx = _canvasCenter;
		double cy = _canvasCenter;
		double wheelRadius = ConfigManager.CurrentConfig.WheelRadius;
		double coreRadius = ConfigManager.CurrentConfig.CoreRadius;
		List<UIElement> list = new List<UIElement>();
		foreach (UIElement child in WheelCanvas.Children)
		{
			if (child is FrameworkElement { Tag: not null } frameworkElement && frameworkElement.Tag.ToString().StartsWith("Deco_"))
			{
				list.Add(child);
			}
		}
		foreach (UIElement item in list)
		{
			WheelCanvas.Children.Remove(item);
		}
		CoreEllipse.Visibility = Visibility.Visible;
		OuterEllipse.Visibility = Visibility.Collapsed;
		System.Windows.Shapes.Path path = CoreGrid.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault((System.Windows.Shapes.Path p) => p.Name == "DynamicGearPath");
		if (path != null)
		{
			CoreGrid.Children.Remove(path);
		}
		Grid grid = CoreGrid.Children.OfType<Grid>().FirstOrDefault((Grid g) => g.Name == "DynamicPawGrid");
		if (grid != null)
		{
			CoreGrid.Children.Remove(grid);
		}
		Grid grid2 = CoreGrid.Children.OfType<Grid>().FirstOrDefault((Grid g) => g.Name == "DynamicTechGrid");
		if (grid2 != null)
		{
			CoreGrid.Children.Remove(grid2);
		}
		int num = CoreGrid.Children.IndexOf(CoreTextPanel);
		if (num < 0)
		{
			num = 0;
		}
		if (_styleRenderer != null)
		{
			_styleRenderer.RenderDecorations(WheelCanvas, CoreGrid, cx, cy, wheelRadius, coreRadius, num);
		}
	}

	private void RenderSectors()
	{
		int sectorCount = _profile.SectorCount;
		double num = 360.0 / (double)sectorCount;
		double num2 = _canvasCenter;
		double num3 = _canvasCenter;
		string shape = ConfigManager.CurrentConfig.Shape ?? "Original";
		double gap = Math.Max(0.0, ConfigManager.CurrentConfig.SectorGap);
		double cornerRadius = Math.Max(0.0, ConfigManager.CurrentConfig.SectorCornerRadius);
		string text = ConfigManager.CurrentConfig.IconLayoutMode ?? "IconAndText";
		bool flag = ConfigManager.CurrentConfig.ShowText && text != "IconOnly";
		_sectorPaths.Clear();
		_contentPanels.Clear();
		_sectorTransforms.Clear();
		_containerTransforms.Clear();
		_sectorAngles.Clear();
		List<UIElement> list = new List<UIElement>();
		foreach (UIElement child in WheelCanvas.Children)
		{
			if (child != CoreGrid && child != OuterEllipse && (!(child is FrameworkElement { Tag: not null } frameworkElement) || !frameworkElement.Tag.ToString().StartsWith("Deco_")))
			{
				list.Add(child);
			}
		}
		foreach (UIElement item in list)
		{
			WheelCanvas.Children.Remove(item);
		}
		for (int i = 0; i < sectorCount; i++)
		{
			double num4 = (double)i * num;
			double startAngle = num4 - num / 2.0;
			double endAngle = num4 + num / 2.0;
			double num5 = num4 * (Math.PI / 180.0);
			double num6 = (_innerRadius + _outerRadius) / 2.0;
			double num7 = num2 + Math.Cos(num5) * num6;
			double num8 = num3 + Math.Sin(num5) * num6;
			Geometry data = IconHelper.CreateAdvancedSectorGeometry(num2, num3, startAngle, endAngle, _innerRadius, _outerRadius, shape, gap, cornerRadius);
			TranslateTransform translateTransform = new TranslateTransform(0.0, 0.0);
			System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
			{
				Data = data,
				Fill = _defaultSectorBrush,
				Stroke = _sectorBorderBrush,
				StrokeThickness = _borderThickness,
				RenderTransform = translateTransform,
				Tag = i
			};
			Panel.SetZIndex(path, 1);
			WheelCanvas.Children.Insert(0, path);
			_styleRenderer?.ApplySectorHighlight(path, isHighlighted: false);
			_sectorPaths.Add(path);
			_sectorTransforms.Add(translateTransform);
			_sectorAngles.Add(num5);
			double width2 = sectorCount switch
			{
				4 => 124.0, 
				12 => 66.0, 
				_ => 100.0, 
			};
			double height = sectorCount switch
			{
				4 => 76.0, 
				12 => 52.0, 
				_ => 68.0, 
			};
			TranslateTransform translateTransform2 = new TranslateTransform(0.0, 0.0);
			Grid grid = new Grid
			{
				Width = width2,
				Height = height,
				RenderTransform = translateTransform2
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Vertical,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			grid.Children.Add(stackPanel);
			ActionItem? currentAction = _profile.GetEffectiveAction(i);
			string sectorLayout = text;
			if (currentAction != null && !string.IsNullOrWhiteSpace(currentAction.LayoutMode) && currentAction.LayoutMode != "Inherit")
			{
				sectorLayout = currentAction.LayoutMode;
			}
			bool shouldShowIcon = (sectorLayout != "TextOnly");
			bool shouldShowText = (sectorLayout != "IconOnly");

			Brush sectorTextColorBrush = _textColorBrush;
			if (currentAction != null && !string.IsNullOrWhiteSpace(currentAction.CustomTextColor))
			{
				try
				{
					sectorTextColorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(currentAction.CustomTextColor));
				}
				catch
				{
				}
			}

			string text2 = "未设置";
			string text3 = "Hotkey";
			string text4 = "";
			string iconKey = "";
			string text5 = "";
			if (currentAction != null)
			{
				text2 = currentAction.Name ?? "";
				text3 = currentAction.Type ?? "Hotkey";
				text4 = currentAction.Parameter ?? "";
				iconKey = currentAction.IconKey ?? "";
				text5 = currentAction.CustomIconSvg ?? "";
			}
			FrameworkElement frameworkElement2 = null;
			if (shouldShowIcon)
			{
				double baseIconSize = (currentAction != null && currentAction.CustomIconSize.HasValue && currentAction.CustomIconSize.Value > 0.0)
					? currentAction.CustomIconSize.Value
					: ((ConfigManager.CurrentConfig.SectorIconSize > 0.0) ? ConfigManager.CurrentConfig.SectorIconSize : 20.0);
				double num10 = sectorCount switch
				{
					4 => 1.2, 
					12 => 0.82, 
					_ => 1.0, 
				};
				double num11 = ((sectorLayout == "IconOnly") ? (baseIconSize * 1.35) : baseIconSize) * num10;
				if (!string.IsNullOrEmpty(text5))
				{
					try
					{
						frameworkElement2 = new System.Windows.Shapes.Path
						{
							Data = Geometry.Parse(text5),
							Fill = sectorTextColorBrush,
							Stretch = Stretch.Uniform,
							Width = num11,
							Height = num11,
							Margin = new Thickness(0.0, 0.0, 0.0, shouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
					catch
					{
					}
				}
				if (frameworkElement2 == null && !string.IsNullOrEmpty(currentAction?.InheritAppIconPath))
				{
					BitmapSource icon = IconHelper.GetIcon(currentAction.InheritAppIconPath);
					if (icon != null)
					{
						frameworkElement2 = new Image
						{
							Source = icon,
							Width = num11 + 4.0,
							Height = num11 + 4.0,
							Stretch = Stretch.Uniform,
							Margin = new Thickness(0.0, 0.0, 0.0, shouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
				if (frameworkElement2 == null && !string.IsNullOrEmpty(iconKey))
				{
					if (iconKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
					{
						IconHelper.CustomIconItem customIconItem = IconHelper.GetCustomIcons().FirstOrDefault((IconHelper.CustomIconItem c) => c.Key == iconKey);
						if (customIconItem != null)
						{
							frameworkElement2 = ((!customIconItem.IsSvg) ? ((FrameworkElement)new Image
							{
								Width = num11,
								Height = num11,
								Stretch = Stretch.Uniform,
								Margin = new Thickness(0.0, 0.0, 0.0, shouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center,
								Source = IconHelper.GetCustomImageSource(customIconItem.FilePath)
							}) : ((FrameworkElement)new System.Windows.Shapes.Path
							{
								Data = Geometry.Parse(customIconItem.SvgData),
								Fill = sectorTextColorBrush,
								Stretch = Stretch.Uniform,
								Width = num11,
								Height = num11,
								Margin = new Thickness(0.0, 0.0, 0.0, shouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center
							}));
						}
					}
					else
					{
						string svgPathByKey = IconHelper.GetSvgPathByKey(iconKey);
						if (!string.IsNullOrEmpty(svgPathByKey))
						{
							frameworkElement2 = new System.Windows.Shapes.Path
							{
								Data = Geometry.Parse(svgPathByKey),
								Fill = sectorTextColorBrush,
								Stretch = Stretch.Uniform,
								Width = num11,
								Height = num11,
								Margin = new Thickness(0.0, 0.0, 0.0, shouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center
							};
						}
					}
				}
				if (frameworkElement2 == null && (text3 == "Launch" || text3 == "App") && !string.IsNullOrEmpty(text4))
				{
					BitmapSource icon = IconHelper.GetIcon(text4);
					if (icon != null)
					{
						frameworkElement2 = new Image
						{
							Source = icon,
							Width = num11 + 4.0,
							Height = num11 + 4.0,
							Stretch = Stretch.Uniform,
							Margin = new Thickness(0.0, 0.0, 0.0, shouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
				if (frameworkElement2 == null && text3 == "SwitchWindow")
				{
					frameworkElement2 = BuildSwitchWindowIcon(text4, num11, shouldShowText);
				}
				if (frameworkElement2 == null)
				{
					string vectorIconPath = GetVectorIconPath(text3, text4);
					if (!string.IsNullOrEmpty(vectorIconPath))
					{
						frameworkElement2 = new System.Windows.Shapes.Path
						{
							Data = Geometry.Parse(vectorIconPath),
							Fill = sectorTextColorBrush,
							Stretch = Stretch.Uniform,
							Width = num11,
							Height = num11,
							Margin = new Thickness(0.0, 0.0, 0.0, shouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
			}

			TextBlock? textElement = null;
			if (shouldShowText && !string.IsNullOrEmpty(text2))
			{
				double baseFontSize = (currentAction != null && currentAction.CustomFontSize.HasValue && currentAction.CustomFontSize.Value > 0.0)
					? currentAction.CustomFontSize.Value
					: ((ConfigManager.CurrentConfig.SectorFontSize > 0.0) ? ConfigManager.CurrentConfig.SectorFontSize : 11.0);
				double num13 = ((sectorLayout == "TextOnly") ? (baseFontSize + 1.0) : baseFontSize);
				switch (sectorCount)
				{
				case 12:
					num13 = Math.Min(num13, 10.0);
					break;
				case 4:
					num13 = Math.Max(num13, 12.0);
					break;
				}

				// 文字长度与语言弹性自适应 (Auto Font-Fit)
				int charLen = text2.Length;
				bool isPureAscii = text2.All(c => c < 128);
				if (sectorCount == 12)
				{
					if (charLen > 8 || (isPureAscii && charLen > 7))
					{
						num13 = Math.Max(7.5, num13 * 0.82);
					}
					else if (charLen > 5)
					{
						num13 = Math.Max(8.2, num13 * 0.90);
					}
				}
				else if (sectorCount == 8)
				{
					if (charLen > 12 || (isPureAscii && charLen > 10))
					{
						num13 = Math.Max(8.5, num13 * 0.85);
					}
					else if (charLen > 7)
					{
						num13 = Math.Max(9.2, num13 * 0.92);
					}
				}
				else // 4 键
				{
					if (charLen > 14)
					{
						num13 = Math.Max(10.0, num13 * 0.88);
					}
				}

				double maxWidth = sectorCount switch
				{
					4 => 128.0, 
					12 => 68.0, 
					_ => 102.0, 
				};
				string sectorFont = (currentAction != null && !string.IsNullOrWhiteSpace(currentAction.CustomFontFamily))
					? currentAction.CustomFontFamily
					: (ConfigManager.CurrentConfig.WheelFontFamily ?? "Microsoft YaHei UI, Segoe UI");
				textElement = new TextBlock
				{
					Text = text2,
					Foreground = sectorTextColorBrush,
					FontSize = num13,
					FontFamily = new FontFamily(sectorFont),
					FontWeight = FontWeights.Medium,
					TextAlignment = TextAlignment.Center,
					TextWrapping = TextWrapping.Wrap,
					TextTrimming = TextTrimming.CharacterEllipsis,
					MaxWidth = maxWidth,
					MaxHeight = 36.0,
					LineHeight = Math.Max(9.0, num13 * 1.15),
					LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
					Effect = (Effect)base.Resources["TextShadow"]
				};
				TextOptions.SetTextFormattingMode((DependencyObject)(object)textElement, (TextFormattingMode)1);
				TextOptions.SetTextRenderingMode((DependencyObject)(object)textElement, TextRenderingMode.ClearType);
			}

			string placement = (!string.IsNullOrWhiteSpace(currentAction?.CustomTextPlacement))
				? currentAction.CustomTextPlacement
				: (ConfigManager.CurrentConfig.SectorTextPlacement ?? "Below");
			double offX = (currentAction != null && currentAction.CustomTextOffsetX.HasValue)
				? currentAction.CustomTextOffsetX.Value
				: ConfigManager.CurrentConfig.SectorTextOffsetX;
			double offY = (currentAction != null && currentAction.CustomTextOffsetY.HasValue)
				? currentAction.CustomTextOffsetY.Value
				: ConfigManager.CurrentConfig.SectorTextOffsetY;

			if (textElement != null && (Math.Abs(offX) > 0.001 || Math.Abs(offY) > 0.001))
			{
				textElement.RenderTransform = new TranslateTransform(offX, offY);
			}

			if (placement == "Above")
			{
				if (textElement != null)
				{
					textElement.Margin = new Thickness(0.0, 0.0, 0.0, (frameworkElement2 != null) ? 1.0 : 0.0);
					stackPanel.Children.Add(textElement);
				}
				if (frameworkElement2 != null)
				{
					frameworkElement2.Margin = new Thickness(0.0, 0.0, 0.0, 0.0);
					stackPanel.Children.Add(frameworkElement2);
				}
			}
			else
			{
				if (frameworkElement2 != null)
				{
					frameworkElement2.Margin = new Thickness(0.0, 0.0, 0.0, (textElement != null) ? 2.0 : 0.0);
					stackPanel.Children.Add(frameworkElement2);
				}
				if (textElement != null)
				{
					textElement.Margin = new Thickness(0.0, 1.0, 0.0, 0.0);
					stackPanel.Children.Add(textElement);
				}
			}
			if (currentAction?.IsInherited == true)
			{
				stackPanel.Opacity = 0.88;
			}
			Canvas.SetLeft(grid, num7 - grid.Width / 2.0);
			Canvas.SetTop(grid, num8 - grid.Height / 2.0);
			Panel.SetZIndex(grid, 10);
			WheelCanvas.Children.Add(grid);
			_contentPanels.Add(stackPanel);
			_containerTransforms.Add(translateTransform2);
		}
	}

	private string? GetVectorIconPath(string type, string parameter)
	{
		switch (type)
		{
		case "Folder":
		case "OpenFolder":
			return IconHelper.GetSvgPathByKey("Folder");
		case "Ocr":
		case "ScreenOcr":
			return "M2,4C2,2.89 2.9,2 4,2H8V4H4V8H2V4M22,4V8H20V4H16V2H20C21.1,2 22,2.89 22,4M2,20V16H4V20H8V22H4C2.9,22 2,21.1 2,20M20,20H16V22H20C21.1,22 22,21.1 22,20V16H20V20M7,7H17V9H13V17H11V9H7V7Z";
		case "Hotkey":
			return "M19,15H5V5H19M19,3H5C3.89,3 3,3.89 3,5V15C3,16.1 3.89,17 5,17H19C20.1,17 21,16.1 21,15V5C21,3.89 20.1,3 19,3M2,18H22V20H2V18Z";
		case "Command":
			return IconHelper.GetSvgPathByKey("Command");
		case "SwitchWindow":
			// 默认程序图标（窗口切换）：四格程序窗格
			return "M4,4H11V11H4V4M13,4H20V11H13V4M4,13H11V20H4V13M13,13H20V20H13V13Z";
		case "System":
			if (!string.IsNullOrEmpty(parameter))
			{
				switch (parameter.Trim().ToLower())
				{
				case "lock":
					return IconHelper.GetSvgPathByKey("Lock");
				case "volumeup":
					return IconHelper.GetSvgPathByKey("VolumeUp");
				case "volumedown":
					return IconHelper.GetSvgPathByKey("VolumeDown");
				case "volumemute":
					return IconHelper.GetSvgPathByKey("VolumeMute");
				case "showdesktop":
					return IconHelper.GetSvgPathByKey("ShowDesktop");
				case "screenshot":
					return IconHelper.GetSvgPathByKey("Screenshot");
				}
			}
			break;
		}
		return null;
	}

	private Geometry CreateSectorGeometry(double startAngleDegrees, double endAngleDegrees, double innerRadius, double outerRadius)
	{
		//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
		//IL_00dc: Unknown result type (might be due to invalid IL or missing references)
		//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
		//IL_00fc: Unknown result type (might be due to invalid IL or missing references)
		//IL_0107: Unknown result type (might be due to invalid IL or missing references)
		//IL_010b: Unknown result type (might be due to invalid IL or missing references)
		//IL_0125: Unknown result type (might be due to invalid IL or missing references)
		double num = startAngleDegrees * (Math.PI / 180.0);
		double num2 = endAngleDegrees * (Math.PI / 180.0);
		double num3 = _canvasCenter;
		double num4 = _canvasCenter;
		Point val = default(Point);
		val = new Point(num3 + Math.Cos(num) * outerRadius, num4 + Math.Sin(num) * outerRadius);
		Point point = default(Point);
		point = new Point(num3 + Math.Cos(num2) * outerRadius, num4 + Math.Sin(num2) * outerRadius);
		Point point2 = default(Point);
		point2 = new Point(num3 + Math.Cos(num2) * innerRadius, num4 + Math.Sin(num2) * innerRadius);
		Point point3 = default(Point);
		point3 = new Point(num3 + Math.Cos(num) * innerRadius, num4 + Math.Sin(num) * innerRadius);
		bool isLargeArc = Math.Abs(endAngleDegrees - startAngleDegrees) > 180.0;
		StreamGeometry streamGeometry = new StreamGeometry();
		using (StreamGeometryContext streamGeometryContext = streamGeometry.Open())
		{
			streamGeometryContext.BeginFigure(val, isFilled: true, isClosed: true);
			streamGeometryContext.ArcTo(point, new Size(outerRadius, outerRadius), 0.0, isLargeArc, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
			streamGeometryContext.LineTo(point2, isStroked: true, isSmoothJoin: false);
			streamGeometryContext.ArcTo(point3, new Size(innerRadius, innerRadius), 0.0, isLargeArc, SweepDirection.Counterclockwise, isStroked: true, isSmoothJoin: true);
			streamGeometryContext.LineTo(val, isStroked: true, isSmoothJoin: false);
		}
		((Freezable)streamGeometry).Freeze();
		return streamGeometry;
	}

	/// <summary>音量"拖距调音"实时预览：isActive=true 显示中心百分比，否则隐藏并恢复选中动作文字。</summary>
	public void SetVolumePreview(int percent, bool isActive)
	{
		if (isActive)
		{
			CoreVolumeText.Text = "🔊 " + percent + "%";
			CoreVolumeText.Visibility = Visibility.Visible;
			Panel.SetZIndex(CoreVolumeText, 22);
			// 调音期间隐藏选中动作遮罩层，避免与音量百分比重叠
			CoreSelectionTextPanel.Visibility = Visibility.Collapsed;
			CoreSelectionOverlay.Visibility = Visibility.Collapsed;
			return;
		}
		CoreVolumeText.Visibility = Visibility.Collapsed;
		if (_currentHighlightedSector >= 0)
		{
			UpdateCoreSelectionDisplay(_currentHighlightedSector, _currentHighlightedSubSector);
		}
	}

	public void SetOuterEscapeState(bool isEscaped)
	{
		if (_isOuterEscaped != isEscaped)
		{
			_isOuterEscaped = isEscaped;
			DoubleAnimation animation = new DoubleAnimation
			{
				From = Opacity,
				To = (isEscaped ? 0.38 : 1.0),
				Duration = TimeSpan.FromMilliseconds(120.0),
				EasingFunction = new QuadraticEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			MainGrid.BeginAnimation(UIElement.OpacityProperty, animation);
		}
	}

	private void ClearSubTier()
	{
		for (int i = 0; i < _subSectorPaths.Count; i++)
		{
			System.Windows.Shapes.Path subSectorPath = _subSectorPaths[i];
			subSectorPath.BeginAnimation(UIElement.OpacityProperty, null);
			subSectorPath.Opacity = 0.0;
			subSectorPath.Visibility = Visibility.Collapsed;
			if (i < _subSectorTransforms.Count)
			{
				TranslateTransform transform = _subSectorTransforms[i];
				transform.BeginAnimation(TranslateTransform.XProperty, null);
				transform.BeginAnimation(TranslateTransform.YProperty, null);
				transform.X = 0.0;
				transform.Y = 0.0;
			}
		}
		for (int i = 0; i < _subContentContainers.Count; i++)
		{
			Grid subContentContainer = _subContentContainers[i];
			subContentContainer.BeginAnimation(UIElement.OpacityProperty, null);
			subContentContainer.Opacity = 0.0;
			subContentContainer.Visibility = Visibility.Collapsed;
			if (i < _subContainerTransforms.Count)
			{
				TranslateTransform transform = _subContainerTransforms[i];
				transform.BeginAnimation(TranslateTransform.XProperty, null);
				transform.BeginAnimation(TranslateTransform.YProperty, null);
				transform.X = 0.0;
				transform.Y = 0.0;
			}
		}
		_subSectorPaths.Clear();
		_subContentContainers.Clear();
		_subSectorTransforms.Clear();
		_subContainerTransforms.Clear();
		_subSectorAngles.Clear();
		_activeSubTierParentSector = -1;
		_currentHighlightedSubSector = -1;
	}

	private void ActivateCachedSubTier(int parentIndex, SubTierVisuals visuals)
	{
		_activeSubTierParentSector = parentIndex;
		_subSectorPaths.AddRange(visuals.Paths);
		_subContentContainers.AddRange(visuals.Containers);
		_subSectorTransforms.AddRange(visuals.PathTransforms);
		_subContainerTransforms.AddRange(visuals.ContainerTransforms);
		_subSectorAngles.AddRange(visuals.Angles);

		Duration duration = new Duration(TimeSpan.FromMilliseconds(110.0));
		DoubleAnimation fadeIn = new DoubleAnimation(0.0, 1.0, duration)
		{
			EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
		};
		for (int i = 0; i < _subSectorPaths.Count; i++)
		{
			System.Windows.Shapes.Path path = _subSectorPaths[i];
			path.Visibility = Visibility.Visible;
			path.Opacity = 0.0;
			path.BeginAnimation(UIElement.OpacityProperty, fadeIn.Clone());
			if (i < _subSectorTransforms.Count)
			{
				_subSectorTransforms[i].BeginAnimation(TranslateTransform.XProperty, null);
				_subSectorTransforms[i].BeginAnimation(TranslateTransform.YProperty, null);
				_subSectorTransforms[i].X = 0.0;
				_subSectorTransforms[i].Y = 0.0;
			}
		}
		for (int i = 0; i < _subContentContainers.Count; i++)
		{
			Grid container = _subContentContainers[i];
			container.Visibility = Visibility.Visible;
			container.Opacity = 0.0;
			container.BeginAnimation(UIElement.OpacityProperty, fadeIn.Clone());
			if (i < _subContainerTransforms.Count)
			{
				_subContainerTransforms[i].BeginAnimation(TranslateTransform.XProperty, null);
				_subContainerTransforms[i].BeginAnimation(TranslateTransform.YProperty, null);
				_subContainerTransforms[i].X = 0.0;
				_subContainerTransforms[i].Y = 0.0;
			}
		}
	}

	private void ShowSubTier(int parentIndex)
	{
		if (ConfigManager.CurrentConfig.SubmenuStyle == "Fan") { RenderFanSubtier(parentIndex); return; }
		ClearSubTier();
		if (!ConfigManager.CurrentConfig.EnableMultiTier || parentIndex < 0 || parentIndex >= _profile.SectorCount)
		{
			return;
		}
		ActionItem? actionItem = _profile.GetEffectiveAction(parentIndex);
		if (actionItem == null || actionItem.SubActions == null || actionItem.SubActions.Count == 0)
		{
			return;
		}
		if (_subTierCache.TryGetValue(parentIndex, out SubTierVisuals? cachedVisuals))
		{
			ActivateCachedSubTier(parentIndex, cachedVisuals);
			return;
		}
		int sectorCount = _profile.SectorCount;
		double num = 360.0 / (double)sectorCount;
		double num2 = _canvasCenter;
		double num3 = _canvasCenter;
		string shape = ConfigManager.CurrentConfig.Shape ?? "Original";
		string text = ConfigManager.CurrentConfig.IconLayoutMode ?? "IconAndText";
		bool flag = ConfigManager.CurrentConfig.ShowText && text != "IconOnly";
		double num4 = ((ConfigManager.CurrentConfig.SubWheelOuterRadius > 0.0) ? ConfigManager.CurrentConfig.SubWheelOuterRadius : (_outerRadius * 1.55));
		double num5 = ((ConfigManager.CurrentConfig.SubWheelInnerGap >= 0.0) ? ConfigManager.CurrentConfig.SubWheelInnerGap : Math.Max(0.0, ConfigManager.CurrentConfig.SectorGap));
		double num6 = _outerRadius + num5 + 2.0;
		double cornerRadius = ((ConfigManager.CurrentConfig.SubWheelCornerRadius >= 0.0) ? ConfigManager.CurrentConfig.SubWheelCornerRadius : Math.Max(0.0, ConfigManager.CurrentConfig.SectorCornerRadius));
		double num7 = ((ConfigManager.CurrentConfig.SubWheelIconSize > 0.0) ? ConfigManager.CurrentConfig.SubWheelIconSize : ((text == "IconOnly") ? 22.0 : 17.0));
		double fontSize = ((ConfigManager.CurrentConfig.SubWheelFontSize > 0.0) ? ConfigManager.CurrentConfig.SubWheelFontSize : Math.Max(8.5, ConfigManager.CurrentConfig.SectorFontSize - 1.0));
		double num8 = (double)parentIndex * num - num / 2.0;
		List<ActionItem> subActions = actionItem.SubActions;
		int count = subActions.Count;
		double num9 = num / (double)count;
		_activeSubTierParentSector = parentIndex;
		for (int i = 0; i < count; i++)
		{
			double num10 = num8 + (double)i * num9;
			double num11 = num10 + num9;
			double num12 = (num10 + num11) / 2.0 * (Math.PI / 180.0);
			double num13 = (num6 + num4) / 2.0;
			double num14 = num2 + Math.Cos(num12) * num13;
			double num15 = num3 + Math.Sin(num12) * num13;
			Geometry data = IconHelper.CreateAdvancedSectorGeometry(num2, num3, num10, num11, num6, num4, shape, num5, cornerRadius);
			ScaleTransform scaleTransform = new ScaleTransform(0.75, 0.75, num2, num3);
			TranslateTransform translateTransform = new TranslateTransform(0.0, 0.0);
			TransformGroup transformGroup = new TransformGroup();
			transformGroup.Children.Add(scaleTransform);
			transformGroup.Children.Add(translateTransform);
			System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
			{
				Data = data,
				Fill = _subDefaultSectorBrush,
				Stroke = _subSectorBorderBrush,
				StrokeThickness = _borderThickness,
				Tag = $"sub_{parentIndex}_{i}",
				Opacity = 0.0,
				RenderTransform = transformGroup
			};
			Panel.SetZIndex(path, 15);
			WheelCanvas.Children.Add(path);
			_subSectorPaths.Add(path);
			_subSectorTransforms.Add(translateTransform);
			_subSectorAngles.Add(num12);
			double num16 = ((count >= 4) ? 76.0 : 92.0);
			double num17 = ((count >= 4) ? 54.0 : 64.0);
			ScaleTransform scaleTransform2 = new ScaleTransform(0.75, 0.75, num16 / 2.0, num17 / 2.0);
			TranslateTransform translateTransform2 = new TranslateTransform(0.0, 0.0);
			TransformGroup transformGroup2 = new TransformGroup();
			transformGroup2.Children.Add(scaleTransform2);
			transformGroup2.Children.Add(translateTransform2);
			Grid grid = new Grid
			{
				Width = num16,
				Height = num17,
				Opacity = 0.0,
				RenderTransform = transformGroup2
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Vertical,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			grid.Children.Add(stackPanel);
			ActionItem actionItem2 = subActions[i];
			string text2 = actionItem2?.Name ?? "子动作";
			string text3 = actionItem2?.Type ?? "Hotkey";
			string text4 = actionItem2?.Parameter ?? "";
			string iconKey = actionItem2?.IconKey ?? "";
			string text5 = actionItem2?.CustomIconSvg ?? "";

			string subLayout = (actionItem2 != null && !string.IsNullOrWhiteSpace(actionItem2.LayoutMode) && actionItem2.LayoutMode != "Inherit")
				? actionItem2.LayoutMode
				: text;
			bool subShouldShowIcon = (subLayout != "TextOnly");
			bool subShouldShowText = (subLayout != "IconOnly");
			Brush subTextColor = _subTextColorBrush;
			if (actionItem2 != null && !string.IsNullOrWhiteSpace(actionItem2.CustomTextColor))
			{
				try
				{
					subTextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(actionItem2.CustomTextColor));
				}
				catch
				{
				}
			}
			double subIconSize = ((actionItem2 != null && actionItem2.CustomIconSize.HasValue && actionItem2.CustomIconSize.Value > 0.0)
				? actionItem2.CustomIconSize.Value
				: num7) * ((subLayout == "IconOnly") ? 1.3 : 1.0);

			FrameworkElement frameworkElement = null;
			if (subShouldShowIcon)
			{
				if (!string.IsNullOrEmpty(text5))
				{
					try
					{
						frameworkElement = new System.Windows.Shapes.Path
						{
							Data = Geometry.Parse(text5),
							Fill = subTextColor,
							Stretch = Stretch.Uniform,
							Width = subIconSize,
							Height = subIconSize,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
					catch
					{
					}
				}
				if (frameworkElement == null && !string.IsNullOrEmpty(actionItem2?.InheritAppIconPath))
				{
					BitmapSource icon = IconHelper.GetIcon(actionItem2.InheritAppIconPath);
					if (icon != null)
					{
						frameworkElement = new Image
						{
							Source = icon,
							Width = subIconSize + 2.0,
							Height = subIconSize + 2.0,
							Stretch = Stretch.Uniform,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
				if (frameworkElement == null && !string.IsNullOrEmpty(iconKey))
				{
					if (iconKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
					{
						IconHelper.CustomIconItem customIconItem = IconHelper.GetCustomIcons().FirstOrDefault((IconHelper.CustomIconItem c) => c.Key == iconKey);
						if (customIconItem != null)
						{
							frameworkElement = ((!customIconItem.IsSvg) ? ((FrameworkElement)new Image
							{
								Width = subIconSize,
								Height = subIconSize,
								Stretch = Stretch.Uniform,
								Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center,
								Source = IconHelper.GetCustomImageSource(customIconItem.FilePath)
							}) : ((FrameworkElement)new System.Windows.Shapes.Path
							{
								Data = Geometry.Parse(customIconItem.SvgData),
								Fill = subTextColor,
								Stretch = Stretch.Uniform,
								Width = subIconSize,
								Height = subIconSize,
								Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center
							}));
						}
					}
					else
					{
						string svgPathByKey = IconHelper.GetSvgPathByKey(iconKey);
						if (!string.IsNullOrEmpty(svgPathByKey))
						{
							frameworkElement = new System.Windows.Shapes.Path
							{
								Data = Geometry.Parse(svgPathByKey),
								Fill = subTextColor,
								Stretch = Stretch.Uniform,
								Width = subIconSize,
								Height = subIconSize,
								Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center
							};
						}
					}
				}
				if (frameworkElement == null && (text3 == "Launch" || text3 == "App") && !string.IsNullOrEmpty(text4))
				{
					BitmapSource icon = IconHelper.GetIcon(text4);
					if (icon != null)
					{
						frameworkElement = new Image
						{
							Source = icon,
							Width = subIconSize + 2.0,
							Height = subIconSize + 2.0,
							Stretch = Stretch.Uniform,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
				if (frameworkElement == null && text3 == "SwitchWindow")
				{
					frameworkElement = BuildSwitchWindowIcon(text4, subIconSize, subShouldShowText);
				}
				if (frameworkElement == null)
				{
					string vectorIconPath = GetVectorIconPath(text3, text4);
					if (!string.IsNullOrEmpty(vectorIconPath))
					{
						frameworkElement = new System.Windows.Shapes.Path
						{
							Data = Geometry.Parse(vectorIconPath),
							Fill = subTextColor,
							Stretch = Stretch.Uniform,
							Width = subIconSize,
							Height = subIconSize,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
			}

			TextBlock? subTextElement = null;
			if (subShouldShowText && !string.IsNullOrEmpty(text2))
			{
				double subFontSize = (actionItem2 != null && actionItem2.CustomFontSize.HasValue && actionItem2.CustomFontSize.Value > 0.0)
					? actionItem2.CustomFontSize.Value
					: fontSize;
				string subFontFamily = (actionItem2 != null && !string.IsNullOrWhiteSpace(actionItem2.CustomFontFamily))
					? actionItem2.CustomFontFamily
					: (ConfigManager.CurrentConfig.WheelFontFamily ?? "Microsoft YaHei UI, Segoe UI");
				double subFinalFontSize = (subLayout == "TextOnly") ? (subFontSize + 1.0) : subFontSize;
				int subCharLen = text2.Length;
				bool subAsciiOnly = text2.All(c => c < 128);
				if (subCharLen > 8 || (subAsciiOnly && subCharLen > 7))
				{
					subFinalFontSize = Math.Max(7.5, subFinalFontSize * 0.85);
				}
				subTextElement = new TextBlock
				{
					Text = text2,
					Foreground = subTextColor,
					FontSize = subFinalFontSize,
					FontFamily = new FontFamily(subFontFamily),
					FontWeight = (subLayout == "TextOnly") ? FontWeights.SemiBold : FontWeights.Medium,
					TextAlignment = TextAlignment.Center,
					TextWrapping = TextWrapping.Wrap,
					TextTrimming = TextTrimming.CharacterEllipsis,
					MaxWidth = num16 - 2.0,
					MaxHeight = 32.0,
					LineHeight = Math.Max(8.5, subFinalFontSize * 1.15),
					LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
					Effect = (Effect)base.Resources["TextShadow"]
				};
				TextOptions.SetTextFormattingMode((DependencyObject)(object)subTextElement, (TextFormattingMode)1);
				TextOptions.SetTextRenderingMode((DependencyObject)(object)subTextElement, TextRenderingMode.ClearType);
			}

			string subPlacement = (!string.IsNullOrWhiteSpace(actionItem2?.CustomTextPlacement))
				? actionItem2.CustomTextPlacement
				: (ConfigManager.CurrentConfig.SectorTextPlacement ?? "Below");
			double subOffX = (actionItem2 != null && actionItem2.CustomTextOffsetX.HasValue)
				? actionItem2.CustomTextOffsetX.Value
				: ConfigManager.CurrentConfig.SectorTextOffsetX;
			double subOffY = (actionItem2 != null && actionItem2.CustomTextOffsetY.HasValue)
				? actionItem2.CustomTextOffsetY.Value
				: ConfigManager.CurrentConfig.SectorTextOffsetY;

			if (subTextElement != null && (Math.Abs(subOffX) > 0.001 || Math.Abs(subOffY) > 0.001))
			{
				subTextElement.RenderTransform = new TranslateTransform(subOffX, subOffY);
			}

			if (subPlacement == "Above")
			{
				if (subTextElement != null)
				{
					subTextElement.Margin = new Thickness(0.0, 0.0, 0.0, (frameworkElement != null) ? 1.0 : 0.0);
					stackPanel.Children.Add(subTextElement);
				}
				if (frameworkElement != null)
				{
					frameworkElement.Margin = new Thickness(0.0, 0.0, 0.0, 0.0);
					stackPanel.Children.Add(frameworkElement);
				}
			}
			else
			{
				if (frameworkElement != null)
				{
					frameworkElement.Margin = new Thickness(0.0, 0.0, 0.0, (subTextElement != null) ? 2.0 : 0.0);
					stackPanel.Children.Add(frameworkElement);
				}
				if (subTextElement != null)
				{
					subTextElement.Margin = new Thickness(0.0, 1.0, 0.0, 0.0);
					stackPanel.Children.Add(subTextElement);
				}
			}
			Canvas.SetLeft(grid, num14 - grid.Width / 2.0);
			Canvas.SetTop(grid, num15 - grid.Height / 2.0);
			Panel.SetZIndex(grid, 30);
			WheelCanvas.Children.Add(grid);
			_subContentContainers.Add(grid);
			_subContainerTransforms.Add(translateTransform2);
			BackEase easingFunction = new BackEase
			{
				Amplitude = 0.35,
				EasingMode = EasingMode.EaseOut
			};
			Duration duration = new Duration(TimeSpan.FromMilliseconds(130.0));
			DoubleAnimation animation = new DoubleAnimation(0.75, 1.0, duration)
			{
				EasingFunction = easingFunction
			};
			DoubleAnimation animation2 = new DoubleAnimation(0.0, 1.0, duration)
			{
				EasingFunction = new CircleEase
				{
					EasingMode = EasingMode.EaseOut
				}
			};
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
			path.BeginAnimation(UIElement.OpacityProperty, animation2);
			scaleTransform2.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
			scaleTransform2.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
			grid.BeginAnimation(UIElement.OpacityProperty, animation2);
		}
		_subTierCache[parentIndex] = new SubTierVisuals(
			new List<System.Windows.Shapes.Path>(_subSectorPaths),
			new List<Grid>(_subContentContainers),
			new List<TranslateTransform>(_subSectorTransforms),
			new List<TranslateTransform>(_subContainerTransforms),
			new List<double>(_subSectorAngles));
	}

	public void HighlightSector(int index)
	{
		HighlightSector(index, -1, showSubTier: true);
	}

	public void HighlightSector(int mainIndex, int subIndex)
	{
		HighlightSector(mainIndex, subIndex, showSubTier: true);
	}

	public void HighlightSector(int mainIndex, int subIndex, bool showSubTier)
	{
		_ = _currentHighlightedSector;
		int currentHighlightedSector = _currentHighlightedSector;
		_currentHighlightedSector = mainIndex;
		_ = _currentHighlightedSubSector;
		_ = _currentHighlightedSubSector;
		_currentHighlightedSubSector = subIndex;
		UpdateCoreSelectionDisplay(mainIndex, subIndex);
		AppConfig currentConfig = ConfigManager.CurrentConfig;
		double num = ((currentConfig == null || !(currentConfig.CustomAnimationDurationMs > 0.0)) ? ((double)(ConfigManager.CurrentConfig?.AnimationSpeed switch
		{
			"Elegant" => 130, 
			"Fast" => 35, 
			"Custom" => (int)(ConfigManager.CurrentConfig?.CustomAnimationDurationMs ?? 80.0), 
			_ => 80, 
		})) : ConfigManager.CurrentConfig.CustomAnimationDurationMs);
		int num2 = (int)num;
		CubicEase easingFunction = new CubicEase
		{
			EasingMode = EasingMode.EaseOut
		};
		Duration duration = new Duration(TimeSpan.FromMilliseconds(num2));
		int num3 = Math.Max(30, (int)((double)num2 * 1.12));
		Duration duration2 = new Duration(TimeSpan.FromMilliseconds(num3));
		if (CoreScale != null)
		{
			double toValue = ((mainIndex == -1) ? 1.1 : 1.0);
			CoreScale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(CoreScale.ScaleX, toValue, duration2)
			{
				EasingFunction = easingFunction
			});
			CoreScale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(CoreScale.ScaleY, toValue, duration2)
			{
				EasingFunction = easingFunction
			});
		}
		if (mainIndex == -1)
		{
			bool hasCenterAction = _profile != null && _profile.EnableCenterAction && _profile.CenterAction != null && (!string.IsNullOrEmpty(_profile.CenterAction.Type) || !string.IsNullOrEmpty(_profile.CenterAction.IconKey) || !string.IsNullOrEmpty(_profile.CenterAction.Name));
			if (hasCenterAction)
			{
				CoreExitIcon.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
				if (_styleRenderer != null)
				{
					_styleRenderer.ApplyExitHighlight(CoreExitIcon, isHighlighted: true);
				}
			}
			else
			{
				CoreExitIcon.Fill = new SolidColorBrush(Color.FromRgb(244, 63, 94));
				if (_styleRenderer != null)
				{
					_styleRenderer.ApplyExitHighlight(CoreExitIcon, isHighlighted: true);
				}
			}
		}
		else
		{
			CoreExitIcon.Fill = _textColorBrush;
			if (_styleRenderer != null)
			{
				_styleRenderer.ApplyExitHighlight(CoreExitIcon, isHighlighted: false);
			}
		}
		if (currentHighlightedSector >= 0 && currentHighlightedSector < _sectorPaths.Count && currentHighlightedSector != mainIndex)
		{
			System.Windows.Shapes.Path path = _sectorPaths[currentHighlightedSector];
			StackPanel obj = ((currentHighlightedSector < _contentPanels.Count) ? _contentPanels[currentHighlightedSector] : null);
			TranslateTransform translateTransform = ((currentHighlightedSector < _sectorTransforms.Count) ? _sectorTransforms[currentHighlightedSector] : null);
			TranslateTransform translateTransform2 = ((currentHighlightedSector < _containerTransforms.Count) ? _containerTransforms[currentHighlightedSector] : null);
			path.Fill = _defaultSectorBrush;
			path.Stroke = _sectorBorderBrush;
			path.StrokeThickness = _borderThickness;
			Panel.SetZIndex(path, 0);
			if (translateTransform != null)
			{
				translateTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform.X, 0.0, duration)
				{
					EasingFunction = easingFunction
				});
				translateTransform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform.Y, 0.0, duration)
				{
					EasingFunction = easingFunction
				});
			}
			if (translateTransform2 != null)
			{
				translateTransform2.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform2.X, 0.0, duration)
				{
					EasingFunction = easingFunction
				});
				translateTransform2.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform2.Y, 0.0, duration)
				{
					EasingFunction = easingFunction
				});
			}
			TextBlock textBlock = obj?.Children.OfType<TextBlock>().FirstOrDefault();
			System.Windows.Shapes.Path path2 = obj?.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
			if (textBlock != null)
			{
				textBlock.Foreground = _textColorBrush;
				textBlock.FontWeight = FontWeights.Medium;
			}
			if (path2 != null)
			{
				path2.Fill = _textColorBrush;
			}
			if (_styleRenderer != null)
			{
				_styleRenderer.ApplySectorHighlight(path, isHighlighted: false);
			}
		}
		if (mainIndex >= 0 && mainIndex < _sectorPaths.Count)
		{
			System.Windows.Shapes.Path path3 = _sectorPaths[mainIndex];
			StackPanel obj2 = ((mainIndex < _contentPanels.Count) ? _contentPanels[mainIndex] : null);
			TranslateTransform translateTransform3 = ((mainIndex < _sectorTransforms.Count) ? _sectorTransforms[mainIndex] : null);
			TranslateTransform translateTransform4 = ((mainIndex < _containerTransforms.Count) ? _containerTransforms[mainIndex] : null);
			double num4 = ((mainIndex < _sectorAngles.Count) ? _sectorAngles[mainIndex] : 0.0);
			path3.Fill = _highlightSectorBrush;
			path3.Stroke = _highlightBorderBrush;
			path3.StrokeThickness = _highlightBorderThickness;
			Panel.SetZIndex(path3, 5);
			double toValue2 = Math.Cos(num4) * 5.5;
			double toValue3 = Math.Sin(num4) * 5.5;
			if (translateTransform3 != null)
			{
				translateTransform3.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform3.X, toValue2, duration)
				{
					EasingFunction = easingFunction
				});
				translateTransform3.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform3.Y, toValue3, duration)
				{
					EasingFunction = easingFunction
				});
			}
			if (translateTransform4 != null)
			{
				translateTransform4.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform4.X, toValue2, duration)
				{
					EasingFunction = easingFunction
				});
				translateTransform4.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform4.Y, toValue3, duration)
				{
					EasingFunction = easingFunction
				});
			}
			TextBlock textBlock2 = obj2?.Children.OfType<TextBlock>().FirstOrDefault();
			System.Windows.Shapes.Path path4 = obj2?.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
			if (textBlock2 != null)
			{
				textBlock2.Foreground = Brushes.White;
				textBlock2.FontWeight = FontWeights.Bold;
			}
			if (path4 != null)
			{
				path4.Fill = Brushes.White;
			}
			if (_styleRenderer != null)
			{
				_styleRenderer.ApplySectorHighlight(path3, isHighlighted: true);
			}
		}
		if (showSubTier && mainIndex >= 0 && mainIndex < _profile.Actions.Count)
		{
			if (_activeSubTierParentSector != mainIndex)
			{
				ShowSubTier(mainIndex);
			}
		}
		else if (_activeSubTierParentSector != -1)
		{
			ClearSubTier();
		}
		if (_subSectorPaths.Count <= 0)
		{
			return;
		}
		for (int i = 0; i < _subSectorPaths.Count; i++)
		{
			System.Windows.Shapes.Path path5 = _subSectorPaths[i];
			TranslateTransform translateTransform5 = ((i < _subSectorTransforms.Count) ? _subSectorTransforms[i] : null);
			Grid obj3 = ((i < _subContentContainers.Count) ? _subContentContainers[i] : null);
			TranslateTransform translateTransform6 = ((i < _subContainerTransforms.Count) ? _subContainerTransforms[i] : null);
			double num5 = ((i < _subSectorAngles.Count) ? _subSectorAngles[i] : 0.0);
			TextBlock textBlock3 = obj3?.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().FirstOrDefault();
			System.Windows.Shapes.Path path6 = obj3?.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<System.Windows.Shapes.Path>().FirstOrDefault();
			if (i == subIndex)
			{
				path5.Fill = _subHighlightSectorBrush;
				path5.Stroke = _subHighlightBorderBrush;
				path5.StrokeThickness = _highlightBorderThickness;
				Panel.SetZIndex(path5, 18);
				ApplySubSectorGlow(path5, isHighlighted: true);
				if (textBlock3 != null)
				{
					textBlock3.Foreground = Brushes.White;
					textBlock3.FontWeight = FontWeights.Bold;
				}
				if (path6 != null)
				{
					path6.Fill = Brushes.White;
				}
				double toValue4 = Math.Cos(num5) * 4.0;
				double toValue5 = Math.Sin(num5) * 4.0;
				if (translateTransform5 != null)
				{
					translateTransform5.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform5.X, toValue4, duration)
					{
						EasingFunction = easingFunction
					});
					translateTransform5.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform5.Y, toValue5, duration)
					{
						EasingFunction = easingFunction
					});
				}
				if (translateTransform6 != null)
				{
					translateTransform6.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform6.X, toValue4, duration)
					{
						EasingFunction = easingFunction
					});
					translateTransform6.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform6.Y, toValue5, duration)
					{
						EasingFunction = easingFunction
					});
				}
			}
			else
			{
				path5.Fill = _subDefaultSectorBrush;
				path5.Stroke = _subSectorBorderBrush;
				path5.StrokeThickness = _borderThickness;
				Panel.SetZIndex(path5, 15);
				ApplySubSectorGlow(path5, isHighlighted: false);
				if (textBlock3 != null)
				{
					textBlock3.Foreground = _subTextColorBrush;
					textBlock3.FontWeight = FontWeights.Medium;
				}
				if (path6 != null)
				{
					path6.Fill = _subTextColorBrush;
				}
				if (translateTransform5 != null && (translateTransform5.X != 0.0 || translateTransform5.Y != 0.0))
				{
					translateTransform5.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform5.X, 0.0, duration)
					{
						EasingFunction = easingFunction
					});
					translateTransform5.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform5.Y, 0.0, duration)
					{
						EasingFunction = easingFunction
					});
				}
				if (translateTransform6 != null && (translateTransform6.X != 0.0 || translateTransform6.Y != 0.0))
				{
					translateTransform6.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(translateTransform6.X, 0.0, duration)
					{
						EasingFunction = easingFunction
					});
					translateTransform6.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(translateTransform6.Y, 0.0, duration)
					{
						EasingFunction = easingFunction
					});
				}
			}
		}
	}

	private void ApplySubSectorGlow(System.Windows.Shapes.Path path, bool isHighlighted)
	{
		if (!isHighlighted)
		{
			path.Effect = null;
			return;
		}
		string text = ConfigManager.CurrentConfig?.SubWheelHighlightGlowPreset ?? "FollowPrimary";
		if (text == "FollowPrimary")
		{
			text = ConfigManager.CurrentConfig?.HighlightGlowPreset ?? "Auto";
		}
		if (text == "None")
		{
			path.Effect = null;
			return;
		}
		Color color;
		if (!(text == "Custom") || string.IsNullOrEmpty(ConfigManager.CurrentConfig?.SubWheelHighlightGlowColor))
		{
			color = text switch
			{
				"Lilac" => Color.FromRgb(168, 85, 247), 
				"Blue" => Color.FromRgb(59, 130, 246), 
				"Emerald" => Color.FromRgb(16, 185, 129), 
				"Rose" => Color.FromRgb(236, 72, 153), 
				"Amber" => Color.FromRgb(245, 158, 11), 
				"Red" => Color.FromRgb(239, 68, 68), 
				"White" => Color.FromRgb(byte.MaxValue, byte.MaxValue, byte.MaxValue), 
				_ => (_subHighlightBorderBrush is SolidColorBrush { Color: { A: >0 } } solidColorBrush) ? solidColorBrush.Color : ((_subHighlightSectorBrush is SolidColorBrush { Color: { A: >0 } } solidColorBrush2) ? solidColorBrush2.Color : ((!(_highlightBorderBrush is SolidColorBrush { Color: { A: >0 } } solidColorBrush3)) ? Color.FromRgb(59, 130, 246) : solidColorBrush3.Color)), 
			};
		}
		else
		{
			try
			{
				color = (Color)ColorConverter.ConvertFromString(ConfigManager.CurrentConfig.SubWheelHighlightGlowColor);
			}
			catch
			{
				color = Color.FromRgb(168, 85, 247);
			}
		}
		double num;
		if (!(ConfigManager.CurrentConfig?.SubWheelHighlightGlowPreset == "FollowPrimary"))
		{
			AppConfig currentConfig = ConfigManager.CurrentConfig;
			num = ((currentConfig != null && currentConfig.SubWheelHighlightGlowRadius > 0.0) ? ConfigManager.CurrentConfig.SubWheelHighlightGlowRadius : 24.0);
		}
		else
		{
			AppConfig currentConfig2 = ConfigManager.CurrentConfig;
			num = ((currentConfig2 != null && currentConfig2.HighlightGlowRadius > 0.0) ? ConfigManager.CurrentConfig.HighlightGlowRadius : 24.0);
		}
		double blurRadius = num;
		double num2;
		if (!(ConfigManager.CurrentConfig?.SubWheelHighlightGlowPreset == "FollowPrimary"))
		{
			AppConfig currentConfig3 = ConfigManager.CurrentConfig;
			num2 = ((currentConfig3 != null && currentConfig3.SubWheelHighlightGlowOpacity >= 0.0) ? ConfigManager.CurrentConfig.SubWheelHighlightGlowOpacity : 0.85);
		}
		else
		{
			AppConfig currentConfig4 = ConfigManager.CurrentConfig;
			num2 = ((currentConfig4 != null && currentConfig4.HighlightGlowOpacity >= 0.0) ? ConfigManager.CurrentConfig.HighlightGlowOpacity : 0.85);
		}
		double opacity = num2;
		path.Effect = new DropShadowEffect
		{
			Color = color,
			BlurRadius = blurRadius,
			ShadowDepth = 0.0,
			Opacity = opacity
		};
	}

	private void UpdateCoreSelectionDisplay(int mainIndex, int subIndex)
	{
		bool shouldShow = ConfigManager.CurrentConfig?.ShowSelectedActionText == true && mainIndex >= 0;
		string selectedName = string.Empty;
		if (shouldShow && mainIndex < _profile.SectorCount)
		{
			ActionItem? action = _profile.GetEffectiveAction(mainIndex);
			if (subIndex >= 0 && action?.SubActions != null && subIndex < action.SubActions.Count)
			{
				selectedName = SelectionDisplayName(action.SubActions[subIndex]);
			}
			if (string.IsNullOrWhiteSpace(selectedName) && action != null)
			{
				selectedName = SelectionDisplayName(action);
			}
		}

		if (shouldShow && !string.IsNullOrWhiteSpace(selectedName))
		{
			CoreSelectionText.Text = selectedName;
			CoreSelectionTextPanel.Visibility = Visibility.Visible;
			CoreSelectionOverlay.Visibility = Visibility.Visible;
			CoreExitIcon.Opacity = (CoreExitIcon.Visibility == Visibility.Visible) ? 0.18 : _defaultCoreExitIconOpacity;
			CoreCustomImageEllipse.Opacity = _defaultCoreCustomImageOpacity;
			CoreCustomImageEllipse.Effect = (CoreCustomImageEllipse.Visibility == Visibility.Visible)
				? new BlurEffect { Radius = 5.5, RenderingBias = RenderingBias.Performance }
				: _defaultCoreCustomImageEffect;
			Panel.SetZIndex(CoreSelectionOverlay, 20);
			Panel.SetZIndex(CoreSelectionTextPanel, 21);
			return;
		}

		CoreSelectionTextPanel.Visibility = Visibility.Collapsed;
		CoreSelectionOverlay.Visibility = Visibility.Collapsed;
		CoreExitIcon.Visibility = _defaultCoreExitIconVisibility;
		CoreCustomImageEllipse.Visibility = _defaultCoreCustomImageVisibility;
		CoreExitIcon.Opacity = _defaultCoreExitIconOpacity;
		CoreCustomImageEllipse.Opacity = _defaultCoreCustomImageOpacity;
		CoreCustomImageEllipse.Effect = _defaultCoreCustomImageEffect;
	}

	/// <summary>轮盘中心选中文字：切换窗口动作显示"将要激活窗口"的真实标题；其它动作显示动作名。</summary>
	private static string SelectionDisplayName(ActionItem? action)
	{
		if (action == null)
		{
			return string.Empty;
		}
		if (string.Equals(action.Type, "SwitchWindow", StringComparison.OrdinalIgnoreCase) &&
			int.TryParse(action.Parameter?.Trim(), out int n) && n > 0)
		{
			string? title = WindowTaskbarHelper.GetNthTaskbarWindowTitle(n);
			if (!string.IsNullOrWhiteSpace(title))
			{
				return title;
			}
		}
		return action.Name ?? string.Empty;
	}

	private static Brush CreateFrostedCoreBrush(Brush baseBrush)
	{
		Color baseColor = (baseBrush as SolidColorBrush)?.Color ?? Color.FromRgb(36, 44, 60);
		double luminance = (0.2126 * baseColor.R) + (0.7152 * baseColor.G) + (0.0722 * baseColor.B);
		if (luminance >= 165.0)
		{
			return new SolidColorBrush(Color.FromArgb(88, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		}

		return new SolidColorBrush(Color.FromArgb(
			112,
			(byte)Math.Min(255, baseColor.R + 48),
			(byte)Math.Min(255, baseColor.G + 56),
			(byte)Math.Min(255, baseColor.B + 72)));
	}

	private static Stretch ParseStretch(string? str)
	{
		if (string.Equals(str, "Uniform", StringComparison.OrdinalIgnoreCase))
		{
			return Stretch.Uniform;
		}
		if (string.Equals(str, "Fill", StringComparison.OrdinalIgnoreCase))
		{
			return Stretch.Fill;
		}
		if (string.Equals(str, "None", StringComparison.OrdinalIgnoreCase))
		{
			return Stretch.None;
		}
		return Stretch.UniformToFill;
	}

	public const int FanSubmenuSlotCount = 3;

	public static (double du, double dv) GetFanSubOffset(int index)
	{
		double ringR = Math.Sqrt(2.0 - Math.Sqrt(2.0)); // 0.7653668647301795
		return index switch
		{
			0 => (1.0 + ringR * 0.5, -ringR * 0.8660254038),  // 左翼 (逆时针/较小角度，对于上方扇区为左侧)
			1 => (1.0 + ringR, 0.0),                          // 中尖 (正中心)
			_ => (1.0 + ringR * 0.5, ringR * 0.8660254038)    // 右翼 (顺时针/较大角度，对于上方扇区为右侧)
		};
	}

	/// <summary>
	/// 按形态取蜂窝扇槽位坐标：
	/// 经典紧凑扇区（Original/ClassicRing）三个二级项落在同一圆环（共轨，PR#34 布局）；
	/// 其余形态（Circle/Capsule/HexagonHive）保持原始等边蜂窝坐标。
	/// 渲染与命中必须使用同一函数，保证"所见即所点"。
	/// </summary>
	public static (double du, double dv) GetFanSubOffsetForShape(string? shape, int index)
	{
		if (string.Equals(shape, "Original", StringComparison.OrdinalIgnoreCase))
		{
			double ringR = Math.Sqrt(2.0 - Math.Sqrt(2.0)); // 0.7653668647301795
			double orbitRadius = 1.0 + ringR;
			double wingAngle = Math.Atan2(ringR * 0.8660254038, 1.0 + ringR * 0.5);
			double wingDu = orbitRadius * Math.Cos(wingAngle);
			double wingDv = orbitRadius * Math.Sin(wingAngle);
			return index switch
			{
				0 => (wingDu, -wingDv),     // 左翼 (共轨，对于上方扇区为左侧)
				1 => (orbitRadius, 0.0),    // 中尖
				_ => (wingDu, wingDv)       // 右翼 (共轨，对于上方扇区为右侧)
			};
		}
		return GetFanSubOffset(index);
	}

	public static int GetFanSlotIndex(int subIndex, int totalCount)
	{
		if (totalCount <= 1) return 1; // 仅1项：居中尖端
		if (totalCount == 2) return (subIndex == 0) ? 0 : 2; // 2项：0=左翼(-dv), 2=右翼(+dv)
		return Math.Clamp(subIndex, 0, 2); // 3项：0=左翼(-dv), 1=中尖(0), 2=右翼(+dv)
	}

	public static double GetFanExtentRadius(double outer, double inner)
	{
		double R = (outer + inner) / 2.0;
		return GetFanSubOffset(1).du * R + (outer - inner) * 0.40;
	}

	public static Geometry CreateSubMenuGeometry(string shape, double cx, double cy, double radius, double parentAngleRad, double wheelCx, double wheelCy, double cornerRadius = -1)
	{
		string s = string.IsNullOrEmpty(shape) ? "Original" : shape;
		double itemAngleRad = Math.Atan2(cy - wheelCy, cx - wheelCx);
		double itemAngleDeg = itemAngleRad * (180.0 / Math.PI);

		// 1. HexagonHive: 6-vertex regular hexagon with smooth rounded corners
		if (s.Equals("HexagonHive", StringComparison.OrdinalIgnoreCase))
		{
			double maxFillet = (Math.Sqrt(3.0) / 2.0) * radius * 0.95;
			double rawCr = (cornerRadius >= 0.0)
				? cornerRadius
				: ((ConfigManager.CurrentConfig?.SubWheelCornerRadius >= 0.0) ? ConfigManager.CurrentConfig.SubWheelCornerRadius : 4.0);
			double effectiveCr = Math.Max(0.0, Math.Min(rawCr, maxFillet));

			Point[] vertices = new Point[6];
			for (int i = 0; i < 6; i++)
			{
				double ang = itemAngleRad + (double)i * (Math.PI / 3.0);
				vertices[i] = new Point(cx + Math.Cos(ang) * radius, cy + Math.Sin(ang) * radius);
			}

			StreamGeometry hex = new StreamGeometry();
			using (StreamGeometryContext ctx = hex.Open())
			{
				if (effectiveCr < 0.5)
				{
					ctx.BeginFigure(vertices[0], isFilled: true, isClosed: true);
					for (int i = 1; i < 6; i++)
					{
						ctx.LineTo(vertices[i], isStroked: true, isSmoothJoin: false);
					}
				}
				else
				{
					double tangentDist = effectiveCr / Math.Sqrt(3.0);
					Point[] pEntry = new Point[6];
					Point[] pExit = new Point[6];

					for (int k = 0; k < 6; k++)
					{
						Point prev = vertices[(k + 5) % 6];
						Point curr = vertices[k];
						Point next = vertices[(k + 1) % 6];

						Vector vIn = curr - prev;
						vIn.Normalize();
						pEntry[k] = curr - vIn * tangentDist;

						Vector vOut = next - curr;
						vOut.Normalize();
						pExit[k] = curr + vOut * tangentDist;
					}

					ctx.BeginFigure(pEntry[0], isFilled: true, isClosed: true);
					Size arcSize = new Size(effectiveCr, effectiveCr);

					for (int i = 0; i < 6; i++)
					{
						ctx.ArcTo(pExit[i], arcSize, 0.0, isLargeArc: false, SweepDirection.Clockwise, isStroked: true, isSmoothJoin: true);
						int nextIdx = (i + 1) % 6;
						ctx.LineTo(pEntry[nextIdx], isStroked: true, isSmoothJoin: false);
					}
				}
			}
			hex.Freeze();
			return hex;
		}

		// 2. Circle: Clean round circular card bubble
		if (s.Equals("Circle", StringComparison.OrdinalIgnoreCase))
		{
			EllipseGeometry ellipse = new EllipseGeometry(new Point(cx, cy), radius, radius);
			ellipse.Freeze();
			return ellipse;
		}

		// 3. RoundedCapsule / FloatingCapsules / Capsule: Stadium / Pill shape
		if (s.Equals("RoundedCapsule", StringComparison.OrdinalIgnoreCase) ||
		    s.Equals("FloatingCapsules", StringComparison.OrdinalIgnoreCase) ||
		    s.Equals("Capsule", StringComparison.OrdinalIgnoreCase))
		{
			double w = 1.9 * radius, h = 1.35 * radius;
			double r = (cornerRadius >= 0.0) ? Math.Min(h / 2.0, cornerRadius) : (h / 2.0);
			RectangleGeometry rect = new RectangleGeometry(new Rect(-w / 2.0, -h / 2.0, w, h), r, r);
			TransformGroup tf = new TransformGroup();
			tf.Children.Add(new RotateTransform(itemAngleDeg));
			tf.Children.Add(new TranslateTransform(cx, cy));
			rect.Transform = tf;
			rect.Freeze();
			return rect;
		}

		// 4. CleanSectors / RoundedRect: Modern smooth rounded rectangle
		if (s.Equals("CleanSectors", StringComparison.OrdinalIgnoreCase) ||
		    s.Equals("RoundedRect", StringComparison.OrdinalIgnoreCase))
		{
			double w = 1.85 * radius, h = 1.4 * radius;
			double r = (cornerRadius >= 0.0) ? cornerRadius : (radius * 0.35);
			RectangleGeometry rect = new RectangleGeometry(new Rect(-w / 2.0, -h / 2.0, w, h), r, r);
			TransformGroup tf = new TransformGroup();
			tf.Children.Add(new RotateTransform(itemAngleDeg));
			tf.Children.Add(new TranslateTransform(cx, cy));
			rect.Transform = tf;
			rect.Freeze();
			return rect;
		}

// 5. Original / ClassicRing: Smooth radiating curved arc petal (compact sector, keep PR#34 tuning)
		{
			double halfSpan = 10.0;
			double startDeg = itemAngleDeg - halfSpan;
			double endDeg = itemAngleDeg + halfSpan;
			double distFromCenter = Math.Sqrt((cx - wheelCx) * (cx - wheelCx) + (cy - wheelCy) * (cy - wheelCy));
			double rIn = Math.Max(10.0, distFromCenter - radius * 0.88);
			double rOut = distFromCenter + radius * 0.88;
			double cr = (cornerRadius >= 0.0) ? cornerRadius : 6.0;

			return IconHelper.CreateAdvancedSectorGeometry(wheelCx, wheelCy, startDeg, endDeg, rIn, rOut, "Original", 0.0, cr);
		}
	}

	private void RenderFanSubtier(int parentIndex)
	{
		ClearSubTier();
		if (!ConfigManager.CurrentConfig.EnableMultiTier || parentIndex < 0 || parentIndex >= _profile.SectorCount)
		{
			return;
		}
		ActionItem? actionItem = _profile.GetEffectiveAction(parentIndex);
		if (actionItem == null || actionItem.SubActions == null || actionItem.SubActions.Count == 0)
		{
			return;
		}
		if (_subTierCache.TryGetValue(parentIndex, out SubTierVisuals? cachedVisuals))
		{
			ActivateCachedSubTier(parentIndex, cachedVisuals);
			return;
		}
		int sectorCount = _profile.SectorCount;
		double sectorSize = 360.0 / (double)sectorCount;
		double cx = _canvasCenter;
		double cy = _canvasCenter;
		string shape = ConfigManager.CurrentConfig.Shape ?? "Original";
		string layoutMode = ConfigManager.CurrentConfig.IconLayoutMode ?? "IconAndText";
		bool showText = ConfigManager.CurrentConfig.ShowText && layoutMode != "IconOnly";

		double midRad = (double)parentIndex * sectorSize * (Math.PI / 180.0);
		double ux = Math.Cos(midRad), uy = Math.Sin(midRad);
		double vx = -Math.Sin(midRad), vy = Math.Cos(midRad);

		double userSubRadius = ConfigManager.CurrentConfig.SubWheelOuterRadius;
		double userSubGap = ConfigManager.CurrentConfig.SubWheelInnerGap;
		double userCornerRadius = ConfigManager.CurrentConfig.SubWheelCornerRadius;

		double ratio = (userSubRadius > 0.0 && _outerRadius > 0.0) ? (userSubRadius / (_outerRadius * 1.55)) : 1.0;
		double itemR = (_outerRadius - _innerRadius) * 0.40 * Math.Max(0.5, Math.Min(2.5, ratio));
		double gapOffset = (userSubGap >= 0.0) ? userSubGap : 4.0;
		double R = ((_innerRadius + _outerRadius) / 2.0 * ratio) + gapOffset;

		double num7 = ((ConfigManager.CurrentConfig.SubWheelIconSize > 0.0) ? ConfigManager.CurrentConfig.SubWheelIconSize : ((layoutMode == "IconOnly") ? 22.0 : 17.0));
		double fontSize = ((ConfigManager.CurrentConfig.SubWheelFontSize > 0.0) ? ConfigManager.CurrentConfig.SubWheelFontSize : Math.Max(8.5, ConfigManager.CurrentConfig.SectorFontSize - 1.0));

		List<ActionItem> subActions = actionItem.SubActions;
		int subCount = subActions.Count;
		int activeCount = Math.Min(FanSubmenuSlotCount, subCount);
		bool isCompactSector = string.Equals(shape, "Original", StringComparison.OrdinalIgnoreCase);
		_activeSubTierParentSector = parentIndex;

		for (int j = 0; j < activeCount; j++)
		{
			int slot = GetFanSlotIndex(j, activeCount);
			var (du, dv) = GetFanSubOffsetForShape(shape, slot);

			double px = cx + ux * (du * R) + vx * (dv * R);
			double py = cy + uy * (du * R) + vy * (dv * R);

			Geometry data = CreateSubMenuGeometry(shape, px, py, itemR, midRad, cx, cy, userCornerRadius);

// 经典紧凑扇区以轮盘中心为缩放枢轴（PR#34 适配），其余形态按各项自身位置
			ScaleTransform scaleTransform = new ScaleTransform(0.75, 0.75, isCompactSector ? cx : px, isCompactSector ? cy : py);
			TranslateTransform translateTransform = new TranslateTransform(0.0, 0.0);
			TransformGroup transformGroup = new TransformGroup();
			transformGroup.Children.Add(scaleTransform);
			transformGroup.Children.Add(translateTransform);

			System.Windows.Shapes.Path path = new System.Windows.Shapes.Path
			{
				Data = data,
				Fill = _subDefaultSectorBrush,
				Stroke = _subSectorBorderBrush,
				StrokeThickness = _borderThickness,
				Tag = $"sub_{parentIndex}_{j}",
				Opacity = 0.0,
				RenderTransform = transformGroup
			};
			Panel.SetZIndex(path, 15);
			WheelCanvas.Children.Add(path);
			_subSectorPaths.Add(path);
			_subSectorTransforms.Add(translateTransform);
			_subSectorAngles.Add(midRad);

			double containerW = itemR * 2.2;
			double containerH = itemR * 2.0;
			ScaleTransform scaleTransform2 = new ScaleTransform(0.75, 0.75, containerW / 2.0, containerH / 2.0);
			TranslateTransform translateTransform2 = new TranslateTransform(0.0, 0.0);
			TransformGroup transformGroup2 = new TransformGroup();
			transformGroup2.Children.Add(scaleTransform2);
			transformGroup2.Children.Add(translateTransform2);

			Grid grid = new Grid
			{
				Width = containerW,
				Height = containerH,
				Opacity = 0.0,
				RenderTransform = transformGroup2
			};
			StackPanel stackPanel = new StackPanel
			{
				Orientation = Orientation.Vertical,
				HorizontalAlignment = HorizontalAlignment.Center,
				VerticalAlignment = VerticalAlignment.Center
			};
			grid.Children.Add(stackPanel);

			ActionItem actionItem2 = subActions[j];
			string text2 = actionItem2?.Name ?? "子动作";
			string text3 = actionItem2?.Type ?? "Hotkey";
			string text4 = actionItem2?.Parameter ?? "";
			string iconKey = actionItem2?.IconKey ?? "";
			string text5 = actionItem2?.CustomIconSvg ?? "";

			string subLayout = (actionItem2 != null && !string.IsNullOrWhiteSpace(actionItem2.LayoutMode) && actionItem2.LayoutMode != "Inherit")
				? actionItem2.LayoutMode
				: layoutMode;
			bool subShouldShowIcon = (subLayout != "TextOnly");
			bool subShouldShowText = (subLayout != "IconOnly");
			Brush subTextColor = _subTextColorBrush;
			if (actionItem2 != null && !string.IsNullOrWhiteSpace(actionItem2.CustomTextColor))
			{
				try
				{
					subTextColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString(actionItem2.CustomTextColor));
				}
				catch
				{
				}
			}
			double subIconSize = ((actionItem2 != null && actionItem2.CustomIconSize.HasValue && actionItem2.CustomIconSize.Value > 0.0)
				? actionItem2.CustomIconSize.Value
				: num7) * ((subLayout == "IconOnly") ? 1.3 : 1.0);

			FrameworkElement frameworkElement = null;

			if (subShouldShowIcon)
			{
				if (!string.IsNullOrEmpty(text5))
				{
					try
					{
						frameworkElement = new System.Windows.Shapes.Path
						{
							Data = Geometry.Parse(text5),
							Fill = subTextColor,
							Stretch = Stretch.Uniform,
							Width = subIconSize,
							Height = subIconSize,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
					catch { }
				}
				if (frameworkElement == null && !string.IsNullOrEmpty(actionItem2?.InheritAppIconPath))
				{
					BitmapSource icon = IconHelper.GetIcon(actionItem2.InheritAppIconPath);
					if (icon != null)
					{
						frameworkElement = new Image
						{
							Source = icon,
							Width = subIconSize + 2.0,
							Height = subIconSize + 2.0,
							Stretch = Stretch.Uniform,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
				if (frameworkElement == null && !string.IsNullOrEmpty(iconKey))
				{
					if (iconKey.StartsWith("custom:", StringComparison.OrdinalIgnoreCase))
					{
						IconHelper.CustomIconItem customIconItem = IconHelper.GetCustomIcons().FirstOrDefault((IconHelper.CustomIconItem c) => c.Key == iconKey);
						if (customIconItem != null)
						{
							frameworkElement = ((!customIconItem.IsSvg) ? ((FrameworkElement)new Image
							{
								Width = subIconSize,
								Height = subIconSize,
								Stretch = Stretch.Uniform,
								Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center,
								Source = IconHelper.GetCustomImageSource(customIconItem.FilePath)
							}) : ((FrameworkElement)new System.Windows.Shapes.Path
							{
								Data = Geometry.Parse(customIconItem.SvgData),
								Fill = subTextColor,
								Stretch = Stretch.Uniform,
								Width = subIconSize,
								Height = subIconSize,
								Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center
							}));
						}
					}
					else
					{
						string svgPathByKey = IconHelper.GetSvgPathByKey(iconKey);
						if (!string.IsNullOrEmpty(svgPathByKey))
						{
							frameworkElement = new System.Windows.Shapes.Path
							{
								Data = Geometry.Parse(svgPathByKey),
								Fill = subTextColor,
								Stretch = Stretch.Uniform,
								Width = subIconSize,
								Height = subIconSize,
								Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
								HorizontalAlignment = HorizontalAlignment.Center
							};
						}
					}
				}
				if (frameworkElement == null && (text3 == "Launch" || text3 == "App") && !string.IsNullOrEmpty(text4))
				{
					BitmapSource icon = IconHelper.GetIcon(text4);
					if (icon != null)
					{
						frameworkElement = new Image
						{
							Source = icon,
							Width = subIconSize + 2.0,
							Height = subIconSize + 2.0,
							Stretch = Stretch.Uniform,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
				if (frameworkElement == null && text3 == "SwitchWindow")
				{
					frameworkElement = BuildSwitchWindowIcon(text4, subIconSize, subShouldShowText);
				}
				if (frameworkElement == null)
				{
					string vectorIconPath = GetVectorIconPath(text3, text4);
					if (!string.IsNullOrEmpty(vectorIconPath))
					{
						frameworkElement = new System.Windows.Shapes.Path
						{
							Data = Geometry.Parse(vectorIconPath),
							Fill = subTextColor,
							Stretch = Stretch.Uniform,
							Width = subIconSize,
							Height = subIconSize,
							Margin = new Thickness(0.0, 0.0, 0.0, subShouldShowText ? 2 : 0),
							HorizontalAlignment = HorizontalAlignment.Center
						};
					}
				}
			}

			TextBlock? textBlock = null;
			if (subShouldShowText && !string.IsNullOrEmpty(text2))
			{
				double subFontSize = (actionItem2 != null && actionItem2.CustomFontSize.HasValue && actionItem2.CustomFontSize.Value > 0.0)
					? actionItem2.CustomFontSize.Value
					: fontSize;
				string subFontFamily = (actionItem2 != null && !string.IsNullOrWhiteSpace(actionItem2.CustomFontFamily))
					? actionItem2.CustomFontFamily
					: (ConfigManager.CurrentConfig.WheelFontFamily ?? "Microsoft YaHei UI, Segoe UI");
				double honeyFinalFontSize = (subLayout == "TextOnly") ? (subFontSize + 1.0) : subFontSize;
				int honeyCharLen = text2.Length;
				bool honeyAsciiOnly = text2.All(c => c < 128);
				if (honeyCharLen > 8 || (honeyAsciiOnly && honeyCharLen > 7))
				{
					honeyFinalFontSize = Math.Max(7.5, honeyFinalFontSize * 0.85);
				}
				textBlock = new TextBlock
				{
					Text = text2,
					Foreground = subTextColor,
					FontSize = honeyFinalFontSize,
					FontFamily = new FontFamily(subFontFamily),
					FontWeight = (subLayout == "TextOnly") ? FontWeights.SemiBold : FontWeights.Medium,
					TextAlignment = TextAlignment.Center,
					TextWrapping = TextWrapping.Wrap,
					TextTrimming = TextTrimming.CharacterEllipsis,
					MaxWidth = containerW - 2.0,
					MaxHeight = 28.0,
					LineHeight = Math.Max(8.5, honeyFinalFontSize * 1.15),
					LineStackingStrategy = LineStackingStrategy.BlockLineHeight
				};
				if (base.Resources.Contains("TextShadow"))
				{
					textBlock.Effect = (Effect)base.Resources["TextShadow"];
				}
			}

			string honeyPlacement = (!string.IsNullOrWhiteSpace(actionItem2?.CustomTextPlacement))
				? actionItem2.CustomTextPlacement
				: (ConfigManager.CurrentConfig.SectorTextPlacement ?? "Below");
			double honeyOffX = (actionItem2 != null && actionItem2.CustomTextOffsetX.HasValue)
				? actionItem2.CustomTextOffsetX.Value
				: ConfigManager.CurrentConfig.SectorTextOffsetX;
			double honeyOffY = (actionItem2 != null && actionItem2.CustomTextOffsetY.HasValue)
				? actionItem2.CustomTextOffsetY.Value
				: ConfigManager.CurrentConfig.SectorTextOffsetY;

			if (textBlock != null && (Math.Abs(honeyOffX) > 0.001 || Math.Abs(honeyOffY) > 0.001))
			{
				textBlock.RenderTransform = new TranslateTransform(honeyOffX, honeyOffY);
			}

			if (honeyPlacement == "Above")
			{
				if (textBlock != null)
				{
					textBlock.Margin = new Thickness(0.0, 0.0, 0.0, (frameworkElement != null) ? 1.0 : 0.0);
					stackPanel.Children.Add(textBlock);
				}
				if (frameworkElement != null)
				{
					frameworkElement.Margin = new Thickness(0.0, 0.0, 0.0, 0.0);
					stackPanel.Children.Add(frameworkElement);
				}
			}
			else
			{
				if (frameworkElement != null)
				{
					frameworkElement.Margin = new Thickness(0.0, 0.0, 0.0, (textBlock != null) ? 2.0 : 0.0);
					stackPanel.Children.Add(frameworkElement);
				}
				if (textBlock != null)
				{
					textBlock.Margin = new Thickness(0.0, 1.0, 0.0, 0.0);
					stackPanel.Children.Add(textBlock);
				}
			}

			Canvas.SetLeft(grid, px - grid.Width / 2.0);
			Canvas.SetTop(grid, py - grid.Height / 2.0);
			Panel.SetZIndex(grid, 35);
			WheelCanvas.Children.Add(grid);
			_subContentContainers.Add(grid);
			_subContainerTransforms.Add(translateTransform2);

			int durationMs = (ConfigManager.CurrentConfig?.AnimationSpeed == "Custom" && ConfigManager.CurrentConfig.CustomAnimationDurationMs > 0) 
				? (int)ConfigManager.CurrentConfig.CustomAnimationDurationMs 
				: (ConfigManager.CurrentConfig?.AnimationSpeed switch
				{
					"Elegant" => 130,
					"Fast" => 35,
					_ => 80
				});
			Duration duration = new Duration(TimeSpan.FromMilliseconds(durationMs * 1.3));
			DoubleAnimation doubleAnimation = new DoubleAnimation(0.0, 1.0, duration)
			{
				EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
			};
			DoubleAnimation doubleAnimation2 = new DoubleAnimation(0.75, 1.0, duration)
			{
				EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 }
			};
			path.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
			grid.BeginAnimation(UIElement.OpacityProperty, doubleAnimation);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, doubleAnimation2);
			scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimation2);
			scaleTransform2.BeginAnimation(ScaleTransform.ScaleXProperty, doubleAnimation2);
			scaleTransform2.BeginAnimation(ScaleTransform.ScaleYProperty, doubleAnimation2);
		}
		_subTierCache[parentIndex] = new SubTierVisuals(
			new List<System.Windows.Shapes.Path>(_subSectorPaths),
			new List<Grid>(_subContentContainers),
			new List<TranslateTransform>(_subSectorTransforms),
			new List<TranslateTransform>(_subContainerTransforms),
			new List<double>(_subSectorAngles));
	}

}
