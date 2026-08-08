using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CPF.Drawing;
using CPF.Threading;
using SignalCpf.Core.Abstractions;
using SignalCpf.Core.Models;
using SignalCpf.UI.Helpers;

namespace SignalCpf.UI.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ISignalSidecarClient _client;
    private CancellationTokenSource? _eventsCts;

    [ObservableProperty]
    private string _statusText = "Disconnected";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingInstallScreen))]
    [NotifyPropertyChangedFor(nameof(IsShowingChat))]
    [NotifyPropertyChangedFor(nameof(IsShowingQrCard))]
    [NotifyPropertyChangedFor(nameof(IsShowingLinkMode))]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterMode))]
    private bool _isRegistered;

    [ObservableProperty]
    private string _deviceName = "Desktop";

    [ObservableProperty]
    private string? _provisioningUrl;

    [ObservableProperty]
    private Image? _qrCodeImage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingQrCard))]
    private bool _isQrLoading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingQrCard))]
    private bool _isLinkInProgress;

    /// <summary>false = link device (QR), true = phone registration.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingLinkMode))]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterMode))]
    [NotifyPropertyChangedFor(nameof(IsShowingQrCard))]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterPhoneStep))]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterCaptchaStep))]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterVerifyStep))]
    private bool _isRegisterMode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendRegistrationCode))]
    [NotifyPropertyChangedFor(nameof(RegisterPhoneNumber))]
    [NotifyPropertyChangedFor(nameof(SelectedCountryLabel))]
    private string _registerCountryCode = CountryDialCodes.Default.DialCode;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendRegistrationCode))]
    [NotifyPropertyChangedFor(nameof(RegisterPhoneNumber))]
    private string _registerNationalNumber = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedCountryLabel))]
    private CountryDialOption? _selectedCountry = CountryDialCodes.Default;

    [ObservableProperty]
    private bool _isCountryPickerOpen;

    public ObservableCollection<CountryDialOption> Countries { get; } = new(CountryDialCodes.All);

    public string SelectedCountryLabel =>
        SelectedCountry?.DialCode
        ?? (string.IsNullOrWhiteSpace(RegisterCountryCode) ? "+1" : RegisterCountryCode);

    [ObservableProperty]
    private string _registerCaptchaToken = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCompleteRegistration))]
    private string _registerVerificationCode = string.Empty;

    [ObservableProperty]
    private bool _registerUseVoice;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterCaptchaStep))]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterVerifyStep))]
    private bool _registerCaptchaRequired;

    [ObservableProperty]
    private string? _registerChallengeUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSendRegistrationCode))]
    [NotifyPropertyChangedFor(nameof(CanCompleteRegistration))]
    private bool _isRegisterBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterVerifyStep))]
    [NotifyPropertyChangedFor(nameof(IsShowingRegisterPhoneStep))]
    private bool _registerCodeSent;

    public string RegisterPhoneNumber => ComposeE164(RegisterCountryCode, RegisterNationalNumber);

    public bool CanSendRegistrationCode =>
        !IsRegisterBusy && LooksLikeValidPhone(RegisterCountryCode, RegisterNationalNumber);

    public bool CanCompleteRegistration =>
        !IsRegisterBusy && RegisterVerificationCode.Trim().Length >= 6;

    public bool IsShowingRegisterPhoneStep => IsShowingRegisterMode && !RegisterCodeSent;

    public bool IsShowingRegisterCaptchaStep =>
        IsShowingRegisterMode && RegisterCaptchaRequired;

    public bool IsShowingRegisterVerifyStep =>
        IsShowingRegisterMode && (RegisterCodeSent || RegisterCaptchaRequired);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelectedConversation))]
    [NotifyPropertyChangedFor(nameof(SelectedConversationTitle))]
    [NotifyPropertyChangedFor(nameof(SelectedConversationInitial))]
    private ConversationItemViewModel? _selectedConversation;

    [ObservableProperty]
    private ContactItemViewModel? _selectedComposeContact;

    [ObservableProperty]
    private string _composeText = string.Empty;

    [ObservableProperty]
    private string _conversationFilter = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingNewChatPanel))]
    [NotifyPropertyChangedFor(nameof(IsShowingConversationList))]
    [NotifyPropertyChangedFor(nameof(IsNewChatHome))]
    [NotifyPropertyChangedFor(nameof(IsNewChatFindUsername))]
    [NotifyPropertyChangedFor(nameof(IsNewChatFindPhone))]
    [NotifyPropertyChangedFor(nameof(IsNewChatCreateGroup))]
    [NotifyPropertyChangedFor(nameof(NewChatTitle))]
    private bool _isNewChatOpen;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNewChatHome))]
    [NotifyPropertyChangedFor(nameof(IsNewChatFindUsername))]
    [NotifyPropertyChangedFor(nameof(IsNewChatFindPhone))]
    [NotifyPropertyChangedFor(nameof(IsNewChatCreateGroup))]
    [NotifyPropertyChangedFor(nameof(NewChatTitle))]
    private NewChatStep _newChatStep = NewChatStep.Home;

    [ObservableProperty]
    private string _findUsernameQuery = string.Empty;

    [ObservableProperty]
    private string _findPhoneQuery = string.Empty;

    [ObservableProperty]
    private string _newGroupName = string.Empty;

    [ObservableProperty]
    private string _newGroupMembersText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingSettings))]
    [NotifyPropertyChangedFor(nameof(IsShowingChatMain))]
    [NotifyPropertyChangedFor(nameof(IsShowingChatsTab))]
    [NotifyPropertyChangedFor(nameof(IsShowingCallsTab))]
    [NotifyPropertyChangedFor(nameof(IsShowingStoriesTab))]
    [NotifyPropertyChangedFor(nameof(IsShowingNewChatPanel))]
    [NotifyPropertyChangedFor(nameof(IsShowingConversationList))]
    private bool _settingsOpen;

    /// <summary>Left module nav rail (Signal hamburger toggles this).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsNavRailCollapsed))]
    private bool _isNavRailVisible = true;

    public bool IsNavRailCollapsed => !IsNavRailVisible;

    /// <summary>Signal desktop primary tabs: Chats / Calls / Stories.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingChatsTab))]
    [NotifyPropertyChangedFor(nameof(IsShowingCallsTab))]
    [NotifyPropertyChangedFor(nameof(IsShowingStoriesTab))]
    [NotifyPropertyChangedFor(nameof(IsShowingChatMain))]
    [NotifyPropertyChangedFor(nameof(IsChatsNavSelected))]
    [NotifyPropertyChangedFor(nameof(IsCallsNavSelected))]
    [NotifyPropertyChangedFor(nameof(IsStoriesNavSelected))]
    private MainNavTab _mainNavTab = MainNavTab.Chats;

    [ObservableProperty]
    private string _settingsSummary = string.Empty;

    [ObservableProperty]
    private string _newContactServiceId = string.Empty;

    [ObservableProperty]
    private string _newContactName = string.Empty;

    [ObservableProperty]
    private bool _notificationsEnabled = true;

    [ObservableProperty]
    private string _serverUrlDisplay = string.Empty;

    public bool IsShowingInstallScreen => !IsRegistered;
    public bool IsShowingChat => IsRegistered;
    public bool IsShowingChatsTab => IsRegistered && !SettingsOpen && MainNavTab == MainNavTab.Chats;
    public bool IsShowingCallsTab => IsRegistered && !SettingsOpen && MainNavTab == MainNavTab.Calls;
    public bool IsShowingStoriesTab => IsRegistered && !SettingsOpen && MainNavTab == MainNavTab.Stories;
    public bool IsShowingChatMain => IsShowingChatsTab;
    public bool IsShowingSettings => IsRegistered && SettingsOpen;
    public bool IsShowingNewChatPanel => IsShowingChatsTab && IsNewChatOpen;
    public bool IsShowingConversationList => IsShowingChatsTab && !IsNewChatOpen;
    public bool IsNewChatHome => IsShowingNewChatPanel && NewChatStep == NewChatStep.Home;
    public bool IsNewChatFindUsername => IsShowingNewChatPanel && NewChatStep == NewChatStep.FindUsername;
    public bool IsNewChatFindPhone => IsShowingNewChatPanel && NewChatStep == NewChatStep.FindPhone;
    public bool IsNewChatCreateGroup => IsShowingNewChatPanel && NewChatStep == NewChatStep.CreateGroup;

    public string NewChatTitle => NewChatStep switch
    {
        NewChatStep.FindUsername => "Find by username",
        NewChatStep.FindPhone => "Find by phone number",
        NewChatStep.CreateGroup => "Create a group",
        _ => "New chat",
    };

    public bool IsShowingLinkMode => !IsRegistered && !IsRegisterMode;
    public bool IsShowingRegisterMode => !IsRegistered && IsRegisterMode;
    public bool IsShowingQrCard =>
        !IsRegistered && !IsRegisterMode && !IsLinkInProgress && !IsQrLoading;

    public bool IsChatsNavSelected => MainNavTab == MainNavTab.Chats;
    public bool IsCallsNavSelected => MainNavTab == MainNavTab.Calls;
    public bool IsStoriesNavSelected => MainNavTab == MainNavTab.Stories;

    public bool HasSelectedConversation => SelectedConversation is not null;
    public string SelectedConversationTitle => SelectedConversation?.Title ?? "Signal";
    public string SelectedConversationInitial => SelectedConversation?.AvatarInitial ?? "S";

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];
    public ObservableCollection<ConversationItemViewModel> FilteredConversations { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];
    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];

    public MainViewModel(ISignalSidecarClient client)
    {
        _client = client;
        SelectedCountry = CountryDialCodes.Default;
        RegisterCountryCode = CountryDialCodes.Default.DialCode;
    }

    partial void OnSelectedCountryChanged(CountryDialOption? value)
    {
        if (value is null)
            return;
        if (!string.Equals(RegisterCountryCode, value.DialCode, StringComparison.Ordinal))
            RegisterCountryCode = value.DialCode;
        IsCountryPickerOpen = false;
    }

    partial void OnRegisterCountryCodeChanged(string value)
    {
        var normalized = NormalizeDialCode(value);
        if (SelectedCountry is not null
            && string.Equals(SelectedCountry.DialCode, normalized, StringComparison.Ordinal))
            return;

        var match = CountryDialCodes.FindByDialCode(normalized);
        if (match is not null)
            SelectedCountry = match;
    }

    partial void OnRegisterNationalNumberChanged(string value)
    {
        var digits = new string((value ?? "").Where(char.IsDigit).ToArray());
        if (!string.Equals(value, digits, StringComparison.Ordinal))
            RegisterNationalNumber = digits;
    }

    private static string NormalizeDialCode(string? raw)
    {
        var code = (raw ?? "").Trim().Replace(" ", "", StringComparison.Ordinal);
        if (string.IsNullOrEmpty(code))
            return "+1";
        return code.StartsWith('+') ? code : "+" + code.TrimStart('+');
    }

    [RelayCommand]
    private void ToggleCountryPicker()
    {
        if (IsRegisterBusy)
            return;
        IsCountryPickerOpen = !IsCountryPickerOpen;
    }

    [RelayCommand]
    private void CloseCountryPicker()
    {
        IsCountryPickerOpen = false;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _client.ConnectAsync();
            await RefreshSettingsAsync();

            var reachable = await _client.HealthAsync();
            StatusText = reachable
                ? $"服务器可达：{ServerUrlDisplay}"
                : $"服务器不可达：{ServerUrlDisplay}（将仍尝试配钥；请检查 SIGNAL_SERVER_URL）";

            var account = await _client.GetAccountStatusAsync();
            ApplyAccount(account);
            if (!reachable && !account.IsRegistered)
            {
                StatusText =
                    $"无法连接服务器：{ServerUrlDisplay}。自建：设 SIGNAL_SERVER_URL + 可选 INSECURE_TLS=1；" +
                    "官方：设 SIGNAL_SERVER_PROFILE=official 后重启。";
            }

            var settings = await _client.GetSettingsAsync();
            if (string.Equals(settings.ServerProfile, "Official", StringComparison.OrdinalIgnoreCase)
                && !settings.UsesNativeLibSignal)
            {
                StatusText =
                    "官方服务器需要 libsignal FFI。请先运行 scripts/build-libsignal-ffi.ps1，确认 native/signal_ffi.dll 存在。";
            }

            _eventsCts = new CancellationTokenSource();
            _ = ListenEventsAsync(_eventsCts.Token);
            if (account.IsRegistered)
            {
                await RefreshConversationsAsync();
                await RefreshContactsAsync();
            }
            else if (!IsRegisterMode
                     && (reachable
                         || string.Equals(settings.ServerProfile, "Official", StringComparison.OrdinalIgnoreCase)))
            {
                await StartProvisioningAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"连接失败：{ex.Message}";
        }
    }

    private void ApplyAccount(AccountStatus account)
    {
        IsRegistered = account.IsRegistered;
        if (account.IsRegistered)
        {
            IsLinkInProgress = false;
            IsRegisterBusy = false;
            ClearQr();
            StatusText = account.DeviceId <= 1
                ? $"已注册：{account.Number ?? account.DeviceName ?? account.ServiceId}"
                : $"已关联：{account.DeviceName ?? account.ServiceId}";
        }
        else if (!IsRegisterMode)
        {
            StatusText = "未注册 — 请扫描二维码关联设备，或切换到「注册账户」";
        }
    }

    [RelayCommand]
    private async Task SwitchToLinkModeAsync()
    {
        if (!IsRegisterMode)
            return;
        await _client.CancelRegistrationAsync();
        IsRegisterMode = false;
        RegisterCaptchaRequired = false;
        RegisterCodeSent = false;
        StatusText = "关联设备模式";
        await StartProvisioningAsync();
    }

    [RelayCommand]
    private async Task SwitchToRegisterModeAsync()
    {
        if (IsRegisterMode)
            return;
        await _client.CancelProvisioningAsync();
        ClearQr();
        IsLinkInProgress = false;
        IsQrLoading = false;
        IsRegisterMode = true;
        RegisterCodeSent = false;
        RegisterCaptchaRequired = false;
        StatusText = string.Empty;
        await RefreshSettingsAsync();
    }

    [RelayCommand]
    private async Task StartProvisioningAsync()
    {
        if (IsRegisterMode)
            return;

        try
        {
            IsLinkInProgress = false;
            IsQrLoading = true;
            ClearQr();
            StatusText = "正在连接配钥服务…";

            var qr = await _client.StartProvisioningAsync(DeviceName);
            ApplyProvisioningUrl(qr.Url);
            StatusText = "请使用手机 Signal 扫描二维码";
        }
        catch (Exception ex)
        {
            var root = ex;
            while (root.InnerException is not null)
                root = root.InnerException;
            StatusText = root.Message == ex.Message
                ? $"关联失败：{ex.Message}"
                : $"关联失败：{ex.Message}（{root.Message}）";
            ClearQr();
        }
        finally
        {
            IsQrLoading = false;
        }
    }

    [RelayCommand]
    private Task SendSmsRegistrationAsync()
    {
        RegisterUseVoice = false;
        return StartPhoneRegistrationAsync();
    }

    [RelayCommand]
    private Task CallRegistrationAsync()
    {
        RegisterUseVoice = true;
        return StartPhoneRegistrationAsync();
    }

    [RelayCommand]
    private async Task StartPhoneRegistrationAsync()
    {
        var e164 = RegisterPhoneNumber;
        if (!LooksLikeValidPhone(RegisterCountryCode, RegisterNationalNumber))
        {
            StatusText = "请输入有效手机号";
            return;
        }

        try
        {
            IsRegisterBusy = true;
            StatusText = RegisterUseVoice ? "正在请求语音验证码…" : "正在发送短信验证码…";
            var transport = RegisterUseVoice ? "voice" : "sms";
            var captcha = string.IsNullOrWhiteSpace(RegisterCaptchaToken)
                ? null
                : RegisterCaptchaToken.Trim();
            var status = await _client.StartPhoneRegistrationAsync(e164, captcha, transport);
            ApplyRegistrationStatus(status);
        }
        catch (Exception ex)
        {
            StatusText = $"注册失败：{RootMessage(ex)}";
        }
        finally
        {
            IsRegisterBusy = false;
        }
    }

    private static string ComposeE164(string countryCode, string national)
    {
        var cc = (countryCode ?? "").Trim().Replace(" ", "", StringComparison.Ordinal);
        if (!cc.StartsWith('+'))
            cc = "+" + cc.TrimStart('+');

        var digits = new string((national ?? "").Where(char.IsDigit).ToArray());
        // National fields must not include trunk prefix or a duplicated country code.
        digits = digits.TrimStart('0');
        var ccDigits = cc.TrimStart('+');
        if (digits.StartsWith(ccDigits, StringComparison.Ordinal)
            && digits.Length >= ccDigits.Length + 7)
        {
            digits = digits[ccDigits.Length..].TrimStart('0');
        }

        return string.IsNullOrEmpty(digits) ? cc : cc + digits;
    }

    private static bool LooksLikeValidPhone(string countryCode, string national)
    {
        var e164 = ComposeE164(countryCode, national);
        // Mirror Signal server "possible number" length bounds roughly (E.164 max 15 digits).
        var nationalDigits = e164.StartsWith('+') ? e164[1..] : e164;
        var ccDigits = (countryCode ?? "").Trim().TrimStart('+');
        if (nationalDigits.StartsWith(ccDigits, StringComparison.Ordinal))
            nationalDigits = nationalDigits[ccDigits.Length..];

        return e164.StartsWith('+')
               && e164.Length is >= 10 and <= 16
               && e164[1..].All(char.IsDigit)
               && nationalDigits.Length is >= 6 and <= 14;
    }

    [RelayCommand]
    private void OpenCaptchaPage()
    {
        var url = string.IsNullOrWhiteSpace(RegisterChallengeUrl)
            ? "https://signalcaptchas.org/registration/generate.html"
            : RegisterChallengeUrl.Trim();
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true,
            });
            StatusText = "已在浏览器打开 Captcha：完成后右键「Open Signal」复制链接，粘贴到下方并提交";
        }
        catch (Exception ex)
        {
            StatusText = $"无法打开浏览器：{ex.Message}。请手动访问：{url}";
        }
    }

    [RelayCommand]
    private async Task SubmitRegistrationCaptchaAsync()
    {
        if (string.IsNullOrWhiteSpace(RegisterCaptchaToken))
        {
            StatusText = "请粘贴 Captcha token（来自浏览器 signalcaptcha://…）";
            return;
        }

        try
        {
            IsRegisterBusy = true;
            StatusText = "正在提交 Captcha…";
            var status = await _client.SubmitRegistrationCaptchaAsync(RegisterCaptchaToken.Trim());
            ApplyRegistrationStatus(status);
            if (status.Kind is RegistrationProgressKind.CodeRequested
                or RegistrationProgressKind.Verified
                or RegistrationProgressKind.Registered)
            {
                RegisterCaptchaToken = string.Empty;
            }
        }
        catch (Exception ex)
        {
            // Token is single-use; clear so the user pastes a freshly solved captcha.
            RegisterCaptchaToken = string.Empty;
            RegisterCaptchaRequired = true;
            StatusText = RootMessage(ex);
        }
        finally
        {
            IsRegisterBusy = false;
        }
    }

    [RelayCommand]
    private async Task RequestRegistrationCodeAsync()
    {
        try
        {
            IsRegisterBusy = true;
            StatusText = "正在请求验证码…";
            var status = await _client.RequestRegistrationCodeAsync(
                RegisterUseVoice ? "voice" : "sms");
            ApplyRegistrationStatus(status);
        }
        catch (Exception ex)
        {
            StatusText = $"请求验证码失败：{RootMessage(ex)}";
        }
        finally
        {
            IsRegisterBusy = false;
        }
    }

    [RelayCommand]
    private async Task CompletePhoneRegistrationAsync()
    {
        if (string.IsNullOrWhiteSpace(RegisterVerificationCode))
        {
            StatusText = "请输入短信/语音验证码";
            return;
        }

        try
        {
            IsRegisterBusy = true;
            StatusText = "正在验证并创建账户…";
            var account = await _client.CompletePhoneRegistrationAsync(
                RegisterVerificationCode.Trim(),
                DeviceName);
            ApplyAccount(account);
            if (account.IsRegistered)
            {
                await RefreshConversationsAsync();
                await RefreshContactsAsync();
            }
        }
        catch (Exception ex)
        {
            StatusText = $"完成注册失败：{RootMessage(ex)}";
        }
        finally
        {
            IsRegisterBusy = false;
        }
    }

    private void ApplyRegistrationStatus(RegistrationSessionStatus status)
    {
        var captchaJustRequired = status.CaptchaRequired && !RegisterCaptchaRequired;
        RegisterCaptchaRequired = status.CaptchaRequired;
        RegisterChallengeUrl = status.ChallengeUrl;
        RegisterCodeSent = status.Kind is RegistrationProgressKind.CodeRequested
            or RegistrationProgressKind.Verified
            or RegistrationProgressKind.Registered;
        if (!string.IsNullOrWhiteSpace(status.Message))
            StatusText = status.Message;

        if (captchaJustRequired)
            OpenCaptchaPage();
    }

    private static string RootMessage(Exception ex)
    {
        var root = ex;
        while (root.InnerException is not null)
            root = root.InnerException;
        return root.Message == ex.Message
            ? ex.Message
            : $"{ex.Message}（{root.Message}）";
    }

    private void ApplyProvisioningUrl(string? url)
    {
        ProvisioningUrl = url;
        var previous = QrCodeImage;
        QrCodeImage = QrCodeBitmapFactory.TryCreate(url);
        previous?.Dispose();
    }

    private void ClearQr()
    {
        var previous = QrCodeImage;
        QrCodeImage = null;
        ProvisioningUrl = null;
        previous?.Dispose();
    }

    [RelayCommand]
    private async Task RefreshConversationsAsync()
    {
        var previousId = SelectedConversation?.Id;
        Conversations.Clear();
        var list = await _client.ListConversationsAsync();
        foreach (var c in list)
            Conversations.Add(new ConversationItemViewModel(c));

        ApplyConversationFilter();

        if (Conversations.Count == 0)
        {
            SelectedConversation = null;
            StatusText = IsRegistered
                ? $"已注册: {await RegisteredNumberAsync()}"
                : StatusText;
            return;
        }

        SelectedConversation =
            FilteredConversations.FirstOrDefault(c => c.Id == previousId)
            ?? FilteredConversations.FirstOrDefault()
            ?? Conversations[0];
    }

    partial void OnConversationFilterChanged(string value) => ApplyConversationFilter();

    private void ApplyConversationFilter()
    {
        var q = ConversationFilter.Trim();
        FilteredConversations.Clear();
        IEnumerable<ConversationItemViewModel> src = Conversations;
        if (!string.IsNullOrEmpty(q))
        {
            src = Conversations.Where(c =>
                c.Title.Contains(q, StringComparison.OrdinalIgnoreCase)
                || c.Preview.Contains(q, StringComparison.OrdinalIgnoreCase)
                || (c.ServiceId?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
        }

        foreach (var c in src)
            FilteredConversations.Add(c);
    }

    [RelayCommand]
    private void ToggleNewChat()
    {
        if (IsNewChatOpen)
        {
            CloseNewChat();
            return;
        }

        SettingsOpen = false;
        NewChatStep = NewChatStep.Home;
        IsNewChatOpen = true;
        _ = RefreshContactsAsync();
    }

    [RelayCommand]
    private void CloseNewChat()
    {
        IsNewChatOpen = false;
        NewChatStep = NewChatStep.Home;
        FindUsernameQuery = string.Empty;
        FindPhoneQuery = string.Empty;
        NewGroupName = string.Empty;
        NewGroupMembersText = string.Empty;
        NewContactServiceId = string.Empty;
        NewContactName = string.Empty;
    }

    [RelayCommand]
    private void NewChatBack()
    {
        if (NewChatStep == NewChatStep.Home)
            CloseNewChat();
        else
            NewChatStep = NewChatStep.Home;
    }

    [RelayCommand]
    private void OpenCreateGroupStep() => NewChatStep = NewChatStep.CreateGroup;

    [RelayCommand]
    private void OpenFindUsernameStep() => NewChatStep = NewChatStep.FindUsername;

    [RelayCommand]
    private void OpenFindPhoneStep() => NewChatStep = NewChatStep.FindPhone;

    private async Task<string> RegisteredNumberAsync()
    {
        try
        {
            var account = await _client.GetAccountStatusAsync();
            return account.Number ?? account.ServiceId ?? "已注册";
        }
        catch
        {
            return "已注册";
        }
    }

    [RelayCommand]
    private async Task RefreshContactsAsync()
    {
        Contacts.Clear();
        var list = await _client.ListContactsAsync();
        foreach (var c in list)
            Contacts.Add(new ContactItemViewModel(c));
    }

    [RelayCommand]
    private async Task RefreshSettingsAsync()
    {
        var s = await _client.GetSettingsAsync();
        NotificationsEnabled = s.NotificationsEnabled;
        ServerUrlDisplay = s.ApiBaseUrl;
        if (!string.IsNullOrWhiteSpace(s.ChallengeUrl))
            RegisterChallengeUrl = s.ChallengeUrl;
        SettingsSummary =
            $"配置档: {s.ServerProfile}\n服务器: {s.ApiBaseUrl}\nCDN: {s.CdnUrl ?? "(same as API)"}\n" +
            $"Storage: {s.StorageUrl ?? "(unset)"}\nCaptcha: {s.ChallengeUrl ?? "(unset)"}\n" +
            $"数据目录: {s.DataDirectory}\n设备名: {s.DeviceName}\n" +
            $"TLS 不安全模式: {s.AllowInsecureTls}\nPQ 密钥: {s.EnablePqKeys}\n" +
            $"通知: {s.NotificationsEnabled}\n已读回执: {s.ReadReceiptsEnabled}\n" +
            $"libsignal FFI: {s.UsesNativeLibSignal}";
    }

    [RelayCommand]
    private void ToggleNavRail() => IsNavRailVisible = !IsNavRailVisible;

    [RelayCommand]
    private void ShowChatsTab()
    {
        SettingsOpen = false;
        MainNavTab = MainNavTab.Chats;
        StatusText = "Chats";
        _ = RefreshConversationsCommand.ExecuteAsync(null);
    }

    [RelayCommand]
    private void ShowCallsTab()
    {
        SettingsOpen = false;
        IsNewChatOpen = false;
        MainNavTab = MainNavTab.Calls;
        StatusText = "Calls";
    }

    [RelayCommand]
    private void ShowStoriesTab()
    {
        SettingsOpen = false;
        IsNewChatOpen = false;
        MainNavTab = MainNavTab.Stories;
        StatusText = "Stories";
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        SettingsOpen = !SettingsOpen;
        if (SettingsOpen)
        {
            IsNewChatOpen = false;
            _ = RefreshSettingsAsync();
        }
    }

    [RelayCommand]
    private async Task SaveSettingsAsync()
    {
        var current = await _client.GetSettingsAsync();
        await _client.UpdateSettingsAsync(current with
        {
            NotificationsEnabled = NotificationsEnabled,
        });
        await RefreshSettingsAsync();
        StatusText = "设置已保存";
    }

    [RelayCommand]
    private async Task AddContactAsync()
    {
        if (string.IsNullOrWhiteSpace(NewContactServiceId))
        {
            StatusText = "请填写对方 ACI（UUID）";
            return;
        }

        await OpenDirectChatAsync(
            NewContactServiceId.Trim(),
            number: null,
            displayName: string.IsNullOrWhiteSpace(NewContactName) ? null : NewContactName.Trim());
    }

    [RelayCommand]
    private async Task StartChatByUsernameAsync()
    {
        var raw = FindUsernameQuery.Trim().TrimStart('@');
        if (string.IsNullOrWhiteSpace(raw))
        {
            StatusText = "请输入用户名";
            return;
        }

        // Official username→ACI directory lookup is not wired yet; accept ACI UUID or create a local placeholder.
        if (LooksLikeUuid(raw))
        {
            await OpenDirectChatAsync(raw, number: null, displayName: raw);
            return;
        }

        var localId = "username:" + raw.ToLowerInvariant();
        await OpenDirectChatAsync(localId, number: null, displayName: "@" + raw);
        StatusText = $"已创建与 @{raw} 的本地会话（用户名目录查询尚未接入服务器）";
    }

    [RelayCommand]
    private async Task StartChatByPhoneAsync()
    {
        var e164 = NormalizeLookupPhone(FindPhoneQuery);
        if (e164 is null)
        {
            StatusText = "请输入有效手机号（E.164，如 +8613812345678）";
            return;
        }

        // Phone→ACI CDSI lookup is not wired; open a local conversation keyed by E.164.
        await OpenDirectChatAsync(e164, number: e164, displayName: e164);
        StatusText = $"已创建与 {e164} 的本地会话（手机号目录查询尚未接入服务器）";
    }

    [RelayCommand]
    private async Task CreateGroupAsync()
    {
        if (string.IsNullOrWhiteSpace(NewGroupName))
        {
            StatusText = "请输入群组名称";
            return;
        }

        var members = NewGroupMembersText
            .Split([',', ';', '\n', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        try
        {
            var conv = await _client.CreateGroupConversationAsync(NewGroupName.Trim(), members);
            CloseNewChat();
            await RefreshContactsAsync();
            await RefreshConversationsAsync();
            SelectedConversation = Conversations.FirstOrDefault(c => c.Id == conv.Id);
            StatusText = members.Count == 0
                ? $"已创建群组「{conv.Title}」"
                : $"已创建群组「{conv.Title}」（{members.Count} 名成员）";
        }
        catch (Exception ex)
        {
            StatusText = $"创建群组失败：{RootMessage(ex)}";
        }
    }

    [RelayCommand]
    private async Task StartChatWithContactAsync(ContactItemViewModel? contact)
    {
        if (contact is null)
            return;
        await OpenDirectChatAsync(contact.ServiceId, number: null, displayName: contact.DisplayName);
    }

    partial void OnSelectedComposeContactChanged(ContactItemViewModel? value)
    {
        if (value is null || !IsNewChatHome)
            return;
        _ = StartChatWithContactAsync(value);
        SelectedComposeContact = null;
    }

    private async Task OpenDirectChatAsync(string serviceId, string? number, string? displayName)
    {
        var id = serviceId.Trim();
        var name = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();
        try
        {
            await _client.UpsertContactAsync(new ContactInfo(
                id,
                Number: number,
                ProfileName: name,
                About: null));
            CloseNewChat();
            await RefreshContactsAsync();
            await RefreshConversationsAsync();
            SelectedConversation = Conversations.FirstOrDefault(c => c.Id == id);
            StatusText = $"已打开与 {name} 的会话";
        }
        catch (Exception ex)
        {
            StatusText = $"打开会话失败：{RootMessage(ex)}";
        }
    }

    private static bool LooksLikeUuid(string value) =>
        Guid.TryParse(value.Trim(), out _);

    private static string? NormalizeLookupPhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return null;
        var digits = new string(raw.Where(c => char.IsDigit(c) || c == '+').ToArray());
        if (!digits.StartsWith('+'))
            digits = "+" + digits.TrimStart('0');
        var national = digits[1..];
        if (national.Length is < 6 or > 15 || !national.All(char.IsDigit))
            return null;
        return digits;
    }

    [RelayCommand]
    private async Task AttachFileAsync()
    {
        if (SelectedConversation is null || Messages.Count == 0)
        {
            StatusText = "请先选择会话并发送一条消息后再附加文件";
            return;
        }

        // Staging path via env for headless/CPF simplicity
        var path = Environment.GetEnvironmentVariable("SIGNAL_ATTACH_FILE");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            StatusText = "设置 SIGNAL_ATTACH_FILE 指向要上传的本地文件";
            return;
        }

        var last = Messages[^1];
        var info = await _client.StageAttachmentAsync(last.Id, path);
        StatusText = info is null
            ? "附件暂存失败"
            : $"附件已暂存: {info.FileName} ({info.Size} bytes)";
    }

    partial void OnSelectedConversationChanged(ConversationItemViewModel? value)
    {
        _ = LoadMessagesAsync(value);
    }

    private async Task LoadMessagesAsync(ConversationItemViewModel? conv)
    {
        Messages.Clear();
        if (conv is null)
            return;

        var msgs = await _client.GetMessagesAsync(conv.Id);
        foreach (var m in msgs)
            Messages.Add(new MessageItemViewModel(m));

        // Mark latest inbound as read
        var latestIn = msgs.LastOrDefault(m => !m.IsOutgoing);
        if (latestIn is not null)
            await _client.SendReadReceiptAsync(conv.Id, latestIn.Id);
    }

    [RelayCommand]
    private async Task SendAsync()
    {
        if (SelectedConversation is null)
        {
            StatusText = "请先选择或添加会话";
            return;
        }

        if (string.IsNullOrWhiteSpace(ComposeText))
        {
            StatusText = "请输入要发送的内容";
            return;
        }

        var body = ComposeText.Trim();
        var convId = SelectedConversation.Id;
        try
        {
            ComposeText = string.Empty;
            StatusText = "正在发送…";
            var sent = await _client.SendTextMessageAsync(
                convId,
                SelectedConversation.ServiceId,
                body);
            Messages.Add(new MessageItemViewModel(sent));
            await RefreshConversationsAsync();
            StatusText = "已发送";
        }
        catch (Exception ex)
        {
            ComposeText = body;
            StatusText = $"发送失败：{RootMessage(ex)}";
        }
    }

    private async Task ListenEventsAsync(CancellationToken ct)
    {
        try
        {
            await foreach (var ev in _client.SubscribeEventsAsync(ct))
            {
                Dispatcher.MainThread.Invoke(() =>
                {
                    _ = HandleEventAsync(ev);
                });
            }
        }
        catch (OperationCanceledException)
        {
            // shutting down
        }
    }

    private async Task HandleEventAsync(SidecarEvent ev)
    {
        switch (ev)
        {
            case SidecarEvent.AccountStatusChanged a:
                ApplyAccount(a.Status);
                if (a.Status.IsRegistered)
                {
                    await RefreshConversationsAsync();
                    await RefreshContactsAsync();
                }
                break;
            case SidecarEvent.ProvisioningUpdated p:
                HandleProvisioningProgress(p.Progress);
                break;
            case SidecarEvent.RegistrationUpdated r:
                ApplyRegistrationStatus(r.Status);
                break;
            case SidecarEvent.MessageReceived m when
                SelectedConversation?.Id == m.Message.ConversationId:
                Messages.Add(new MessageItemViewModel(m.Message));
                break;
            case SidecarEvent.ConversationUpdated:
                await RefreshConversationsAsync();
                break;
            case SidecarEvent.Error e when e.Code == "NOTIFICATION":
                if (NotificationsEnabled)
                    StatusText = e.Message;
                break;
            case SidecarEvent.Error e:
                StatusText = $"错误 [{e.Code}]：{e.Message}";
                IsLinkInProgress = false;
                break;
        }
    }

    private void HandleProvisioningProgress(ProvisioningProgress progress)
    {
        if (progress.Qr is not null)
            ApplyProvisioningUrl(progress.Qr.Url);

        switch (progress.Kind)
        {
            case ProvisioningProgressKind.QrReady:
            case ProvisioningProgressKind.WaitingForScan:
                IsLinkInProgress = false;
                StatusText = progress.Message ?? "请扫描二维码";
                break;
            case ProvisioningProgressKind.Linked:
                IsLinkInProgress = true;
                StatusText = progress.Message ?? "正在完成关联…";
                break;
            case ProvisioningProgressKind.Failed:
            case ProvisioningProgressKind.Timeout:
                IsLinkInProgress = false;
                StatusText = progress.Message ?? "关联失败";
                break;
            default:
                if (!string.IsNullOrWhiteSpace(progress.Message))
                    StatusText = progress.Message;
                break;
        }
    }

    public async Task ShutdownAsync()
    {
        try
        {
            _eventsCts?.Cancel();
            _eventsCts?.Dispose();
            _eventsCts = null;
            ClearQr();

            // Stop message WebSocket / registration / provisioning before the process exits.
            if (_client is IAsyncDisposable asyncDisposable)
                await asyncDisposable.DisposeAsync();
            else if (_client is IDisposable disposable)
                disposable.Dispose();
        }
        catch
        {
            // Ignore cleanup errors during shutdown.
        }
    }
}

public sealed class ConversationItemViewModel(Conversation model) : ObservableObject
{
    public string Id { get; } = model.Id;
    public string? ServiceId { get; } = model.ServiceId;
    public string Title { get; } = string.IsNullOrWhiteSpace(model.Title) ? (model.ServiceId ?? "Chat") : model.Title;
    public string Preview { get; } = model.LastMessagePreview ?? "尚无消息";
    public long LastMessageAtMs { get; } = model.LastMessageAtMs;
    public int UnreadCount { get; } = model.UnreadCount;
    public bool HasUnread => UnreadCount > 0;
    public string UnreadLabel => UnreadCount > 99 ? "99+" : UnreadCount.ToString();
    public string AvatarInitial
    {
        get
        {
            var t = Title.Trim();
            return t.Length == 0 ? "?" : char.ToUpperInvariant(t[0]).ToString();
        }
    }

    public string TimeLabel => FormatChatTime(LastMessageAtMs);

    internal static string FormatChatTime(long unixMs)
    {
        if (unixMs <= 0)
            return string.Empty;
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(unixMs).ToLocalTime();
        var now = DateTimeOffset.Now;
        var age = now - dt;
        if (age.TotalMinutes < 1)
            return "now";
        if (age.TotalHours < 1)
            return $"{(int)age.TotalMinutes}m";
        if (dt.Date == now.Date)
            return dt.ToString("h:mm tt");
        if (dt.Date == now.Date.AddDays(-1))
            return "Yesterday";
        if (age.TotalDays < 7)
            return dt.ToString("ddd");
        return dt.ToString("M/d/yy");
    }
}

public enum MainNavTab
{
    Chats = 0,
    Calls = 1,
    Stories = 2,
}

public enum NewChatStep
{
    Home = 0,
    CreateGroup = 1,
    FindUsername = 2,
    FindPhone = 3,
}

public sealed class ContactItemViewModel(ContactInfo model) : ObservableObject
{
    public string ServiceId { get; } = model.ServiceId;
    public string DisplayName { get; } = model.ProfileName ?? model.Number ?? model.ServiceId;
    public string Initial
    {
        get
        {
            var t = DisplayName.Trim().TrimStart('@');
            return t.Length == 0 ? "?" : char.ToUpperInvariant(t[0]).ToString();
        }
    }
}

public sealed class MessageItemViewModel : ObservableObject
{
    public MessageItemViewModel(ChatMessage model)
    {
        Id = model.Id;
        Body = model.Body ?? string.Empty;
        IsOutgoing = model.IsOutgoing;
        BubbleBackground = IsOutgoing ? "#3A76F0" : "#E9E9E9";
        BubbleForeground = IsOutgoing ? "#FFFFFF" : "#1B1B1B";
        var time = ConversationItemViewModel.FormatChatTime(model.SentAtMs);
        MetaLabel = IsOutgoing
            ? (string.IsNullOrEmpty(time) ? "✓✓" : $"{time}  ✓✓")
            : time;
    }

    public string Id { get; }
    public string Body { get; }
    public bool IsOutgoing { get; }
    public string BubbleBackground { get; }
    public string BubbleForeground { get; }
    public string MetaLabel { get; }
}
