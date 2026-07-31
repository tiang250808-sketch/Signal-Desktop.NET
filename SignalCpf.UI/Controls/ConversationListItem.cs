using CPF;
using CPF.Controls;
using CPF.Drawing;
using CPF.Styling;
using SignalCpf.UI.ViewModels;

namespace SignalCpf.UI.Controls;

public class ConversationListItem : ListBoxItem
{
    protected override void InitializeComponent()
    {
        Width = "100%";
        Height = 56;
        Background = "Transparent";

        Children.Add(new StackPanel
        {
            MarginLeft = 12,
            MarginRight = 12,
            MarginTop = 10,
            Children =
            {
                new TextBlock
                {
                    FontStyle = FontStyles.Bold,
                    Foreground = "#0F172A",
                    FontSize = 14,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(ConversationItemViewModel.Title) },
                    },
                },
                new TextBlock
                {
                    Foreground = "#475569",
                    FontSize = 12,
                    MarginTop = 2,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(ConversationItemViewModel.Preview) },
                    },
                },
            },
        });

        Triggers.Add(new Trigger
        {
            Property = nameof(IsMouseOver),
            PropertyConditions = a => (bool)a! && !IsSelected,
            Setters =
            {
                { nameof(Background), "#E2E8F0" },
            },
        });
        Triggers.Add(new Trigger
        {
            Property = nameof(IsSelected),
            PropertyConditions = a => (bool)a!,
            Setters =
            {
                { nameof(Background), "#CBD5E1" },
            },
        });
    }
}
