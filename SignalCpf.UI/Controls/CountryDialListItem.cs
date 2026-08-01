using CPF;
using CPF.Controls;
using CPF.Drawing;
using CPF.Styling;
using SignalCpf.UI.Helpers;

namespace SignalCpf.UI.Controls;

public class CountryDialListItem : ListBoxItem
{
    protected override void InitializeComponent()
    {
        Width = "100%";
        Height = 36;
        Background = "Transparent";

        Children.Add(new TextBlock
        {
            MarginLeft = 12,
            MarginRight = 12,
            MarginTop = 8,
            FontSize = 13,
            Foreground = "#1B1B1B",
            Bindings =
            {
                { nameof(TextBlock.Text), nameof(CountryDialOption.Display) },
            },
        });

        Triggers.Add(new Trigger
        {
            Property = nameof(IsMouseOver),
            PropertyConditions = a => (bool)a! && !IsSelected,
            Setters =
            {
                { nameof(Background), "#EEF2FF" },
            },
        });
        Triggers.Add(new Trigger
        {
            Property = nameof(IsSelected),
            PropertyConditions = a => (bool)a!,
            Setters =
            {
                { nameof(Background), "#DBEAFE" },
            },
        });
    }
}
