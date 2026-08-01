using CPF;
using CPF.Controls;
using CPF.Drawing;
using CPF.Shapes;
using SignalCpf.UI.Controls;
using SignalCpf.UI.ViewModels;

namespace SignalCpf.UI;

public class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _shuttingDown;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        CommandContext = viewModel;
    }

    protected override void InitializeComponent()
    {
        Title = "Signal";
        Width = 960;
        Height = 640;
        MinWidth = 720;
        MinHeight = 480;
        Background = null;
        CanResize = true;

        Children.Add(new WindowFrame(
            this,
            new Panel
            {
                Width = "100%",
                Height = "100%",
                Background = "#F6F6F6",
                Children =
                {
                    BuildInstallScreen(),
                    BuildChatScreen(),
                },
            })
        {
            MaximizeBox = true,
            MinimizeBox = true,
            ShowIcon = false,
            Background = "#FFFFFF",
        });

        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += async (_, _) =>
        {
            if (_shuttingDown)
                return;
            _shuttingDown = true;
            await _viewModel.ShutdownAsync();
        };
    }

    private const string SignalBlue = "#3A76F0";
    private const string SignalBlueMuted = "#A8C4F5";
    private const string SendSmsDisabledBg = "#C5C8CE";

    private UIElement BuildInstallScreen()
    {
        return new Panel
        {
            Width = "100%",
            Height = "100%",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingInstallScreen),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                BuildLinkInstallPanel(),
                BuildRegisterPanel(),
            },
        };
    }

    private UIElement BuildLinkInstallPanel()
    {
        return new Panel
        {
            Width = "100%",
            Height = "100%",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingLinkMode),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                new SignalLogo
                {
                    MarginLeft = 32,
                    MarginTop = 36,
                    ZIndex = 1,
                },
                new Border
                {
                    MaxWidth = 760,
                    Padding = "22",
                    Background = "#FFFFFF",
                    CornerRadius = "8",
                    BorderFill = "#E5E7EB",
                    BorderStroke = "1",
                    Child = new StackPanel
                    {
                        Children =
                        {
                            BuildInstallModeToggle(),
                            new Grid
                            {
                                MarginTop = 16,
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition { Width = "auto" },
                                    new ColumnDefinition { Width = "auto" },
                                },
                                Children =
                                {
                                    BuildQrColumn(),
                                    BuildInstructionsColumn(),
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildInstallModeToggle()
    {
        return new Panel
        {
            Height = 32,
            Children =
            {
                new Button
                {
                    Content = "关联设备",
                    Width = 110,
                    Height = 30,
                    Commands =
                    {
                        {
                            nameof(Button.Click),
                            (s, e) => _ = _viewModel.SwitchToLinkModeCommand.ExecuteAsync(null)
                        },
                    },
                },
                new Button
                {
                    Content = "注册账户",
                    Width = 110,
                    Height = 30,
                    MarginLeft = 118,
                    Commands =
                    {
                        {
                            nameof(Button.Click),
                            (s, e) => _ = _viewModel.SwitchToRegisterModeCommand.ExecuteAsync(null)
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildRegisterPanel()
    {
        // Full-bleed, centered layout matching Signal Desktop "Create your Signal Account".
        return new Panel
        {
            Width = "100%",
            Height = "100%",
            Background = "#F6F6F6",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingRegisterMode),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                new StackPanel
                {
                    Width = 360,
                    Children =
                    {
                        new SignalHeroLogo
                        {
                            MarginTop = 64,
                            MarginLeft = 120,
                        },
                        new TextBlock
                        {
                            Text = "Create your Signal Account",
                            Width = 360,
                            FontSize = 22,
                            FontStyle = FontStyles.Bold,
                            Foreground = "#1B1B1B",
                            MarginTop = 28,
                            TextAlignment = TextAlignment.Center,
                        },
                        BuildPhoneNumberField(),
                        BuildSendSmsButton(),
                        new Button
                        {
                            Content = "Call",
                            Width = 60,
                            Height = 28,
                            MarginTop = 10,
                            MarginLeft = 150,
                            Background = null,
                            BorderFill = null,
                            Foreground = SignalBlueMuted,
                            Commands =
                            {
                                {
                                    nameof(Button.Click),
                                    (s, e) => _ = _viewModel.CallRegistrationCommand.ExecuteAsync(null)
                                },
                            },
                            Bindings =
                            {
                                {
                                    nameof(Button.IsEnabled),
                                    nameof(MainViewModel.CanSendRegistrationCode),
                                    null,
                                    BindingMode.OneWay
                                },
                            },
                        },
                        BuildRegisterFollowUpSteps(),
                        new TextBlock
                        {
                            Width = 360,
                            FontSize = 12,
                            Foreground = "#6B7280",
                            MarginTop = 16,
                            TextAlignment = TextAlignment.Center,
                            Bindings =
                            {
                                { nameof(TextBlock.Text), nameof(MainViewModel.StatusText) },
                            },
                        },
                        new Button
                        {
                            Content = "Link an existing device",
                            Width = 200,
                            Height = 28,
                            MarginTop = 8,
                            MarginLeft = 80,
                            Background = null,
                            BorderFill = null,
                            Foreground = SignalBlue,
                            Commands =
                            {
                                {
                                    nameof(Button.Click),
                                    (s, e) => _ = _viewModel.SwitchToLinkModeCommand.ExecuteAsync(null)
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildPhoneNumberField()
    {
        return new StackPanel
        {
            Width = 320,
            MarginTop = 28,
            MarginLeft = 20,
            Children =
            {
                new Border
                {
                    Width = 320,
                    Height = 44,
                    Background = "#FFFFFF",
                    BorderFill = SignalBlue,
                    BorderStroke = "1.5",
                    CornerRadius = "4",
                    Child = new Panel
                    {
                        Width = 320,
                        Height = 44,
                        Children =
                        {
                            // Clickable country selector (globe + dial code + chevron)
                            new Button
                            {
                                Width = 96,
                                Height = 40,
                                MarginLeft = 2,
                                MarginTop = 2,
                                Background = null,
                                BorderFill = null,
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _viewModel.ToggleCountryPickerCommand.Execute(null)
                                    },
                                },
                                Bindings =
                                {
                                    {
                                        nameof(Button.IsEnabled),
                                        nameof(MainViewModel.IsRegisterBusy),
                                        null,
                                        BindingMode.OneWay,
                                        a => !(bool)a!,
                                        null
                                    },
                                },
                                Content = new Panel
                                {
                                    Width = 96,
                                    Height = 40,
                                    Children =
                                    {
                                        new Ellipse
                                        {
                                            Width = 16,
                                            Height = 16,
                                            MarginLeft = 10,
                                            MarginTop = 12,
                                            Fill = null,
                                            StrokeFill = "#6B7280",
                                            IsAntiAlias = true,
                                        },
                                        new Ellipse
                                        {
                                            Width = 6,
                                            Height = 6,
                                            MarginLeft = 15,
                                            MarginTop = 17,
                                            Fill = "#6B7280",
                                            StrokeFill = null,
                                            IsAntiAlias = true,
                                        },
                                        new TextBlock
                                        {
                                            FontSize = 14,
                                            Foreground = "#1B1B1B",
                                            MarginLeft = 32,
                                            MarginTop = 10,
                                            Bindings =
                                            {
                                                {
                                                    nameof(TextBlock.Text),
                                                    nameof(MainViewModel.SelectedCountryLabel)
                                                },
                                            },
                                        },
                                        new TextBlock
                                        {
                                            Text = "▾",
                                            FontSize = 10,
                                            Foreground = "#6B7280",
                                            MarginLeft = 78,
                                            MarginTop = 11,
                                        },
                                    },
                                },
                            },
                            new Border
                            {
                                Width = 1,
                                Height = 22,
                                MarginLeft = 100,
                                MarginTop = 11,
                                Background = "#D1D5DB",
                            },
                            new TextBox
                            {
                                Width = 200,
                                Height = 28,
                                MarginLeft = 110,
                                MarginTop = 8,
                                Background = null,
                                BorderFill = null,
                                Bindings =
                                {
                                    {
                                        nameof(TextBox.Text),
                                        nameof(MainViewModel.RegisterNationalNumber),
                                        null,
                                        BindingMode.TwoWay
                                    },
                                    {
                                        nameof(TextBox.IsEnabled),
                                        nameof(MainViewModel.IsRegisterBusy),
                                        null,
                                        BindingMode.OneWay,
                                        a => !(bool)a!,
                                        null
                                    },
                                },
                            },
                            new TextBlock
                            {
                                Text = "Phone Number",
                                FontSize = 14,
                                Foreground = "#9CA3AF",
                                MarginLeft = 114,
                                MarginTop = 12,
                                Bindings =
                                {
                                    {
                                        nameof(Visibility),
                                        nameof(MainViewModel.RegisterNationalNumber),
                                        null,
                                        BindingMode.OneWay,
                                        a => string.IsNullOrEmpty(a as string)
                                            ? Visibility.Visible
                                            : Visibility.Collapsed,
                                        null
                                    },
                                },
                            },
                        },
                    },
                },
                // Country dropdown
                new Border
                {
                    Width = 320,
                    Height = 220,
                    MarginTop = 4,
                    Background = "#FFFFFF",
                    BorderFill = "#D1D5DB",
                    BorderStroke = "1",
                    CornerRadius = "4",
                    ZIndex = 20,
                    Bindings =
                    {
                        {
                            nameof(Visibility),
                            nameof(MainViewModel.IsCountryPickerOpen),
                            null,
                            BindingMode.OneWay,
                            BoolToVisibility,
                            null
                        },
                    },
                    Child = new ListBox
                    {
                        Width = "100%",
                        Height = "100%",
                        Background = "#FFFFFF",
                        ItemTemplate = typeof(CountryDialListItem),
                        Bindings =
                        {
                            { nameof(ListBox.Items), nameof(MainViewModel.Countries) },
                            {
                                nameof(ListBox.SelectedValue),
                                nameof(MainViewModel.SelectedCountry),
                                null,
                                BindingMode.TwoWay
                            },
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildSendSmsButton()
    {
        return new Button
        {
            Content = "Send SMS",
            Width = 320,
            Height = 44,
            MarginTop = 18,
            MarginLeft = 20,
            CornerRadius = "4",
            Foreground = "#FFFFFF",
            BorderFill = null,
            Commands =
            {
                {
                    nameof(Button.Click),
                    (s, e) => _ = _viewModel.SendSmsRegistrationCommand.ExecuteAsync(null)
                },
            },
            Bindings =
            {
                {
                    nameof(Button.IsEnabled),
                    nameof(MainViewModel.CanSendRegistrationCode),
                    null,
                    BindingMode.OneWay
                },
                {
                    nameof(Button.Background),
                    nameof(MainViewModel.CanSendRegistrationCode),
                    null,
                    BindingMode.OneWay,
                    a => (bool)a! ? SignalBlue : SendSmsDisabledBg,
                    null
                },
            },
        };
    }

    private UIElement BuildRegisterFollowUpSteps()
    {
        return new StackPanel
        {
            Width = 320,
            MarginTop = 20,
            MarginLeft = 20,
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingRegisterVerifyStep),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                new StackPanel
                {
                    Bindings =
                    {
                        {
                            nameof(Visibility),
                            nameof(MainViewModel.IsShowingRegisterCaptchaStep),
                            null,
                            BindingMode.OneWay,
                            BoolToVisibility,
                            null
                        },
                    },
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "Captcha required",
                            Width = 320,
                            FontSize = 13,
                            Foreground = "#374151",
                            TextAlignment = TextAlignment.Center,
                        },
                        new TextBlock
                        {
                            Width = 320,
                            FontSize = 11,
                            Foreground = SignalBlue,
                            MarginTop = 4,
                            TextAlignment = TextAlignment.Center,
                            Bindings =
                            {
                                {
                                    nameof(TextBlock.Text),
                                    nameof(MainViewModel.RegisterChallengeUrl),
                                    null,
                                    BindingMode.OneWay,
                                    a => string.IsNullOrWhiteSpace(a as string)
                                        ? "Open captcha page, then paste token below"
                                        : (string)a!,
                                    null
                                },
                            },
                        },
                        new TextBox
                        {
                            Width = 320,
                            Height = 36,
                            MarginTop = 8,
                            Bindings =
                            {
                                {
                                    nameof(TextBox.Text),
                                    nameof(MainViewModel.RegisterCaptchaToken),
                                    null,
                                    BindingMode.TwoWay
                                },
                            },
                        },
                        new Button
                        {
                            Content = "Submit Captcha",
                            Width = 320,
                            Height = 40,
                            MarginTop = 8,
                            Background = SignalBlue,
                            Foreground = "#FFFFFF",
                            BorderFill = null,
                            CornerRadius = "4",
                            Commands =
                            {
                                {
                                    nameof(Button.Click),
                                    (s, e) => _ = _viewModel.SubmitRegistrationCaptchaCommand.ExecuteAsync(null)
                                },
                            },
                            Bindings =
                            {
                                {
                                    nameof(Button.IsEnabled),
                                    nameof(MainViewModel.IsRegisterBusy),
                                    null,
                                    BindingMode.OneWay,
                                    a => !(bool)a!,
                                    null
                                },
                            },
                        },
                    },
                },
                new TextBlock
                {
                    Text = "Verification code",
                    Width = 320,
                    FontSize = 13,
                    Foreground = "#374151",
                    MarginTop = 16,
                    TextAlignment = TextAlignment.Center,
                },
                new TextBox
                {
                    Width = 320,
                    Height = 40,
                    MarginTop = 8,
                    Bindings =
                    {
                        {
                            nameof(TextBox.Text),
                            nameof(MainViewModel.RegisterVerificationCode),
                            null,
                            BindingMode.TwoWay
                        },
                    },
                },
                new Button
                {
                    Content = "Continue",
                    Width = 320,
                    Height = 44,
                    MarginTop = 12,
                    CornerRadius = "4",
                    Foreground = "#FFFFFF",
                    BorderFill = null,
                    Commands =
                    {
                        {
                            nameof(Button.Click),
                            (s, e) => _ = _viewModel.CompletePhoneRegistrationCommand.ExecuteAsync(null)
                        },
                    },
                    Bindings =
                    {
                        {
                            nameof(Button.IsEnabled),
                            nameof(MainViewModel.CanCompleteRegistration),
                            null,
                            BindingMode.OneWay
                        },
                        {
                            nameof(Button.Background),
                            nameof(MainViewModel.CanCompleteRegistration),
                            null,
                            BindingMode.OneWay,
                            a => (bool)a! ? SignalBlue : SendSmsDisabledBg,
                            null
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildQrColumn()
    {
        return new Border
        {
            Width = 256,
            Height = 256,
            Margin = "8,8,38,8",
            Background = "#FFFFFF",
            [Grid.ColumnIndex] = 0,
            Child = new Panel
            {
                Width = 256,
                Height = 256,
                Children =
                {
                    new ProgressBar
                    {
                        IsIndeterminate = true,
                        Width = 120,
                        Height = 4,
                        MarginTop = 126,
                        MarginLeft = 68,
                        Bindings =
                        {
                            {
                                nameof(Visibility),
                                nameof(MainViewModel.IsQrLoading),
                                null,
                                BindingMode.OneWay,
                                BoolToVisibility,
                                null
                            },
                        },
                    },
                    new Panel
                    {
                        Width = 256,
                        Height = 256,
                        Bindings =
                        {
                            {
                                nameof(Visibility),
                                nameof(MainViewModel.IsShowingQrCard),
                                null,
                                BindingMode.OneWay,
                                BoolToVisibility,
                                null
                            },
                        },
                        Children =
                        {
                            new Picture
                            {
                                Width = 256,
                                Height = 256,
                                Stretch = Stretch.Uniform,
                                Bindings =
                                {
                                    { nameof(Picture.Source), nameof(MainViewModel.QrCodeImage) },
                                },
                            },
                            new Border
                            {
                                Width = 72,
                                Height = 72,
                                CornerRadius = "36",
                                Background = "#FFFFFF",
                                MarginLeft = 92,
                                MarginTop = 92,
                                Child = new SignalBubbleIcon
                                {
                                    MarginLeft = 12,
                                    MarginTop = 12,
                                },
                                Bindings =
                                {
                                    {
                                        nameof(Visibility),
                                        nameof(MainViewModel.QrCodeImage),
                                        null,
                                        BindingMode.OneWay,
                                        a => a is null ? Visibility.Collapsed : Visibility.Visible,
                                        null
                                    },
                                },
                            },
                        },
                    },
                    new StackPanel
                    {
                        MarginTop = 100,
                        MarginLeft = 40,
                        Bindings =
                        {
                            {
                                nameof(Visibility),
                                nameof(MainViewModel.IsLinkInProgress),
                                null,
                                BindingMode.OneWay,
                                BoolToVisibility,
                                null
                            },
                        },
                        Children =
                        {
                            new ProgressBar
                            {
                                IsIndeterminate = true,
                                Width = 140,
                                Height = 4,
                            },
                            new TextBlock
                            {
                                Text = "正在关联设备…",
                                Foreground = "#4B5563",
                                MarginTop = 12,
                            },
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildInstructionsColumn()
    {
        return new StackPanel
        {
            [Grid.ColumnIndex] = 1,
            Margin = "0,8,8,8",
            MaxWidth = 420,
            Children =
            {
                new TextBlock
                {
                    Text = "扫描您手机上的 Signal 应用中的此二维码",
                    FontSize = 22,
                    FontStyle = FontStyles.Bold,
                    Foreground = "#000000",
                },
                new TextBlock
                {
                    Text = "1. 在您的手机上打开 Signal",
                    FontSize = 15,
                    Foreground = "#000000",
                    MarginTop = 16,
                },
                new TextBlock
                {
                    Text = "2. 点击设置，然后点击已关联的设备",
                    FontSize = 15,
                    Foreground = "#000000",
                    MarginTop = 6,
                },
                new TextBlock
                {
                    Text = "3. 点击链接新设备",
                    FontSize = 15,
                    Foreground = "#000000",
                    MarginTop = 6,
                },
                new TextBlock
                {
                    Text = "SELF-HOSTED / CONFIGURABLE SERVER",
                    FontSize = 12,
                    Foreground = "#6B7280",
                    MarginTop = 16,
                },
                new Panel
                {
                    MarginTop = 12,
                    Height = 32,
                    Children =
                    {
                        new TextBox
                        {
                            Width = 180,
                            Height = 28,
                            Bindings =
                            {
                                {
                                    nameof(TextBox.Text),
                                    nameof(MainViewModel.DeviceName),
                                    null,
                                    BindingMode.TwoWay
                                },
                                {
                                    nameof(TextBox.IsEnabled),
                                    nameof(MainViewModel.IsLinkInProgress),
                                    null,
                                    BindingMode.OneWay,
                                    a => !(bool)a!,
                                    null
                                },
                            },
                        },
                        new Button
                        {
                            Content = "重新生成",
                            Width = 88,
                            Height = 28,
                            MarginLeft = 188,
                            Commands =
                            {
                                {
                                    nameof(Button.Click),
                                    (s, e) => _ = _viewModel.StartProvisioningCommand.ExecuteAsync(null)
                                },
                            },
                            Bindings =
                            {
                                {
                                    nameof(Button.IsEnabled),
                                    nameof(MainViewModel.IsLinkInProgress),
                                    null,
                                    BindingMode.OneWay,
                                    a => !(bool)a!,
                                    null
                                },
                            },
                        },
                    },
                },
                new TextBlock
                {
                    FontSize = 12,
                    Foreground = "#6B7280",
                    MarginTop = 10,
                    Bindings =
                    {
                        { nameof(TextBlock.Text), nameof(MainViewModel.StatusText) },
                    },
                },
            },
        };
    }

    private UIElement BuildChatScreen()
    {
        return new DockPanel
        {
            Width = "100%",
            Height = "100%",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingChat),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                new Border
                {
                    [DockPanel.Dock] = Dock.Top,
                    Padding = "12,8,12,8",
                    Background = "#0F172A",
                    Height = 48,
                    Child = new Grid
                    {
                        ColumnDefinitions =
                        {
                            new ColumnDefinition { Width = "1*" },
                            new ColumnDefinition { Width = "auto" },
                        },
                        Children =
                        {
                            new TextBlock
                            {
                                Foreground = "#F8FAFC",
                                MarginTop = 8,
                                Bindings =
                                {
                                    { nameof(TextBlock.Text), nameof(MainViewModel.StatusText) },
                                },
                            },
                            new StackPanel
                            {
                                [Grid.ColumnIndex] = 1,
                                Orientation = Orientation.Horizontal,
                                Children =
                                {
                                    new Button
                                    {
                                        Content = "刷新",
                                        Width = 64,
                                        Height = 28,
                                        MarginTop = 4,
                                        Commands =
                                        {
                                            {
                                                nameof(Button.Click),
                                                (s, e) => _ = _viewModel.RefreshConversationsCommand.ExecuteAsync(null)
                                            },
                                        },
                                    },
                                    new Button
                                    {
                                        Content = "附件",
                                        Width = 64,
                                        Height = 28,
                                        MarginLeft = 6,
                                        MarginTop = 4,
                                        Commands =
                                        {
                                            {
                                                nameof(Button.Click),
                                                (s, e) => _ = _viewModel.AttachFileCommand.ExecuteAsync(null)
                                            },
                                        },
                                    },
                                    new Button
                                    {
                                        Content = "设置",
                                        Width = 64,
                                        Height = 28,
                                        MarginLeft = 6,
                                        MarginTop = 4,
                                        Commands =
                                        {
                                            {
                                                nameof(Button.Click),
                                                (s, e) => _viewModel.ToggleSettingsCommand.Execute(null)
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                },
                BuildSettingsPanel(),
                BuildChatMainPanel(),
            },
        };
    }

    private UIElement BuildSettingsPanel()
    {
        return new Border
        {
            Background = "#FFFFFF",
            Padding = "24",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingSettings),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Child = new StackPanel
            {
                Children =
                {
                    new TextBlock
                    {
                        Text = "设置",
                        FontSize = 20,
                        FontStyle = FontStyles.Bold,
                        Foreground = "#0F172A",
                    },
                    new TextBlock
                    {
                        MarginTop = 12,
                        FontSize = 13,
                        Foreground = "#334155",
                        Bindings =
                        {
                            { nameof(TextBlock.Text), nameof(MainViewModel.SettingsSummary) },
                        },
                    },
                    new CheckBox
                    {
                        Content = "启用桌面通知提示",
                        MarginTop = 16,
                        Bindings =
                        {
                            {
                                nameof(CheckBox.IsChecked),
                                nameof(MainViewModel.NotificationsEnabled),
                                null,
                                BindingMode.TwoWay
                            },
                        },
                    },
                    new TextBlock
                    {
                        Text = "添加联系人（ServiceId / ACI）",
                        MarginTop = 20,
                        FontSize = 14,
                        FontStyle = FontStyles.Bold,
                    },
                    new TextBox
                    {
                        MarginTop = 8,
                        Height = 28,
                        Bindings =
                        {
                            {
                                nameof(TextBox.Text),
                                nameof(MainViewModel.NewContactServiceId),
                                null,
                                BindingMode.TwoWay
                            },
                        },
                    },
                    new TextBox
                    {
                        MarginTop = 8,
                        Height = 28,
                        Bindings =
                        {
                            {
                                nameof(TextBox.Text),
                                nameof(MainViewModel.NewContactName),
                                null,
                                BindingMode.TwoWay
                            },
                        },
                    },
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        MarginTop = 12,
                        Children =
                        {
                            new Button
                            {
                                Content = "添加联系人",
                                Width = 100,
                                Height = 28,
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _ = _viewModel.AddContactCommand.ExecuteAsync(null)
                                    },
                                },
                            },
                            new Button
                            {
                                Content = "保存设置",
                                Width = 100,
                                Height = 28,
                                MarginLeft = 8,
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _ = _viewModel.SaveSettingsCommand.ExecuteAsync(null)
                                    },
                                },
                            },
                            new Button
                            {
                                Content = "返回聊天",
                                Width = 100,
                                Height = 28,
                                MarginLeft = 8,
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _viewModel.ToggleSettingsCommand.Execute(null)
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildChatMainPanel()
    {
        return new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = "280" },
                new ColumnDefinition { Width = "1*" },
            },
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingChatMain),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                new Border
                {
                    Background = "#F8FAFC",
                    BorderFill = "#CBD5E1",
                    BorderStroke = "0,0,1,0",
                    [Grid.ColumnIndex] = 0,
                    Child = new ListBox
                    {
                        Background = "Transparent",
                        ItemTemplate = typeof(ConversationListItem),
                        Bindings =
                        {
                            { nameof(ListBox.Items), nameof(MainViewModel.Conversations) },
                            {
                                nameof(ListBox.SelectedValue),
                                nameof(MainViewModel.SelectedConversation),
                                null,
                                BindingMode.TwoWay
                            },
                        },
                    },
                },
                new DockPanel
                {
                    [Grid.ColumnIndex] = 1,
                    Children =
                    {
                        new Border
                        {
                            [DockPanel.Dock] = Dock.Bottom,
                            Padding = "8",
                            Background = "#E2E8F0",
                            Height = 48,
                            Child = new Grid
                            {
                                ColumnDefinitions =
                                {
                                    new ColumnDefinition { Width = "1*" },
                                    new ColumnDefinition { Width = "auto" },
                                },
                                Children =
                                {
                                    new TextBox
                                    {
                                        Height = 28,
                                        MarginTop = 2,
                                        Bindings =
                                        {
                                            {
                                                nameof(TextBox.Text),
                                                nameof(MainViewModel.ComposeText),
                                                null,
                                                BindingMode.TwoWay
                                            },
                                        },
                                    },
                                    new Button
                                    {
                                        Content = "发送",
                                        [Grid.ColumnIndex] = 1,
                                        Width = 72,
                                        Height = 28,
                                        MarginLeft = 8,
                                        MarginTop = 2,
                                        Commands =
                                        {
                                            {
                                                nameof(Button.Click),
                                                (s, e) => _ = _viewModel.SendCommand.ExecuteAsync(null)
                                            },
                                        },
                                    },
                                },
                            },
                        },
                        new ListBox
                        {
                            Background = "#FFFFFF",
                            ItemTemplate = typeof(MessageListItem),
                            Bindings =
                            {
                                { nameof(ListBox.Items), nameof(MainViewModel.Messages) },
                            },
                        },
                    },
                },
            },
        };
    }

    private static object BoolToVisibility(object? value) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
}
