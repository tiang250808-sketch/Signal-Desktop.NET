using System.Collections.ObjectModel;
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
    private bool _isRegistered;

    [ObservableProperty]
    private string _deviceName = "CPF Desktop";

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

    [ObservableProperty]
    private ConversationItemViewModel? _selectedConversation;

    [ObservableProperty]
    private string _composeText = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsShowingSettings))]
    [NotifyPropertyChangedFor(nameof(IsShowingChatMain))]
    private bool _settingsOpen;

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
    public bool IsShowingChatMain => IsRegistered && !SettingsOpen;
    public bool IsShowingSettings => IsRegistered && SettingsOpen;
    public bool IsShowingQrCard => !IsRegistered && !IsLinkInProgress && !IsQrLoading;

    public ObservableCollection<ConversationItemViewModel> Conversations { get; } = [];
    public ObservableCollection<MessageItemViewModel> Messages { get; } = [];
    public ObservableCollection<ContactItemViewModel> Contacts { get; } = [];

    public MainViewModel(ISignalSidecarClient client)
    {
        _client = client;
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
            else if (reachable || string.Equals(settings.ServerProfile, "Official", StringComparison.OrdinalIgnoreCase))
                await StartProvisioningAsync();
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
            ClearQr();
            StatusText = $"已关联：{account.DeviceName ?? account.ServiceId}";
        }
        else
        {
            StatusText = "未注册 — 请扫描二维码关联设备";
        }
    }

    [RelayCommand]
    private async Task StartProvisioningAsync()
    {
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
        Conversations.Clear();
        var list = await _client.ListConversationsAsync();
        foreach (var c in list)
            Conversations.Add(new ConversationItemViewModel(c));
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
        SettingsSummary =
            $"配置档: {s.ServerProfile}\n服务器: {s.ApiBaseUrl}\nCDN: {s.CdnUrl ?? "(same as API)"}\n" +
            $"Storage: {s.StorageUrl ?? "(unset)"}\n数据目录: {s.DataDirectory}\n设备名: {s.DeviceName}\n" +
            $"TLS 不安全模式: {s.AllowInsecureTls}\nPQ 密钥: {s.EnablePqKeys}\n" +
            $"通知: {s.NotificationsEnabled}\n已读回执: {s.ReadReceiptsEnabled}\n" +
            $"libsignal FFI: {s.UsesNativeLibSignal}";
    }

    [RelayCommand]
    private void ToggleSettings()
    {
        SettingsOpen = !SettingsOpen;
        if (SettingsOpen)
            _ = RefreshSettingsAsync();
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
            return;

        var id = NewContactServiceId.Trim();
        await _client.UpsertContactAsync(new ContactInfo(
            id,
            Number: null,
            ProfileName: string.IsNullOrWhiteSpace(NewContactName) ? id : NewContactName.Trim(),
            About: null));
        NewContactServiceId = string.Empty;
        NewContactName = string.Empty;
        await RefreshContactsAsync();
        await RefreshConversationsAsync();
        StatusText = "联系人已添加";
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
        if (SelectedConversation is null || string.IsNullOrWhiteSpace(ComposeText))
            return;

        var body = ComposeText.Trim();
        ComposeText = string.Empty;
        var sent = await _client.SendTextMessageAsync(
            SelectedConversation.Id,
            SelectedConversation.ServiceId,
            body);
        Messages.Add(new MessageItemViewModel(sent));
        await RefreshConversationsAsync();
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

    public Task ShutdownAsync()
    {
        _eventsCts?.Cancel();
        ClearQr();
        // Lifetime owned by DI ServiceProvider (IAsyncDisposable).
        return Task.CompletedTask;
    }
}

public sealed class ConversationItemViewModel(Conversation model) : ObservableObject
{
    public string Id { get; } = model.Id;
    public string? ServiceId { get; } = model.ServiceId;
    public string Title { get; } = model.Title;
    public string Preview { get; } = model.LastMessagePreview ?? string.Empty;
}

public sealed class ContactItemViewModel(ContactInfo model) : ObservableObject
{
    public string ServiceId { get; } = model.ServiceId;
    public string DisplayName { get; } = model.ProfileName ?? model.Number ?? model.ServiceId;
}

public sealed class MessageItemViewModel : ObservableObject
{
    public MessageItemViewModel(ChatMessage model)
    {
        Id = model.Id;
        Body = model.Body ?? string.Empty;
        IsOutgoing = model.IsOutgoing;
        BubbleBackground = IsOutgoing ? "#2C6BED" : "#E5E7EB";
        BubbleForeground = IsOutgoing ? "#FFFFFF" : "#111827";
    }

    public string Id { get; }
    public string Body { get; }
    public bool IsOutgoing { get; }
    public string BubbleBackground { get; }
    public string BubbleForeground { get; }
}
