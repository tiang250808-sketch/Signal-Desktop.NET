using CPF;
using CPF.Controls;
using CPF.Drawing;
using CPF.Styling;
using SignalCpf.UI.ViewModels;

namespace SignalCpf.UI.Controls;

public class ContactPickListItem : ListBoxItem
{
    protected override void InitializeComponent()
    {
        Width = "100%";
        Height = 56;
        Background = "Transparent";
        MarginLeft = 8;
        MarginRight = 8;
        CornerRadius = "10";

        Children.Add(new Panel
        {
            Width = "100%",
            Height = "100%",
            Children =
            {
                new Border
                {
                    Width = 40,
                    Height = 40,
                    MarginLeft = 8,
                    MarginTop = 8,
                    Background = "#C5C8CE",
                    CornerRadius = "20",
                    Child = new TextBlock
                    {
                        FontSize = 16,
                        FontStyle = FontStyles.Bold,
                        Foreground = "#FFFFFF",
                        MarginLeft = 13,
                        MarginTop = 8,
                        Bindings =
                        {
                            { nameof(TextBlock.Text), nameof(ContactItemViewModel.Initial) },
                        },
                    },
                },
                new TextBlock
                {
                    FontSize = 14,
                    FontStyle = FontStyles.Bold,
                    Foreground = "#1B1B1B",
                    MarginLeft = 60,
                    MarginTop = 18,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(ContactItemViewModel.DisplayName) },
                    },
                },
            },
        });

        Triggers.Add(new Trigger
        {
            Property = nameof(IsMouseOver),
            PropertyConditions = a => (bool)a!,
            Setters =
            {
                { nameof(Background), "#F0F0F0" },
            },
        });
    }
}
