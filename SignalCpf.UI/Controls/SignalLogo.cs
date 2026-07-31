using CPF;
using CPF.Controls;
using CPF.Drawing;
using CPF.Shapes;

namespace SignalCpf.UI.Controls;

public class SignalLogo : Control
{
    protected override void InitializeComponent()
    {
        Width = 112;
        Height = 32;
        ClipToBounds = false;
        Children.Add(new Panel
        {
            Width = 112,
            Height = 32,
            Children =
            {
                new Ellipse
                {
                    Width = 28,
                    Height = 28,
                    MarginLeft = 0,
                    MarginTop = 2,
                    Fill = "#3B45FD",
                    StrokeFill = null,
                    IsAntiAlias = true,
                },
                new TextBlock
                {
                    Text = "Signal",
                    FontSize = 22,
                    FontStyle = FontStyles.Bold,
                    Foreground = "#3B45FD",
                    MarginLeft = 36,
                    MarginTop = 2,
                },
            },
        });
    }
}
