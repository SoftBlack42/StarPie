using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace WinPieGestures;

/// <summary>
/// 手势轨迹浮层：覆盖"手势起点所在显示器"的透明置顶窗（定位一次，之后不再移动/缩放），
/// 轨迹点按该屏 DIP 缩放换算绘制——单屏内精确，无跨屏错位，任意方向/长度不裁剪不变形。
/// </summary>
public class GestureTrailOverlay : Window
{
	private readonly Canvas _canvas;
	private readonly Polyline _line;
	private readonly Ellipse _startDot;
	private readonly Border _hintBorder;
	private readonly TextBlock _hintText;
	private string _lastHintText = string.Empty;
	private double _lastHintWidth;
	private double _lastHintHeight;
	private double _leftDIP;
	private double _topDIP;

	[DllImport("user32.dll")]
	private static extern nint MonitorFromPoint(POINT pt, uint dwFlags);

	[DllImport("user32.dll")]
	private static extern bool GetMonitorInfo(nint hMonitor, ref MONITORINFO lpmi);

	[StructLayout(LayoutKind.Sequential)]
	private struct POINT
	{
		public int x;
		public int y;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct RECT
	{
		public int Left;
		public int Top;
		public int Right;
		public int Bottom;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct MONITORINFO
	{
		public uint cbSize;
		public RECT rcMonitor;
		public RECT rcWork;
		public uint dwFlags;
	}

	private const uint MONITOR_DEFAULTTONEAREST = 2;

	public GestureTrailOverlay()
	{
		WindowStyle = WindowStyle.None;
		AllowsTransparency = true;
		Background = Brushes.Transparent;
		Topmost = true;
		ShowInTaskbar = false;
		ShowActivated = false;
		Focusable = false;
		ResizeMode = ResizeMode.NoResize;
		IsHitTestVisible = false;
		UseLayoutRounding = true;
		SnapsToDevicePixels = true;

		_canvas = new Canvas { IsHitTestVisible = false };
		_line = new Polyline
		{
			Stroke = new SolidColorBrush(Color.FromArgb(255, 108, 99, 255)),
			StrokeThickness = 3.0,
			StrokeStartLineCap = PenLineCap.Round,
			StrokeEndLineCap = PenLineCap.Round,
			StrokeLineJoin = PenLineJoin.Round
		};
		_startDot = new Ellipse { Width = 6.0, Height = 6.0, Fill = new SolidColorBrush(Color.FromRgb(244, 63, 94)) };
		_hintText = new TextBlock
		{
			FontSize = 18.0,
			FontWeight = FontWeights.Bold,
			Foreground = Brushes.White,
			TextAlignment = TextAlignment.Center
		};
		_hintBorder = new Border
		{
			Child = _hintText,
			Background = new SolidColorBrush(Color.FromArgb(210, 20, 25, 45)),
			BorderBrush = new SolidColorBrush(Color.FromArgb(130, 255, 255, 255)),
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(7.0),
			Padding = new Thickness(12.0, 5.0, 12.0, 5.0),
			Visibility = Visibility.Collapsed
		};
		Panel.SetZIndex(_line, 0);
		Panel.SetZIndex(_startDot, 1);
		Panel.SetZIndex(_hintBorder, 2);
		_canvas.Children.Add(_line);
		_canvas.Children.Add(_startDot);
		_canvas.Children.Add(_hintBorder);
		Content = _canvas;
	}

	/// <summary>覆盖手势起点所在显示器（物理矩形 ÷ 该屏缩放 → DIP）；只定位，不显示。</summary>
	public void PositionAt(double screenX, double screenY, double scaleX, double scaleY)
	{
		double sx = scaleX > 0.0 ? scaleX : 1.0;
		double sy = scaleY > 0.0 ? scaleY : 1.0;
		RECT rc;
		try
		{
			nint mon = MonitorFromPoint(new POINT { x = (int)Math.Round(screenX), y = (int)Math.Round(screenY) }, MONITOR_DEFAULTTONEAREST);
			MONITORINFO mi = default;
			mi.cbSize = (uint)Marshal.SizeOf<MONITORINFO>();
			if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi))
			{
				throw new InvalidOperationException("monitor");
			}
			rc = mi.rcMonitor;
		}
		catch
		{
			// 兜底：虚拟屏幕（主屏度量，近似）
			rc = new RECT
			{
				Left = (int)Math.Round(SystemParameters.VirtualScreenLeft * sx),
				Top = (int)Math.Round(SystemParameters.VirtualScreenTop * sy),
				Right = (int)Math.Round((SystemParameters.VirtualScreenLeft + SystemParameters.VirtualScreenWidth) * sx),
				Bottom = (int)Math.Round((SystemParameters.VirtualScreenTop + SystemParameters.VirtualScreenHeight) * sy)
			};
		}
		_leftDIP = rc.Left / sx;
		_topDIP = rc.Top / sy;
		Left = _leftDIP;
		Top = _topDIP;
		Width = (rc.Right - rc.Left) / sx;
		Height = (rc.Bottom - rc.Top) / sy;
	}

