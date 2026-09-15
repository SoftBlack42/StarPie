using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace WinPieGestures
{
    /// <summary>
    /// Cat Paw Style: Adorable pastel sakura aesthetics, perky organic cat ears, and 3D Q-pop center paw cushions.
    /// </summary>
    public class CatPawRenderer : BaseStyleRenderer
    {
        protected override void GetDefaultColors(string theme, out string sectorBgHex, out string sectorBorderHex, out string highlightBgHex, out string highlightBorderHex, out string textHex)
        {
            if (theme != "Custom")
            {
                sectorBgHex = "#FFF7F9";       // Creamy soft sakura white
                sectorBorderHex = "#F472B6";   // Soft pink border
                highlightBgHex = "#FB7185";    // Sweet Sakura Rose
                highlightBorderHex = "#FFE4E6";
                textHex = "#881337";           // Deep rosy cocoa text
            }
            else
            {
                base.GetDefaultColors(theme, out sectorBgHex, out sectorBorderHex, out highlightBgHex, out highlightBorderHex, out textHex);
            }
        }

        protected override bool UseStandardLightThemeFallback()
        {
            return false;
        }

        // 冻结复用的光晕 Effect：高亮切换仅引用赋值，避免每次 new DropShadowEffect。
        private System.Windows.Media.Effects.DropShadowEffect? _sectorGlowEffect;

        private System.Windows.Media.Effects.DropShadowEffect? _sectorIdleEffect;

        private System.Windows.Media.Effects.DropShadowEffect? _pawGlowEffect;

        protected override void PostInitialize()
        {
            BorderThickness = 1.4;
            HighlightBorderThickness = 2.2;
            CoreBgBrush = new SolidColorBrush(Color.FromRgb(255, 245, 247));
            CoreBorderBrush = new SolidColorBrush(Color.FromRgb(244, 114, 182));
            _sectorGlowEffect = CreateFrozenDropShadow(GetEffectiveGlowColor(), GetEffectiveGlowRadius(18.0), 0.0, GetEffectiveGlowOpacity(0.85));
            _sectorIdleEffect = CreateFrozenDropShadow(Color.FromRgb(244, 114, 182), 8.0, 1.0, 0.25);
            _pawGlowEffect = CreateFrozenDropShadow(Color.FromRgb(244, 63, 94), 18.0, 0.0, 0.95);
        }

        public override void RenderDecorations(Canvas canvas, Grid coreGrid, double cx, double cy, double wheelRadius, double coreRadius, int insertIndex)
        {
            // 1. Prominent Natural Cat Ears (Widely spaced on top-left ~216° and top-right ~324°)
            double earSize = Math.Max(30.0, wheelRadius * 0.36);
            
            // Left Ear (centered at 216° - prominent top-left at 10:15 position)
            double leftCenterRad = 216.0 * Math.PI / 180.0;
            double leftBase1Rad = 196.0 * Math.PI / 180.0;
            double leftBase2Rad = 244.0 * Math.PI / 180.0;

            double lx1 = cx + (wheelRadius - 2.0) * Math.Cos(leftBase1Rad);
            double ly1 = cy + (wheelRadius - 2.0) * Math.Sin(leftBase1Rad);
            double lx2 = cx + (wheelRadius + earSize) * Math.Cos(leftCenterRad);
            double ly2 = cy + (wheelRadius + earSize) * Math.Sin(leftCenterRad);
            double lx3 = cx + (wheelRadius - 2.0) * Math.Cos(leftBase2Rad);
            double ly3 = cy + (wheelRadius - 2.0) * Math.Sin(leftBase2Rad);

            var leftEar = new Path
            {
                Data = Geometry.Parse($"M {lx1:F1},{ly1:F1} Q {(lx1*0.45+lx2*0.55-4):F1},{(ly1*0.45+ly2*0.55-4):F1} {lx2:F1},{ly2:F1} Q {(lx3*0.45+lx2*0.55+2):F1},{(ly3*0.45+ly2*0.55-3):F1} {lx3:F1},{ly3:F1} Z"),
                Fill = DefaultSectorBrush,
                Stroke = SectorBorderBrush,
                StrokeThickness = 1.5,
                Tag = "Deco_LeftEar",
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(244, 114, 182),
                    BlurRadius = 10,
                    ShadowDepth = 1,
                    Opacity = 0.40
                }
            };
            Panel.SetZIndex(leftEar, 0);
            canvas.Children.Add(leftEar);

            // Right Ear (centered at 324° - prominent top-right at 1:45 position)
            double rightCenterRad = 324.0 * Math.PI / 180.0;
            double rightBase1Rad = 296.0 * Math.PI / 180.0;
            double rightBase2Rad = 344.0 * Math.PI / 180.0;

            double rx1 = cx + (wheelRadius - 2.0) * Math.Cos(rightBase1Rad);
            double ry1 = cy + (wheelRadius - 2.0) * Math.Sin(rightBase1Rad);
            double rx2 = cx + (wheelRadius + earSize) * Math.Cos(rightCenterRad);
            double ry2 = cy + (wheelRadius + earSize) * Math.Sin(rightCenterRad);
            double rx3 = cx + (wheelRadius - 2.0) * Math.Cos(rightBase2Rad);
            double ry3 = cy + (wheelRadius - 2.0) * Math.Sin(rightBase2Rad);

            var rightEar = new Path
            {
                Data = Geometry.Parse($"M {rx1:F1},{ry1:F1} Q {(rx1*0.45+rx2*0.55-2):F1},{(ry1*0.45+ry2*0.55-3):F1} {rx2:F1},{ry2:F1} Q {(rx3*0.45+rx2*0.55+4):F1},{(ry3*0.45+ry2*0.55-4):F1} {rx3:F1},{ry3:F1} Z"),
                Fill = DefaultSectorBrush,
                Stroke = SectorBorderBrush,
                StrokeThickness = 1.5,
                Tag = "Deco_RightEar",
                Effect = new DropShadowEffect
                {
                    Color = Color.FromRgb(244, 114, 182),
                    BlurRadius = 10,
                    ShadowDepth = 1,
                    Opacity = 0.40
                }
            };
            Panel.SetZIndex(rightEar, 0);
            canvas.Children.Add(rightEar);

            // Inner Ear Pink Cushions
            var leftInner = new Path
            {
                Data = Geometry.Parse($"M {(lx1 * 0.68 + lx2 * 0.32):F1},{(ly1 * 0.68 + ly2 * 0.32):F1} L {(lx2 * 0.86 + lx1 * 0.07 + lx3 * 0.07):F1},{(ly2 * 0.86 + ly1 * 0.07 + ly3 * 0.07):F1} L {(lx3 * 0.68 + lx2 * 0.32):F1},{(ly3 * 0.68 + ly2 * 0.32):F1} Z"),
                Fill = new LinearGradientBrush(Color.FromRgb(244, 114, 182), Color.FromRgb(251, 113, 133), 90.0),
                Tag = "Deco_LeftInnerEar"
            };
            Panel.SetZIndex(leftInner, 0);
            canvas.Children.Add(leftInner);

            var rightInner = new Path
            {
                Data = Geometry.Parse($"M {(rx1 * 0.68 + rx2 * 0.32):F1},{(ry1 * 0.68 + ry2 * 0.32):F1} L {(rx2 * 0.86 + rx1 * 0.07 + rx3 * 0.07):F1},{(ry2 * 0.86 + ry1 * 0.07 + ry3 * 0.07):F1} L {(rx3 * 0.68 + rx2 * 0.32):F1},{(ry3 * 0.68 + ry2 * 0.32):F1} Z"),
                Fill = new LinearGradientBrush(Color.FromRgb(244, 114, 182), Color.FromRgb(251, 113, 133), 90.0),
                Tag = "Deco_RightInnerEar"
            };
            Panel.SetZIndex(rightInner, 0);
            canvas.Children.Add(rightInner);

            // 2. 3D Q-Pop Cat Paw Centerpiece
            if (coreGrid != null)
            {
                // Hide any conflicting text or default exit crosses
                foreach (UIElement child in coreGrid.Children)
                {
                    if (child is FrameworkElement fe && (fe.Name == "CoreExitIcon" || fe.Name == "CoreTextPanel" || fe.Tag?.ToString() == "PreviewExitIcon"))
                    {
                        fe.Visibility = Visibility.Collapsed;
                    }
                }

                var existingPaw = coreGrid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "DynamicPawGrid");
                if (existingPaw != null) coreGrid.Children.Remove(existingPaw);

                if (!ConfigManager.CurrentConfig.ShowCoreIcon)
                {
                    return;
                }

                var pawGrid = new Grid
                {
                    Name = "DynamicPawGrid",
                    Width = coreRadius * 2.0,
                    Height = coreRadius * 2.0,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible = false
                };

                // Pink pad brush with sweet sakura 3D gradient
                var padGradient = new LinearGradientBrush
                {
                    StartPoint = new Point(0, 0),
                    EndPoint = new Point(0, 1)
                };
                padGradient.GradientStops.Add(new GradientStop(Color.FromRgb(244, 114, 182), 0.0));
                padGradient.GradientStops.Add(new GradientStop(Color.FromRgb(251, 113, 133), 1.0));

                // A. Main Palm Pad (圆润饱满立体大肉垫)
                double padW = coreRadius * 0.94;
                double padH = coreRadius * 0.86;
                var mainPad = new Path
                {
                    Name = "PawMainPad",
                    Width = padW,
                    Height = padH,
                    Stretch = Stretch.Fill,
                    Data = Geometry.Parse("M 25,52 C 5,52 0,35 3,20 C 6,8 18,9 25,18 C 32,9 44,8 47,20 C 50,35 45,52 25,52 Z"),
                    Fill = padGradient,
                    Margin = new Thickness(0, coreRadius * 0.62, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Effect = new DropShadowEffect
                    {
                        Color = Color.FromRgb(244, 114, 182),
                        BlurRadius = 8,
                        ShadowDepth = 1,
                        Opacity = 0.45
                    }
                };
                pawGrid.Children.Add(mainPad);

                // Main pad specular 3D gloss highlight
                var mainGloss = new Ellipse
                {
                    Width = padW * 0.36,
                    Height = padH * 0.22,
                    Fill = new SolidColorBrush(Color.FromArgb(140, 255, 255, 255)),
                    Margin = new Thickness(0, coreRadius * 0.74, 0, 0),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top
                };
                pawGrid.Children.Add(mainGloss);

                // B. 4 Perky Rounded Toe Beans (4 个圆润饱满肉球)
                double toeW = coreRadius * 0.32;
                double toeH = coreRadius * 0.38;

                void AddToe(double leftOffset, double topOffset, double angle)
                {
                    var toeContainer = new Grid
                    {
                        Width = toeW,
                        Height = toeH,
                        Margin = new Thickness(leftOffset, topOffset, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top,
                        RenderTransformOrigin = new Point(0.5, 0.5),
                        RenderTransform = new RotateTransform(angle)
                    };

                    var toeEllipse = new Ellipse
                    {
                        Width = toeW,
                        Height = toeH,
                        Fill = padGradient,
                        Effect = new DropShadowEffect
                        {
                            Color = Color.FromRgb(244, 114, 182),
                            BlurRadius = 4,
                            ShadowDepth = 1,
                            Opacity = 0.35
                        }
                    };
                    toeContainer.Children.Add(toeEllipse);

                    // Toe gloss dot
                    var toeGloss = new Ellipse
                    {
                        Width = toeW * 0.38,
                        Height = toeH * 0.28,
                        Fill = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255)),
                        Margin = new Thickness(toeW * 0.18, toeH * 0.12, 0, 0),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Top
                    };
                    toeContainer.Children.Add(toeGloss);

                    pawGrid.Children.Add(toeContainer);
                }

                AddToe(coreRadius * 0.20, coreRadius * 0.28, -26.0);
                AddToe(coreRadius * 0.58, coreRadius * 0.15, -9.0);
                AddToe(coreRadius * 1.10, coreRadius * 0.15, 9.0);
                AddToe(coreRadius * 1.48, coreRadius * 0.28, 26.0);

                coreGrid.Children.Add(pawGrid);
            }
        }

        public override void ApplySectorHighlight(Path path, bool isHighlighted)
        {
            path.Effect = (isHighlighted ? _sectorGlowEffect : _sectorIdleEffect);
        }

        public override void ApplyExitHighlight(Path exitIcon, bool isHighlighted)
        {
            // Highlight the entire cat paw on core exit hover
            if (exitIcon.Parent is Grid grid)
            {
                var pawGrid = grid.Children.OfType<Grid>().FirstOrDefault(g => g.Name == "DynamicPawGrid");
                if (pawGrid != null)
                {
                    pawGrid.Effect = (isHighlighted ? _pawGlowEffect : null);
                }
            }
        }
    }
}
