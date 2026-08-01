using CPF;
using CPF.Controls;
using CPF.Drawing;
using CPF.Shapes;

namespace SignalCpf.UI.Controls;

/// <summary>Large centered Signal mark (bubble + dashed ring), matching Desktop install art.</summary>
public class SignalHeroLogo : Control
{
    private const string SignalBlue = "#3A76F0";

    protected override void InitializeComponent()
    {
        Width = 120;
        Height = 120;
        ClipToBounds = false;
        Children.Add(new Panel
        {
            Width = 120,
            Height = 120,
            Children =
            {
                // Dashed outer ring (approximated with short arcs).
                BuildRingSegment(0, -48),
                BuildRingSegment(36, -33),
                BuildRingSegment(48, 0),
                BuildRingSegment(36, 33),
                BuildRingSegment(0, 48),
                BuildRingSegment(-36, 33),
                BuildRingSegment(-48, 0),
                BuildRingSegment(-36, -33),
                // Solid bubble
                new Ellipse
                {
                    Width = 72,
                    Height = 72,
                    MarginLeft = 24,
                    MarginTop = 24,
                    Fill = SignalBlue,
                    StrokeFill = null,
                    IsAntiAlias = true,
                },
                // Inner highlight circle
                new Ellipse
                {
                    Width = 28,
                    Height = 28,
                    MarginLeft = 46,
                    MarginTop = 42,
                    Fill = "#FFFFFF",
                    StrokeFill = null,
                    IsAntiAlias = true,
                },
            },
        });
    }

    private static UIElement BuildRingSegment(double offsetX, double offsetY)
    {
        const double cx = 60;
        const double cy = 60;
        return new Ellipse
        {
            Width = 14,
            Height = 14,
            MarginLeft = cx + offsetX - 7,
            MarginTop = cy + offsetY - 7,
            Fill = SignalBlue,
            StrokeFill = null,
            IsAntiAlias = true,
        };
    }
}
