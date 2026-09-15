using System;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WinPieGestures;

public class ClassicRingRenderer : BaseStyleRenderer
{
	protected override void GetDefaultColors(string theme, out string sectorBgHex, out string sectorBorderHex, out string highlightBgHex, out string highlightBorderHex, out string textHex)
	{
		if (theme == "Light")
		{
			sectorBgHex = "#F5F8FAFC";
			sectorBorderHex = "#3564748B";
			highlightBgHex = "#FF2563EB";
			highlightBorderHex = "#FF93C5FD";
			textHex = "#FF0F172A";
		}
		else
		{
			sectorBgHex = "#F018181B";
			sectorBorderHex = "#40FFFFFF";
			highlightBgHex = "#FF2563EB";
			highlightBorderHex = "#FF93C5FD";
			textHex = "#FFF8FAFC";
		}
	}

	// 冻结复用的高光光晕：高亮切换仅引用赋值，避免每次 new DropShadowEffect。
	private System.Windows.Media.Effects.DropShadowEffect? _sectorGlowEffect;

	protected override void PostInitialize()
	{
		base.BorderThickness = 1.0;
		base.HighlightBorderThickness = 2.0;
		_sectorGlowEffect = CreateFrozenDropShadow(GetEffectiveGlowColor(), GetEffectiveGlowRadius(20.0), 2.0, GetEffectiveGlowOpacity(0.75));
	}

	public override void RenderDecorations(Canvas canvas, Grid coreGrid, double cx, double cy, double wheelRadius, double coreRadius, int insertIndex)
	{
		Color color = (base.IsLightTheme ? Color.FromArgb(70, 100, 116, 139) : Color.FromArgb(45, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		Color color2 = (base.IsLightTheme ? Color.FromArgb(100, 71, 85, 105) : Color.FromArgb(70, byte.MaxValue, byte.MaxValue, byte.MaxValue));
		double num = wheelRadius + 8.0;
		Ellipse element = new Ellipse
		{
			Width = num * 2.0,
			Height = num * 2.0,
			Stroke = new SolidColorBrush(color),
			StrokeThickness = 1.0,
			StrokeDashArray = new DoubleCollection { 3.0, 5.0 },
			Tag = "Deco_SpatialOuterOrbit"
		};
		Canvas.SetLeft(element, cx - num);
		Canvas.SetTop(element, cy - num);
		Panel.SetZIndex(element, 0);
		canvas.Children.Add(element);
		double[] array = new double[4] { 0.0, 90.0, 180.0, 270.0 };
		for (int i = 0; i < array.Length; i++)
		{
			double num2 = array[i] * Math.PI / 180.0;
			double num3 = wheelRadius + 4.0;
			double num4 = wheelRadius + 11.0;
			Line element2 = new Line
			{
				X1 = cx + num3 * Math.Cos(num2),
				Y1 = cy + num3 * Math.Sin(num2),
				X2 = cx + num4 * Math.Cos(num2),
				Y2 = cy + num4 * Math.Sin(num2),
				Stroke = new SolidColorBrush(color2),
				StrokeThickness = 1.2,
				Tag = "Deco_CompassTick"
			};
			Panel.SetZIndex(element2, 0);
			canvas.Children.Add(element2);
		}
		Ellipse element3 = new Ellipse
		{
			Width = coreRadius * 2.0 + 8.0,
			Height = coreRadius * 2.0 + 8.0,
			Stroke = new SolidColorBrush(Color.FromArgb(50, 59, 130, 246)),
			StrokeThickness = 1.2,
			Tag = "Deco_ClassicInnerRing"
		};
		Canvas.SetLeft(element3, cx - (coreRadius + 4.0));
		Canvas.SetTop(element3, cy - (coreRadius + 4.0));
		Panel.SetZIndex(element3, 0);
		canvas.Children.Add(element3);
	}

	public override void ApplySectorHighlight(Path path, bool isHighlighted)
	{
		path.Effect = (isHighlighted ? _sectorGlowEffect : null);
	}
}
