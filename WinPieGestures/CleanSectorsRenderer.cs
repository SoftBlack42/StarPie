using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WinPieGestures;

public class CleanSectorsRenderer : BaseStyleRenderer
{
	protected override void GetDefaultColors(string theme, out string sectorBgHex, out string sectorBorderHex, out string highlightBgHex, out string highlightBorderHex, out string textHex)
	{
		if (theme == "Light")
		{
			sectorBgHex = "#F8FFFFFF";
			sectorBorderHex = "#35CBD5E1";
			highlightBgHex = "#FF059669";
			highlightBorderHex = "#FF10B981";
			textHex = "#FF0F172A";
		}
		else
		{
			sectorBgHex = "#F20F172A";
			sectorBorderHex = "#35334155";
			highlightBgHex = "#FF10B981";
			highlightBorderHex = "#FF6EE7B7";
			textHex = "#FFF8FAFC";
		}
	}

	// 冻结复用的高光光晕：高亮切换仅引用赋值，避免每次 new DropShadowEffect。
	private System.Windows.Media.Effects.DropShadowEffect? _sectorGlowEffect;

	protected override void PostInitialize()
	{
		base.BorderThickness = 0.9;
		base.HighlightBorderThickness = 1.6;
		_sectorGlowEffect = CreateFrozenDropShadow(GetEffectiveGlowColor(), GetEffectiveGlowRadius(16.0), 0.0, GetEffectiveGlowOpacity(0.7));
	}

	public override void RenderDecorations(Canvas canvas, Grid coreGrid, double cx, double cy, double wheelRadius, double coreRadius, int insertIndex)
	{
	}

	public override void ApplySectorHighlight(Path path, bool isHighlighted)
	{
		path.Effect = (isHighlighted ? _sectorGlowEffect : null);
	}
}
