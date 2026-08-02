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
        Height = 68;
        Background = "Transparent";
        MarginLeft = 8;
        MarginRight = 8;
        MarginTop = 2;
        MarginBottom = 2;
        CornerRadius = "10";

        Children.Add(new Panel
        {
            Width = "100%",
            Height = "100%",
            Children =
            {
                // Avatar
                new Border
                {
                    Width = 48,
                    Height = 48,
                    MarginLeft = 10,
                    MarginTop = 10,
                    Background = "#C5C8CE",
                    CornerRadius = "24",
                    Child = new TextBlock
                    {
                        FontSize = 18,
                        FontStyle = FontStyles.Bold,
                        Foreground = "#FFFFFF",
                        MarginLeft = 16,
                        MarginTop = 12,
                        Bindings =
                        {
                            { nameof(TextBlock.Text), nameof(ConversationItemViewModel.AvatarInitial) },
                        },
                    },
                },
                // Title
                new TextBlock
                {
                    FontStyle = FontStyles.Bold,
                    Foreground = "#1B1B1B",
                    FontSize = 14,
                    MarginLeft = 70,
                    MarginTop = 12,
                    MarginRight = 56,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(ConversationItemViewModel.Title) },
                    },
                },
                // Preview
                new TextBlock
                {
                    Foreground = "#6B6B6B",
                    FontSize = 13,
                    MarginLeft = 70,
                    MarginTop = 34,
                    MarginRight = 56,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(ConversationItemViewModel.Preview) },
                    },
                },
                // Time
                new TextBlock
                {
                    Foreground = "#8E8E8E",
                    FontSize = 12,
                    MarginTop = 12,
                    MarginRight = 12,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(ConversationItemViewModel.TimeLabel) },
                        {
                            nameof(MarginLeft),
                            nameof(ConversationItemViewModel.TimeLabel),
                            null,
                            BindingMode.OneWay,
                            _ => (object)"auto",
                            null
                        },
                    },
                },
                // Unread badge
                new Border
                {
                    Width = 22,
                    Height = 22,
                    MarginTop = 36,
                    MarginRight = 12,
                    Background = "#3A76F0",
                    CornerRadius = "11",
                    Bindings =
                    {
                        {
                            nameof(Visibility),
                            nameof(ConversationItemViewModel.HasUnread),
                            null,
                            BindingMode.OneWay,
                            a => a is true ? Visibility.Visible : Visibility.Collapsed,
                            null
                        },
                        {
                            nameof(MarginLeft),
                            nameof(ConversationItemViewModel.HasUnread),
                            null,
                            BindingMode.OneWay,
                            _ => (object)"auto",
                            null
                        },
                    },
                    Child = new TextBlock
                    {
                        FontSize = 11,
                        Foreground = "#FFFFFF",
                        MarginLeft = 6,
                        MarginTop = 3,
                        Bindings =
                        {
                            { nameof(TextBlock.Text), nameof(ConversationItemViewModel.UnreadLabel) },
                        },
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
                { nameof(Background), "#F0F0F0" },
            },
        });
        Triggers.Add(new Trigger
        {
            Property = nameof(IsSelected),
            PropertyConditions = a => (bool)a!,
            Setters =
            {
                { nameof(Background), "#E7E7E7" },
            },
        });
    }
}