	/// <summary>开始轨迹：屏幕物理坐标 → 覆盖层局部坐标，画起点。</summary>
	public void BeginAt(double screenX, double screenY, double scaleX, double scaleY)
	{
		double sx = scaleX > 0.0 ? scaleX : 1.0;
		double sy = scaleY > 0.0 ? scaleY : 1.0;
		double lx = screenX / sx - _leftDIP;
		double ly = screenY / sy - _topDIP;
		_line.Points.Clear();
		_line.Points.Add(new Point(lx, ly));
		Canvas.SetLeft(_startDot, lx - 3.0);
		Canvas.SetTop(_startDot, ly - 3.0);
	}

	public void AddPoint(double screenX, double screenY, double scaleX, double scaleY)
	{
		double sx = scaleX > 0.0 ? scaleX : 1.0;
		double sy = scaleY > 0.0 ? scaleY : 1.0;
		_line.Points.Add(new Point(screenX / sx - _leftDIP, screenY / sy - _topDIP));
	}

	public void ClearTrail()
	{
		_line.Points.Clear();
		HideHint();
	}

	/// <summary>更新提示标签：文本 + 相对鼠标的八方向定位（Auto 已由调用方解析为具体方向）。</summary>
	public void UpdateHint(string text, double screenX, double screenY, double scaleX, double scaleY, string placement)
	{
		double sx = scaleX > 0.0 ? scaleX : 1.0;
		double sy = scaleY > 0.0 ? scaleY : 1.0;
		double cx = screenX / sx - _leftDIP;
		double cy = screenY / sy - _topDIP;
		double ox = 54.0;
		double oy = 54.0;
		switch (placement)
		{
		case "U":
			ox = 0.0;
			oy = -54.0;
			break;
		case "D":
			ox = 0.0;
			oy = 54.0;
			break;
		case "L":
			ox = -54.0;
			oy = 0.0;
			break;
		case "UL":
			ox = -40.0;
			oy = -40.0;
			break;
		case "UR":
			ox = 40.0;
			oy = -40.0;
			break;
		case "DL":
			ox = -40.0;
			oy = 40.0;
			break;
		case "DR":
			ox = 40.0;
			oy = 40.0;
			break;
		}
		double x = cx + ox;
		double y = cy + oy;
		if (x < 6.0)
		{
			x = 6.0;
		}
		if (x > Width - 6.0)
		{
			x = Width - 6.0;
		}
		if (y < 6.0)
		{
			y = 6.0;
		}
		if (y > Height - 6.0)
		{
			y = Height - 6.0;
		}
		_hintText.Text = text;
		// 文本未变时跳过显式 Measure 与尺寸重取：手势期间同一图样会以高频重复调用，
		// Measure 会强制布局计算，是轨迹浮层的主要 CPU 开销来源。
		if (!string.Equals(text, _lastHintText, StringComparison.Ordinal))
		{
			_hintBorder.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
			_lastHintWidth = _hintBorder.DesiredSize.Width;
			_lastHintHeight = _hintBorder.DesiredSize.Height;
			_lastHintText = text;
		}
		double w = _lastHintWidth;
		double h = _lastHintHeight;
		if (x + w > Width)
		{
			x = Width - w - 6.0;
		}
		if (y + h > Height)
		{
			y = Height - h - 6.0;
		}
		Canvas.SetLeft(_hintBorder, x);
		Canvas.SetTop(_hintBorder, y);
		_hintBorder.Visibility = Visibility.Visible;
	}

	public void HideHint()
	{
		_hintBorder.Visibility = Visibility.Collapsed;
		_lastHintText = string.Empty; // 下次手势重新 Measure，避免跨手势缓存陈旧尺寸
	}
}