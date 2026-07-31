using CPF.Controls;
using CPF.Shapes;

namespace SignalCpf.UI.Controls;

public class SignalBubbleIcon : Control
{
    protected override void InitializeComponent()
    {
        Width = 48;
        Height = 48;
        Children.Add(new Ellipse
        {
            Width = 48,
            Height = 48,
            Fill = "#3B45FD",
            StrokeFill = null,
            IsAntiAlias = true,
        });
    }
}
