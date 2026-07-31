namespace SignalCpf.Core.Models;

public sealed record AccountStatus(
    bool IsRegistered,
    string? ServiceId = null,
    string? Pni = null,
    int DeviceId = 0,
    string? DeviceName = null,
    string? Number = null);

public sealed record Conversation(
    string Id,
    string? ServiceId,
    string Title,
    string? LastMessagePreview,
    long LastMessageAtMs,
    int UnreadCount,
    bool IsGroup);

public enum MessageStatus
{
    Unspecified = 0,
    Pending = 1,
    Sent = 2,
    Delivered = 3,
    Read = 4,
    Failed = 5,
}

public sealed record ChatMessage(
    string Id,
    string ConversationId,
    string? SenderServiceId,
    string? Body,
    long SentAtMs,
    long ReceivedAtMs,
    bool IsOutgoing,
    MessageStatus Status);

public sealed record ProvisioningQr(
    string Url,
    byte[]? QrPng = null);

public enum ProvisioningProgressKind
{
    Unspecified = 0,
    QrReady = 1,
    WaitingForScan = 2,
    Linked = 3,
    Failed = 4,
    Timeout = 5,
}

public sealed record ProvisioningProgress(
    ProvisioningProgressKind Kind,
    string? Message = null,
    ProvisioningQr? Qr = null);

public abstract record SidecarEvent
{
    public sealed record MessageReceived(ChatMessage Message) : SidecarEvent;
    public sealed record ConversationUpdated(Conversation Conversation) : SidecarEvent;
    public sealed record AccountStatusChanged(AccountStatus Status) : SidecarEvent;
    public sealed record ProvisioningUpdated(ProvisioningProgress Progress) : SidecarEvent;
    public sealed record Error(string Code, string Message) : SidecarEvent;
}
