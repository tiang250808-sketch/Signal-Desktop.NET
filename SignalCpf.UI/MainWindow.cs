using CPF;
using CPF.Controls;
using CPF.Drawing;
using CPF.Platform;
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
        Width = 1100;
        Height = 720;
        MinWidth = 900;
        MinHeight = 560;
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
            try
            {
                await _viewModel.ShutdownAsync();
            }
            catch
            {
                // Best-effort cleanup; still exit the process.
            }
            finally
            {
                // CPF does not always leave Run() when the last window closes.
                Application.Exit();
            }
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
        return new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Height = 32,
            Children =
            {
                new Button
                {
                    Content = "关联设备",
                    Width = 128,
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
                    Width = 128,
                    Height = 30,
                    MarginLeft = 8,
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
                                        new TextBlock
                                        {
                                            FontSize = 14,
                                            Foreground = "#1B1B1B",
                                            MarginLeft = 12,
                                            MarginTop = 10,
                                            Bindings =
                                            {
                                                {
                                                    nameof(TextBlock.Text),
                                                    nameof(MainViewModel.SelectedCountryLabel)
                                                },
                                            },
                                        },
                                        BuildCountryChevron(),
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
                            // Placeholder behind the TextBox; must not intercept clicks.
                            new TextBlock
                            {
                                Text = "Phone Number",
                                FontSize = 14,
                                Foreground = "#9CA3AF",
                                MarginLeft = 114,
                                MarginTop = 13,
                                IsHitTestVisible = false,
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
                            new TextBox
                            {
                                Width = 208,
                                Height = 44,
                                MarginLeft = 108,
                                MarginTop = 0,
                                Background = null,
                                BorderFill = null,
                                // Vertically center 14px text inside the 44px field.
                                Padding = "4,12,4,12",
                                FontSize = 14,
                                AcceptsReturn = false,
                                MaxLength = 15,
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

    /// <summary>Solid chevron sized to match the 14px country-code text.</summary>
    private static UIElement BuildCountryChevron()
    {
        var chevron = new Polygon
        {
            Width = 18,
            Height = 16,
            MarginLeft = 70,
            MarginTop = 12,
            Fill = "#4B5563",
            StrokeFill = null,
            IsAntiAlias = true,
        };
        // Full-height triangle so visual size matches "+N" glyphs.
        chevron.Points.Add(new Point(0, 1));
        chevron.Points.Add(new Point(18, 1));
        chevron.Points.Add(new Point(9, 15));
        return chevron;
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
                            Text = "需要完成 Captcha 后才会发送短信",
                            Width = 320,
                            FontSize = 13,
                            Foreground = "#374151",
                            TextAlignment = TextAlignment.Center,
                        },
                        new TextBlock
                        {
                            Text = "浏览器完成验证后，右键「Open Signal」复制链接，粘贴到下方并提交",
                            Width = 320,
                            FontSize = 12,
                            Foreground = "#6B7280",
                            MarginTop = 6,
                            TextAlignment = TextAlignment.Center,
                        },
                        new Button
                        {
                            Content = "在浏览器中打开 Captcha",
                            Width = 320,
                            Height = 36,
                            MarginTop = 10,
                            Background = SignalBlue,
                            Foreground = "#FFFFFF",
                            BorderFill = null,
                            CornerRadius = "4",
                            Commands =
                            {
                                {
                                    nameof(Button.Click),
                                    (s, e) => _viewModel.OpenCaptchaPageCommand.Execute(null)
                                },
                            },
                        },
                        new TextBox
                        {
                            Width = 320,
                            Height = 56,
                            MarginTop = 10,
                            AcceptsReturn = true,
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
                            Content = "提交 Captcha（提交后发送短信）",
                            Width = 320,
                            Height = 36,
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
                    Text = "验证码",
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
                    AcceptsReturn = false,
                    MaxLength = 10,
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
                    Content = "重新发送短信",
                    Width = 320,
                    Height = 36,
                    MarginTop = 8,
                    CornerRadius = "4",
                    Background = "#E5E7EB",
                    Foreground = "#1B1B1B",
                    BorderFill = null,
                    Commands =
                    {
                        {
                            nameof(Button.Click),
                            (s, e) => _ = _viewModel.RequestRegistrationCodeCommand.ExecuteAsync(null)
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
                        {
                            nameof(Visibility),
                            nameof(MainViewModel.RegisterCodeSent),
                            null,
                            BindingMode.OneWay,
                            BoolToVisibility,
                            null
                        },
                    },
                },
                new Button
                {
                    Content = "继续",
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
        // Prefer DockPanel over Grid: CPF Grid star columns often fail to stretch children to full height.
        return new Panel
        {
            Width = "100%",
            Height = "100%",
            Background = "#FFFFFF",
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
                new DockPanel
                {
                    Width = "100%",
                    Height = "100%",
                    Children =
                    {
                        BuildChatNavRail(),
                        new Panel
                        {
                            Width = "100%",
                            Height = "100%",
                            Children =
                            {
                                BuildChatsTab(),
                                BuildCallsTab(),
                                BuildStoriesTab(),
                            },
                        },
                    },
                },
                BuildSettingsPanel(),
            },
        };
    }

    private UIElement BuildChatsTab()
    {
        return new DockPanel
        {
            Width = "100%",
            Height = "100%",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingChatsTab),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                BuildChatListColumn(),
                BuildChatThreadColumn(),
            },
        };
    }

    private UIElement BuildChatNavRail()
    {
        return new Border
        {
            [DockPanel.Dock] = Dock.Left,
            Width = 68,
            Height = "100%",
            Background = "#F2F2F2",
            BorderFill = "#E0E0E0",
            BorderStroke = "0,0,1,0",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsNavRailVisible),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Child = new DockPanel
            {
                Width = "100%",
                Height = "100%",
                Children =
                {
                    new StackPanel
                    {
                        [DockPanel.Dock] = Dock.Bottom,
                        Width = "100%",
                        MarginBottom = 12,
                        Children =
                        {
                            MakeNavButton("⚙", () => _viewModel.ToggleSettingsCommand.Execute(null)),
                        },
                    },
                    new StackPanel
                    {
                        Width = "100%",
                        MarginTop = 10,
                        Children =
                        {
                            MakeNavButton("☰", () => _viewModel.ToggleNavRailCommand.Execute(null)),
                            MakeNavButton(
                                "💬",
                                () => _viewModel.ShowChatsTabCommand.Execute(null),
                                nameof(MainViewModel.IsChatsNavSelected),
                                marginTop: 8),
                            MakeNavButton(
                                "📞",
                                () => _viewModel.ShowCallsTabCommand.Execute(null),
                                nameof(MainViewModel.IsCallsNavSelected),
                                marginTop: 8),
                            MakeNavButton(
                                "▢",
                                () => _viewModel.ShowStoriesTabCommand.Execute(null),
                                nameof(MainViewModel.IsStoriesNavSelected),
                                marginTop: 8),
                        },
                    },
                },
            },
        };
    }

    private Button MakeNavButton(
        string glyph,
        Action onClick,
        string? selectedProperty = null,
        float marginTop = 0)
    {
        var button = new Button
        {
            Width = 48,
            Height = 48,
            MarginLeft = 10,
            MarginTop = marginTop,
            Background = "Transparent",
            BorderFill = "Transparent",
            CornerRadius = "12",
            Content = new TextBlock
            {
                Text = glyph,
                FontSize = 20,
                Foreground = "#3B3B3B",
                MarginLeft = 13,
                MarginTop = 10,
            },
            Commands =
            {
                { nameof(Button.Click), (s, e) => onClick() },
            },
        };

        if (selectedProperty is not null)
        {
            button.Bindings.Add(
                nameof(Button.Background),
                selectedProperty,
                null,
                BindingMode.OneWay,
                a => a is true ? "#E0E0E0" : "Transparent",
                null);
        }

        return button;
    }

    private UIElement BuildChatListColumn()
    {
        return new Border
        {
            [DockPanel.Dock] = Dock.Left,
            Width = 321,
            Height = "100%",
            Background = "#FFFFFF",
            BorderFill = "Transparent",
            Child = new DockPanel
            {
                Width = "100%",
                Height = "100%",
                Children =
                {
                    // Always-on splitter (independent of whether the message pane is shown).
                    new Border
                    {
                        [DockPanel.Dock] = Dock.Right,
                        Width = 1,
                        Height = "100%",
                        Background = "#E0E0E0",
                        BorderFill = "Transparent",
                    },
                    // Header: Chats + compose
                    new Border
                    {
                        [DockPanel.Dock] = Dock.Top,
                        Width = "100%",
                        Height = 56,
                        Child = new DockPanel
                        {
                            Width = "100%",
                            Height = "100%",
                            Children =
                            {
                                new StackPanel
                                {
                                    [DockPanel.Dock] = Dock.Right,
                                    Orientation = Orientation.Horizontal,
                                    MarginTop = 10,
                                    MarginRight = 8,
                                    Children =
                                    {
                                        new Button
                                        {
                                            Content = "✎",
                                            Width = 36,
                                            Height = 36,
                                            Background = "Transparent",
                                            BorderFill = "Transparent",
                                            Foreground = "#3B3B3B",
                                            Commands =
                                            {
                                                {
                                                    nameof(Button.Click),
                                                    (s, e) => _viewModel.ToggleNewChatCommand.Execute(null)
                                                },
                                            },
                                        },
                                        new Button
                                        {
                                            Content = "⋮",
                                            Width = 36,
                                            Height = 36,
                                            Background = "Transparent",
                                            BorderFill = "Transparent",
                                            Foreground = "#3B3B3B",
                                            Commands =
                                            {
                                                {
                                                    nameof(Button.Click),
                                                    (s, e) => _ = _viewModel.RefreshConversationsCommand.ExecuteAsync(null)
                                                },
                                            },
                                        },
                                    },
                                },
                                // Shown when nav rail is collapsed — reopen the left menu.
                                new Button
                                {
                                    [DockPanel.Dock] = Dock.Left,
                                    Content = "☰",
                                    Width = 36,
                                    Height = 36,
                                    MarginLeft = 8,
                                    MarginTop = 10,
                                    Background = "Transparent",
                                    BorderFill = "Transparent",
                                    Foreground = "#3B3B3B",
                                    Commands =
                                    {
                                        {
                                            nameof(Button.Click),
                                            (s, e) => _viewModel.ToggleNavRailCommand.Execute(null)
                                        },
                                    },
                                    Bindings =
                                    {
                                        {
                                            nameof(Visibility),
                                            nameof(MainViewModel.IsNavRailCollapsed),
                                            null,
                                            BindingMode.OneWay,
                                            BoolToVisibility,
                                            null
                                        },
                                    },
                                },
                                new TextBlock
                                {
                                    Text = "Chats",
                                    FontSize = 24,
                                    FontStyle = FontStyles.Bold,
                                    Foreground = "#1B1B1B",
                                    MarginLeft = 12,
                                    MarginTop = 12,
                                },
                            },
                        },
                    },
                    // Search
                    new Border
                    {
                        [DockPanel.Dock] = Dock.Top,
                        Width = "100%",
                        Height = 46,
                        Padding = "12,0,12,10",
                        Child = new Border
                        {
                            Width = "100%",
                            Height = 36,
                            Background = "#F0F0F0",
                            CornerRadius = "18",
                            Child = new DockPanel
                            {
                                Width = "100%",
                                Height = "100%",
                                Children =
                                {
                                    new TextBlock
                                    {
                                        [DockPanel.Dock] = Dock.Left,
                                        Text = "🔍",
                                        FontSize = 13,
                                        MarginLeft = 12,
                                        MarginTop = 8,
                                        IsHitTestVisible = false,
                                    },
                                    new TextBox
                                    {
                                        Width = "100%",
                                        Height = "100%",
                                        MarginLeft = 4,
                                        MarginRight = 12,
                                        Background = "Transparent",
                                        BorderFill = "Transparent",
                                        Padding = "0,8,0,8",
                                        FontSize = 14,
                                        Bindings =
                                        {
                                            {
                                                nameof(TextBox.Text),
                                                nameof(MainViewModel.ConversationFilter),
                                                null,
                                                BindingMode.TwoWay
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                    // Conversation list OR Signal-style New chat flow.
                    new Panel
                    {
                        Width = "100%",
                        Height = "100%",
                        Children =
                        {
                            BuildNewChatPanel(),
                            new ListBox
                            {
                                Width = "100%",
                                Height = "100%",
                                Background = "#FFFFFF",
                                ItemTemplate = typeof(ConversationListItem),
                                Bindings =
                                {
                                    { nameof(ListBox.Items), nameof(MainViewModel.FilteredConversations) },
                                    {
                                        nameof(ListBox.SelectedValue),
                                        nameof(MainViewModel.SelectedConversation),
                                        null,
                                        BindingMode.TwoWay
                                    },
                                    {
                                        nameof(Visibility),
                                        nameof(MainViewModel.IsShowingConversationList),
                                        null,
                                        BindingMode.OneWay,
                                        BoolToVisibility,
                                        null
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildNewChatPanel()
    {
        return new Border
        {
            Width = "100%",
            Height = "100%",
            Background = "#FFFFFF",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingNewChatPanel),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Child = new DockPanel
            {
                Width = "100%",
                Height = "100%",
                Children =
                {
                    // Title bar: back + title
                    new Border
                    {
                        [DockPanel.Dock] = Dock.Top,
                        Width = "100%",
                        Height = 52,
                        Child = new DockPanel
                        {
                            Width = "100%",
                            Height = "100%",
                            Children =
                            {
                                new Button
                                {
                                    [DockPanel.Dock] = Dock.Left,
                                    Content = "←",
                                    Width = 40,
                                    Height = 36,
                                    MarginLeft = 8,
                                    MarginTop = 8,
                                    Background = "Transparent",
                                    BorderFill = "Transparent",
                                    Foreground = "#3B3B3B",
                                    Commands =
                                    {
                                        {
                                            nameof(Button.Click),
                                            (s, e) => _viewModel.NewChatBackCommand.Execute(null)
                                        },
                                    },
                                },
                                new TextBlock
                                {
                                    FontSize = 18,
                                    FontStyle = FontStyles.Bold,
                                    Foreground = "#1B1B1B",
                                    MarginLeft = 4,
                                    MarginTop = 14,
                                    Bindings =
                                    {
                                        { nameof(TextBlock.Text), nameof(MainViewModel.NewChatTitle) },
                                    },
                                },
                            },
                        },
                    },
                    // Home: create group / find username / find phone
                    new StackPanel
                    {
                        Width = "100%",
                        MarginTop = 8,
                        Bindings =
                        {
                            {
                                nameof(Visibility),
                                nameof(MainViewModel.IsNewChatHome),
                                null,
                                BindingMode.OneWay,
                                BoolToVisibility,
                                null
                            },
                        },
                        Children =
                        {
                            MakeComposeActionRow(
                                "👥",
                                "Create a group",
                                "Start a group chat with multiple people",
                                () => _viewModel.OpenCreateGroupStepCommand.Execute(null)),
                            MakeComposeActionRow(
                                "@",
                                "Find by username",
                                "Search for someone by their Signal username",
                                () => _viewModel.OpenFindUsernameStepCommand.Execute(null)),
                            MakeComposeActionRow(
                                "📱",
                                "Find by phone number",
                                "Enter a phone number to start a chat",
                                () => _viewModel.OpenFindPhoneStepCommand.Execute(null)),
                            new TextBlock
                            {
                                Text = "Contacts",
                                FontSize = 13,
                                FontStyle = FontStyles.Bold,
                                Foreground = "#6B6B6B",
                                MarginLeft = 16,
                                MarginTop = 16,
                                MarginBottom = 4,
                            },
                            new ListBox
                            {
                                Width = "100%",
                                Height = 280,
                                Background = "#FFFFFF",
                                ItemTemplate = typeof(ContactPickListItem),
                                Bindings =
                                {
                                    { nameof(ListBox.Items), nameof(MainViewModel.Contacts) },
                                    {
                                        nameof(ListBox.SelectedValue),
                                        nameof(MainViewModel.SelectedComposeContact),
                                        null,
                                        BindingMode.TwoWay
                                    },
                                },
                            },
                            new TextBlock
                            {
                                Text = "Or paste an ACI UUID below",
                                FontSize = 11,
                                Foreground = "#8E8E8E",
                                MarginLeft = 16,
                                MarginTop = 8,
                            },
                            new TextBox
                            {
                                Width = "100%",
                                Height = 34,
                                MarginLeft = 12,
                                MarginRight = 12,
                                MarginTop = 6,
                                Background = "#F0F0F0",
                                BorderFill = "Transparent",
                                CornerRadius = "8",
                                Padding = "8,7,8,7",
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
                            new Button
                            {
                                Content = "Start chat with ACI",
                                Width = 180,
                                Height = 34,
                                MarginLeft = 12,
                                MarginTop = 8,
                                Background = SignalBlue,
                                Foreground = "#FFFFFF",
                                BorderFill = "Transparent",
                                CornerRadius = "8",
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _ = _viewModel.AddContactCommand.ExecuteAsync(null)
                                    },
                                },
                            },
                        },
                    },
                    // Find by username
                    new StackPanel
                    {
                        Width = "100%",
                        MarginTop = 12,
                        MarginLeft = 16,
                        MarginRight = 16,
                        Bindings =
                        {
                            {
                                nameof(Visibility),
                                nameof(MainViewModel.IsNewChatFindUsername),
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
                                Text = "Enter a username",
                                FontSize = 14,
                                Foreground = "#1B1B1B",
                            },
                            new TextBox
                            {
                                Width = "100%",
                                Height = 40,
                                MarginTop = 10,
                                Background = "#F0F0F0",
                                BorderFill = "Transparent",
                                CornerRadius = "10",
                                Padding = "12,10,12,10",
                                FontSize = 15,
                                Bindings =
                                {
                                    {
                                        nameof(TextBox.Text),
                                        nameof(MainViewModel.FindUsernameQuery),
                                        null,
                                        BindingMode.TwoWay
                                    },
                                },
                            },
                            new TextBlock
                            {
                                Text = "Usernames look like @nickname.xx",
                                FontSize = 12,
                                Foreground = "#8E8E8E",
                                MarginTop = 8,
                            },
                            new Button
                            {
                                Content = "Next",
                                Width = "100%",
                                Height = 40,
                                MarginTop = 16,
                                Background = SignalBlue,
                                Foreground = "#FFFFFF",
                                BorderFill = "Transparent",
                                CornerRadius = "10",
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _ = _viewModel.StartChatByUsernameCommand.ExecuteAsync(null)
                                    },
                                },
                            },
                        },
                    },
                    // Find by phone
                    new StackPanel
                    {
                        Width = "100%",
                        MarginTop = 12,
                        MarginLeft = 16,
                        MarginRight = 16,
                        Bindings =
                        {
                            {
                                nameof(Visibility),
                                nameof(MainViewModel.IsNewChatFindPhone),
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
                                Text = "Enter a phone number",
                                FontSize = 14,
                                Foreground = "#1B1B1B",
                            },
                            new TextBox
                            {
                                Width = "100%",
                                Height = 40,
                                MarginTop = 10,
                                Background = "#F0F0F0",
                                BorderFill = "Transparent",
                                CornerRadius = "10",
                                Padding = "12,10,12,10",
                                FontSize = 15,
                                Bindings =
                                {
                                    {
                                        nameof(TextBox.Text),
                                        nameof(MainViewModel.FindPhoneQuery),
                                        null,
                                        BindingMode.TwoWay
                                    },
                                },
                            },
                            new TextBlock
                            {
                                Text = "Include country code, e.g. +8613812345678",
                                FontSize = 12,
                                Foreground = "#8E8E8E",
                                MarginTop = 8,
                            },
                            new Button
                            {
                                Content = "Next",
                                Width = "100%",
                                Height = 40,
                                MarginTop = 16,
                                Background = SignalBlue,
                                Foreground = "#FFFFFF",
                                BorderFill = "Transparent",
                                CornerRadius = "10",
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _ = _viewModel.StartChatByPhoneCommand.ExecuteAsync(null)
                                    },
                                },
                            },
                        },
                    },
                    // Create group
                    new StackPanel
                    {
                        Width = "100%",
                        MarginTop = 12,
                        MarginLeft = 16,
                        MarginRight = 16,
                        Bindings =
                        {
                            {
                                nameof(Visibility),
                                nameof(MainViewModel.IsNewChatCreateGroup),
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
                                Text = "Group name",
                                FontSize = 14,
                                Foreground = "#1B1B1B",
                            },
                            new TextBox
                            {
                                Width = "100%",
                                Height = 40,
                                MarginTop = 8,
                                Background = "#F0F0F0",
                                BorderFill = "Transparent",
                                CornerRadius = "10",
                                Padding = "12,10,12,10",
                                FontSize = 15,
                                Bindings =
                                {
                                    {
                                        nameof(TextBox.Text),
                                        nameof(MainViewModel.NewGroupName),
                                        null,
                                        BindingMode.TwoWay
                                    },
                                },
                            },
                            new TextBlock
                            {
                                Text = "Members (ACI / phone, comma-separated)",
                                FontSize = 14,
                                Foreground = "#1B1B1B",
                                MarginTop = 14,
                            },
                            new TextBox
                            {
                                Width = "100%",
                                Height = 80,
                                MarginTop = 8,
                                Background = "#F0F0F0",
                                BorderFill = "Transparent",
                                CornerRadius = "10",
                                Padding = "12,10,12,10",
                                FontSize = 14,
                                AcceptsReturn = true,
                                Bindings =
                                {
                                    {
                                        nameof(TextBox.Text),
                                        nameof(MainViewModel.NewGroupMembersText),
                                        null,
                                        BindingMode.TwoWay
                                    },
                                },
                            },
                            new TextBlock
                            {
                                Text = "You can add members later. Group messaging sync is limited in this build.",
                                FontSize = 12,
                                Foreground = "#8E8E8E",
                                MarginTop = 8,
                            },
                            new Button
                            {
                                Content = "Create",
                                Width = "100%",
                                Height = 40,
                                MarginTop = 16,
                                Background = SignalBlue,
                                Foreground = "#FFFFFF",
                                BorderFill = "Transparent",
                                CornerRadius = "10",
                                Commands =
                                {
                                    {
                                        nameof(Button.Click),
                                        (s, e) => _ = _viewModel.CreateGroupCommand.ExecuteAsync(null)
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private Button MakeComposeActionRow(string glyph, string title, string subtitle, Action onClick) =>
        new()
        {
            Width = "100%",
            Height = 64,
            MarginLeft = 8,
            MarginRight = 8,
            MarginTop = 4,
            Background = "Transparent",
            BorderFill = "Transparent",
            CornerRadius = "10",
            Content = new Panel
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
                        MarginTop = 12,
                        Background = "#E8EEF9",
                        CornerRadius = "20",
                        Child = new TextBlock
                        {
                            Text = glyph,
                            FontSize = 16,
                            MarginLeft = 11,
                            MarginTop = 9,
                        },
                    },
                    new TextBlock
                    {
                        Text = title,
                        FontSize = 15,
                        FontStyle = FontStyles.Bold,
                        Foreground = "#1B1B1B",
                        MarginLeft = 60,
                        MarginTop = 12,
                    },
                    new TextBlock
                    {
                        Text = subtitle,
                        FontSize = 12,
                        Foreground = "#8E8E8E",
                        MarginLeft = 60,
                        MarginTop = 34,
                    },
                },
            },
            Commands =
            {
                { nameof(Button.Click), (s, e) => onClick() },
            },
        };

    private UIElement BuildChatThreadColumn()
    {
        // Always occupy the remaining space so the list/thread splitter stays visible
        // even when no conversation is selected (message list hidden / empty state).
        return new Border
        {
            Width = "100%",
            Height = "100%",
            Background = "#FFFFFF",
            Child = new DockPanel
            {
                Width = "100%",
                Height = "100%",
                Children =
                {
                    // Thread header
                    new Border
                    {
                        [DockPanel.Dock] = Dock.Top,
                        Width = "100%",
                        Height = 64,
                        Background = "#FFFFFF",
                        BorderFill = "#E0E0E0",
                        BorderStroke = "0,0,0,1",
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
                        Child = new DockPanel
                        {
                            Width = "100%",
                            Height = "100%",
                            Children =
                            {
                                new TextBlock
                                {
                                    [DockPanel.Dock] = Dock.Right,
                                    FontSize = 12,
                                    Foreground = "#8E8E8E",
                                    MarginTop = 24,
                                    MarginRight = 16,
                                    Bindings =
                                    {
                                        { nameof(TextBlock.Text), nameof(MainViewModel.StatusText) },
                                    },
                                },
                                new Border
                                {
                                    [DockPanel.Dock] = Dock.Left,
                                    Width = 40,
                                    Height = 40,
                                    MarginLeft = 16,
                                    MarginTop = 12,
                                    Background = "#C5C8CE",
                                    CornerRadius = "20",
                                    Child = new TextBlock
                                    {
                                        FontSize = 16,
                                        FontStyle = FontStyles.Bold,
                                        Foreground = "#FFFFFF",
                                        MarginLeft = 13,
                                        MarginTop = 9,
                                        Bindings =
                                        {
                                            {
                                                nameof(TextBlock.Text),
                                                nameof(MainViewModel.SelectedConversationInitial)
                                            },
                                        },
                                    },
                                },
                                new TextBlock
                                {
                                    FontSize = 16,
                                    FontStyle = FontStyles.Bold,
                                    Foreground = "#1B1B1B",
                                    MarginLeft = 12,
                                    MarginTop = 20,
                                    Bindings =
                                    {
                                        {
                                            nameof(TextBlock.Text),
                                            nameof(MainViewModel.SelectedConversationTitle)
                                        },
                                    },
                                },
                            },
                        },
                    },
                    // Composer
                    new Border
                    {
                        [DockPanel.Dock] = Dock.Bottom,
                        Width = "100%",
                        Height = 64,
                        Padding = "12,10,12,10",
                        Background = "#FFFFFF",
                        BorderFill = "#E0E0E0",
                        BorderStroke = "0,1,0,0",
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
                        Child = new DockPanel
                        {
                            Width = "100%",
                            Height = "100%",
                            Children =
                            {
                                new Button
                                {
                                    [DockPanel.Dock] = Dock.Left,
                                    Content = "+",
                                    Width = 40,
                                    Height = 40,
                                    Background = "#F0F0F0",
                                    BorderFill = "Transparent",
                                    CornerRadius = "20",
                                    Foreground = "#3B3B3B",
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
                                    [DockPanel.Dock] = Dock.Right,
                                    Content = "➤",
                                    Width = 40,
                                    Height = 40,
                                    Background = SignalBlue,
                                    Foreground = "#FFFFFF",
                                    BorderFill = "Transparent",
                                    CornerRadius = "20",
                                    Commands =
                                    {
                                        {
                                            nameof(Button.Click),
                                            (s, e) => _ = _viewModel.SendCommand.ExecuteAsync(null)
                                        },
                                    },
                                },
                                new Border
                                {
                                    Width = "100%",
                                    Height = 40,
                                    MarginLeft = 8,
                                    MarginRight = 8,
                                    Background = "#F0F0F0",
                                    CornerRadius = "20",
                                    Child = new TextBox
                                    {
                                        Width = "100%",
                                        Height = 40,
                                        Background = "Transparent",
                                        BorderFill = "Transparent",
                                        Padding = "14,10,14,10",
                                        FontSize = 15,
                                        Bindings =
                                        {
                                            {
                                                nameof(TextBox.Text),
                                                nameof(MainViewModel.ComposeText),
                                                null,
                                                BindingMode.TwoWay
                                            },
                                            {
                                                nameof(TextBox.IsEnabled),
                                                nameof(MainViewModel.HasSelectedConversation),
                                                null,
                                                BindingMode.OneWay
                                            },
                                        },
                                    },
                                },
                            },
                        },
                    },
                    // Messages + empty state
                    new Panel
                    {
                        Width = "100%",
                        Height = "100%",
                        Background = "#FFFFFF",
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
                            new ListBox
                            {
                                Width = "100%",
                                Height = "100%",
                                Background = "#FFFFFF",
                                ItemTemplate = typeof(MessageListItem),
                                Bindings =
                                {
                                    { nameof(ListBox.Items), nameof(MainViewModel.Messages) },
                                    {
                                        nameof(Visibility),
                                        nameof(MainViewModel.HasSelectedConversation),
                                        null,
                                        BindingMode.OneWay,
                                        BoolToVisibility,
                                        null
                                    },
                                },
                            },
                            new StackPanel
                            {
                                Width = 360,
                                MarginTop = 180,
                                IsHitTestVisible = false,
                                Bindings =
                                {
                                    {
                                        nameof(Visibility),
                                        nameof(MainViewModel.HasSelectedConversation),
                                        null,
                                        BindingMode.OneWay,
                                        a => a is true ? Visibility.Collapsed : Visibility.Visible,
                                        null
                                    },
                                    {
                                        nameof(MarginLeft),
                                        nameof(MainViewModel.HasSelectedConversation),
                                        null,
                                        BindingMode.OneWay,
                                        _ => (object)"auto",
                                        null
                                    },
                                    {
                                        nameof(MarginRight),
                                        nameof(MainViewModel.HasSelectedConversation),
                                        null,
                                        BindingMode.OneWay,
                                        _ => (object)"auto",
                                        null
                                    },
                                },
                                Children =
                                {
                                    new SignalBubbleIcon
                                    {
                                        Width = 64,
                                        Height = 64,
                                        MarginLeft = 148,
                                    },
                                    new TextBlock
                                    {
                                        Text = "Select a chat",
                                        FontSize = 20,
                                        FontStyle = FontStyles.Bold,
                                        Foreground = "#1B1B1B",
                                        MarginTop = 16,
                                        TextAlignment = TextAlignment.Center,
                                        Width = 360,
                                    },
                                    new TextBlock
                                    {
                                        Text = "Or tap ✎ to start a new conversation",
                                        FontSize = 14,
                                        Foreground = "#8E8E8E",
                                        MarginTop = 8,
                                        TextAlignment = TextAlignment.Center,
                                        Width = 360,
                                    },
                                },
                            },
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildCallsTab()
    {
        return new DockPanel
        {
            Width = "100%",
            Height = "100%",
            Background = "#FFFFFF",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingCallsTab),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                // Calls list pane
                new Border
                {
                    [DockPanel.Dock] = Dock.Left,
                    Width = 321,
                    Height = "100%",
                    Background = "#FFFFFF",
                    Child = new DockPanel
                    {
                        Width = "100%",
                        Height = "100%",
                        Children =
                        {
                            new Border
                            {
                                [DockPanel.Dock] = Dock.Right,
                                Width = 1,
                                Height = "100%",
                                Background = "#E0E0E0",
                                BorderFill = "Transparent",
                            },
                            new Border
                            {
                                [DockPanel.Dock] = Dock.Top,
                                Width = "100%",
                                Height = 56,
                                Child = new DockPanel
                                {
                                    Width = "100%",
                                    Height = "100%",
                                    Children =
                                    {
                                        new Button
                                        {
                                            [DockPanel.Dock] = Dock.Right,
                                            Content = "+",
                                            Width = 36,
                                            Height = 36,
                                            MarginTop = 10,
                                            MarginRight = 12,
                                            Background = "Transparent",
                                            BorderFill = "Transparent",
                                            Foreground = "#3B3B3B",
                                            Commands =
                                            {
                                                {
                                                    nameof(Button.Click),
                                                    (s, e) => _viewModel.StatusText = "新建通话尚未实现"
                                                },
                                            },
                                        },
                                        new Button
                                        {
                                            [DockPanel.Dock] = Dock.Left,
                                            Content = "☰",
                                            Width = 36,
                                            Height = 36,
                                            MarginLeft = 8,
                                            MarginTop = 10,
                                            Background = "Transparent",
                                            BorderFill = "Transparent",
                                            Foreground = "#3B3B3B",
                                            Commands =
                                            {
                                                {
                                                    nameof(Button.Click),
                                                    (s, e) => _viewModel.ToggleNavRailCommand.Execute(null)
                                                },
                                            },
                                            Bindings =
                                            {
                                                {
                                                    nameof(Visibility),
                                                    nameof(MainViewModel.IsNavRailCollapsed),
                                                    null,
                                                    BindingMode.OneWay,
                                                    BoolToVisibility,
                                                    null
                                                },
                                            },
                                        },
                                        new TextBlock
                                        {
                                            Text = "Calls",
                                            FontSize = 24,
                                            FontStyle = FontStyles.Bold,
                                            Foreground = "#1B1B1B",
                                            MarginLeft = 12,
                                            MarginTop = 12,
                                        },
                                    },
                                },
                            },
                            new StackPanel
                            {
                                Width = "100%",
                                MarginTop = 80,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = "No calls yet",
                                        FontSize = 16,
                                        FontStyle = FontStyles.Bold,
                                        Foreground = "#1B1B1B",
                                        TextAlignment = TextAlignment.Center,
                                        Width = 300,
                                        MarginLeft = 10,
                                    },
                                    new TextBlock
                                    {
                                        Text = "Voice and video call history will appear here.",
                                        FontSize = 13,
                                        Foreground = "#8E8E8E",
                                        TextAlignment = TextAlignment.Center,
                                        Width = 300,
                                        MarginLeft = 10,
                                        MarginTop = 8,
                                    },
                                },
                            },
                        },
                    },
                },
                // Calls detail / empty state
                new StackPanel
                {
                    Width = 420,
                    MarginTop = 200,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "📞",
                            FontSize = 48,
                            TextAlignment = TextAlignment.Center,
                            Width = 420,
                        },
                        new TextBlock
                        {
                            Text = "No call selected",
                            FontSize = 20,
                            FontStyle = FontStyles.Bold,
                            Foreground = "#1B1B1B",
                            TextAlignment = TextAlignment.Center,
                            Width = 420,
                            MarginTop = 12,
                        },
                        new TextBlock
                        {
                            Text = "Start a call from a chat, or view call history on the left.",
                            FontSize = 14,
                            Foreground = "#8E8E8E",
                            TextAlignment = TextAlignment.Center,
                            Width = 420,
                            MarginTop = 8,
                        },
                    },
                    Bindings =
                    {
                        {
                            nameof(MarginLeft),
                            nameof(MainViewModel.IsShowingCallsTab),
                            null,
                            BindingMode.OneWay,
                            _ => (object)"auto",
                            null
                        },
                        {
                            nameof(MarginRight),
                            nameof(MainViewModel.IsShowingCallsTab),
                            null,
                            BindingMode.OneWay,
                            _ => (object)"auto",
                            null
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildStoriesTab()
    {
        return new DockPanel
        {
            Width = "100%",
            Height = "100%",
            Background = "#FFFFFF",
            Bindings =
            {
                {
                    nameof(Visibility),
                    nameof(MainViewModel.IsShowingStoriesTab),
                    null,
                    BindingMode.OneWay,
                    BoolToVisibility,
                    null
                },
            },
            Children =
            {
                // Stories list pane
                new Border
                {
                    [DockPanel.Dock] = Dock.Left,
                    Width = 321,
                    Height = "100%",
                    Background = "#FFFFFF",
                    Child = new DockPanel
                    {
                        Width = "100%",
                        Height = "100%",
                        Children =
                        {
                            new Border
                            {
                                [DockPanel.Dock] = Dock.Right,
                                Width = 1,
                                Height = "100%",
                                Background = "#E0E0E0",
                                BorderFill = "Transparent",
                            },
                            new Border
                            {
                                [DockPanel.Dock] = Dock.Top,
                                Width = "100%",
                                Height = 56,
                                Child = new DockPanel
                                {
                                    Width = "100%",
                                    Height = "100%",
                                    Children =
                                    {
                                        new Button
                                        {
                                            [DockPanel.Dock] = Dock.Right,
                                            Content = "+",
                                            Width = 36,
                                            Height = 36,
                                            MarginTop = 10,
                                            MarginRight = 12,
                                            Background = "Transparent",
                                            BorderFill = "Transparent",
                                            Foreground = "#3B3B3B",
                                            Commands =
                                            {
                                                {
                                                    nameof(Button.Click),
                                                    (s, e) => _viewModel.StatusText = "发布动态尚未实现"
                                                },
                                            },
                                        },
                                        new Button
                                        {
                                            [DockPanel.Dock] = Dock.Left,
                                            Content = "☰",
                                            Width = 36,
                                            Height = 36,
                                            MarginLeft = 8,
                                            MarginTop = 10,
                                            Background = "Transparent",
                                            BorderFill = "Transparent",
                                            Foreground = "#3B3B3B",
                                            Commands =
                                            {
                                                {
                                                    nameof(Button.Click),
                                                    (s, e) => _viewModel.ToggleNavRailCommand.Execute(null)
                                                },
                                            },
                                            Bindings =
                                            {
                                                {
                                                    nameof(Visibility),
                                                    nameof(MainViewModel.IsNavRailCollapsed),
                                                    null,
                                                    BindingMode.OneWay,
                                                    BoolToVisibility,
                                                    null
                                                },
                                            },
                                        },
                                        new TextBlock
                                        {
                                            Text = "Stories",
                                            FontSize = 24,
                                            FontStyle = FontStyles.Bold,
                                            Foreground = "#1B1B1B",
                                            MarginLeft = 12,
                                            MarginTop = 12,
                                        },
                                    },
                                },
                            },
                            // My Story row
                            new Border
                            {
                                [DockPanel.Dock] = Dock.Top,
                                Width = "100%",
                                Height = 72,
                                MarginLeft = 8,
                                MarginRight = 8,
                                MarginTop = 4,
                                Background = "#F7F7F7",
                                CornerRadius = "10",
                                Child = new Panel
                                {
                                    Width = "100%",
                                    Height = "100%",
                                    Children =
                                    {
                                        new Border
                                        {
                                            Width = 48,
                                            Height = 48,
                                            MarginLeft = 12,
                                            MarginTop = 12,
                                            Background = SignalBlue,
                                            CornerRadius = "24",
                                            Child = new TextBlock
                                            {
                                                Text = "+",
                                                FontSize = 22,
                                                Foreground = "#FFFFFF",
                                                MarginLeft = 16,
                                                MarginTop = 8,
                                            },
                                        },
                                        new TextBlock
                                        {
                                            Text = "My Story",
                                            FontSize = 15,
                                            FontStyle = FontStyles.Bold,
                                            Foreground = "#1B1B1B",
                                            MarginLeft = 72,
                                            MarginTop = 16,
                                        },
                                        new TextBlock
                                        {
                                            Text = "Tap to add a story",
                                            FontSize = 13,
                                            Foreground = "#8E8E8E",
                                            MarginLeft = 72,
                                            MarginTop = 38,
                                        },
                                    },
                                },
                            },
                            new StackPanel
                            {
                                Width = "100%",
                                MarginTop = 40,
                                Children =
                                {
                                    new TextBlock
                                    {
                                        Text = "No stories yet",
                                        FontSize = 16,
                                        FontStyle = FontStyles.Bold,
                                        Foreground = "#1B1B1B",
                                        TextAlignment = TextAlignment.Center,
                                        Width = 300,
                                        MarginLeft = 10,
                                    },
                                    new TextBlock
                                    {
                                        Text = "Stories from your contacts will show up here.",
                                        FontSize = 13,
                                        Foreground = "#8E8E8E",
                                        TextAlignment = TextAlignment.Center,
                                        Width = 300,
                                        MarginLeft = 10,
                                        MarginTop = 8,
                                    },
                                },
                            },
                        },
                    },
                },
                // Stories viewer empty state
                new StackPanel
                {
                    Width = 420,
                    MarginTop = 200,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = "▢",
                            FontSize = 48,
                            TextAlignment = TextAlignment.Center,
                            Width = 420,
                        },
                        new TextBlock
                        {
                            Text = "Click on a story to view it",
                            FontSize = 20,
                            FontStyle = FontStyles.Bold,
                            Foreground = "#1B1B1B",
                            TextAlignment = TextAlignment.Center,
                            Width = 420,
                            MarginTop = 12,
                        },
                        new TextBlock
                        {
                            Text = "Stories disappear after 24 hours.",
                            FontSize = 14,
                            Foreground = "#8E8E8E",
                            TextAlignment = TextAlignment.Center,
                            Width = 420,
                            MarginTop = 8,
                        },
                    },
                    Bindings =
                    {
                        {
                            nameof(MarginLeft),
                            nameof(MainViewModel.IsShowingStoriesTab),
                            null,
                            BindingMode.OneWay,
                            _ => (object)"auto",
                            null
                        },
                        {
                            nameof(MarginRight),
                            nameof(MainViewModel.IsShowingStoriesTab),
                            null,
                            BindingMode.OneWay,
                            _ => (object)"auto",
                            null
                        },
                    },
                },
            },
        };
    }

    private UIElement BuildSettingsPanel()
    {
        return new Border
        {
            Width = "100%",
            Height = "100%",
            Background = "#FFFFFF",
            ZIndex = 30,
            Padding = "32",
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
                Width = 520,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Settings",
                        FontSize = 24,
                        FontStyle = FontStyles.Bold,
                        Foreground = "#1B1B1B",
                    },
                    new TextBlock
                    {
                        MarginTop = 16,
                        FontSize = 13,
                        Foreground = "#4B5563",
                        Bindings =
                        {
                            { nameof(TextBlock.Text), nameof(MainViewModel.SettingsSummary) },
                        },
                    },
                    new CheckBox
                    {
                        Content = "Desktop notification tips",
                        MarginTop = 20,
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
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        MarginTop = 24,
                        Children =
                        {
                            new Button
                            {
                                Content = "Save",
                                Width = 100,
                                Height = 36,
                                Background = SignalBlue,
                                Foreground = "#FFFFFF",
                                BorderFill = null,
                                CornerRadius = "8",
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
                                Content = "Back to chats",
                                Width = 120,
                                Height = 36,
                                MarginLeft = 10,
                                Background = "#E7E7E7",
                                Foreground = "#1B1B1B",
                                BorderFill = null,
                                CornerRadius = "8",
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

    private static object BoolToVisibility(object? value) =>
        value is true ? Visibility.Visible : Visibility.Collapsed;
}
