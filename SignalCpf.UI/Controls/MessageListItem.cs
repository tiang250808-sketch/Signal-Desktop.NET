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
        Padding = "12,2,12,2";

        Children.Add(new StackPanel
        {
            Width = "100%",
            Bindings =
            {
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
            Children =
            {
                new Border
                {
                    MaxWidth = 420,
                    Padding = "12,8,12,8",
                    CornerRadius = "18",
                    Child = new TextBlock
                    {
                        FontSize = 15,
                        Bindings =
                        {
                            { nameof(TextBlock.Text), nameof(MessageItemViewModel.Body) },
                            { nameof(TextBlock.Foreground), nameof(MessageItemViewModel.BubbleForeground) },
                        },
                    },
                    Bindings =
                    {
                        { nameof(Background), nameof(MessageItemViewModel.BubbleBackground) },
                    },
                },
                new TextBlock
                {
                    FontSize = 11,
                    Foreground = "#8E8E8E",
                    MarginTop = 2,
                    MarginLeft = 4,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(MessageItemViewModel.MetaLabel) },
                        {
                            nameof(MarginLeft),
                            nameof(MessageItemViewModel.IsOutgoing),
                            null,
                            BindingMode.OneWay,
                            a => (bool)a! ? (object)"auto" : 4,
                            null
                        },
                        {
                            nameof(MarginRight),
                            nameof(MessageItemViewModel.IsOutgoing),
                            null,
                            BindingMode.OneWay,
                            a => (bool)a! ? (object)4 : "auto",
                            null
                        },
                    },
                },
            },
        });
    }
}
