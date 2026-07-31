using CPF;
using CPF.Controls;
using SignalCpf.UI.ViewModels;

namespace SignalCpf.UI.Controls;

public class MessageListItem : ListBoxItem
{
    protected override void InitializeComponent()
    {
        Width = "100%";
        Background = "Transparent";
        Height = "auto";

        Children.Add(new Border
        {
            MarginTop = 4,
            MarginBottom = 4,
            MaxWidth = 420,
            Padding = "10,6,10,6",
            CornerRadius = "8",
            Child = new TextBlock
            {
                Bindings =
                {
                    { nameof(TextBlock.Text), nameof(MessageItemViewModel.Body) },
                    { nameof(TextBlock.Foreground), nameof(MessageItemViewModel.BubbleForeground) },
                },
            },
            Bindings =
            {
                { nameof(Background), nameof(MessageItemViewModel.BubbleBackground) },
                {
                    nameof(MarginLeft),
                    nameof(MessageItemViewModel.IsOutgoing),
                    null,
                    BindingMode.OneWay,
                    a => (bool)a! ? (object)"auto" : 8,
                    null
                },
                {
                    nameof(MarginRight),
                    nameof(MessageItemViewModel.IsOutgoing),
                    null,
                    BindingMode.OneWay,
                    a => (bool)a! ? (object)8 : "auto",
                    null
                },
            },
        });
    }
}
