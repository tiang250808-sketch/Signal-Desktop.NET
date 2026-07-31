using SignalCpf.Core.Models;

namespace SignalCpf.Core.Abstractions;

/// <summary>
/// Desktop Signal client facade. Implemented by SignalClientOrchestrator.
/// </summary>
public interface ISignalSidecarClient : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken cancellationToken = default);

    Task<bool> HealthAsync(CancellationToken cancellationToken = default);

    Task<AccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default);

    Task<ProvisioningQr> StartProvisioningAsync(
        string deviceName,
        CancellationToken cancellationToken = default);

    Task CancelProvisioningAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<Conversation>> ListConversationsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        string conversationId,
        int limit = 50,
        CancellationToken cancellationToken = default);

    Task<ChatMessage> SendTextMessageAsync(
        string? conversationId,
        string? recipientServiceId,
        string body,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<ContactInfo>> ListContactsAsync(
        CancellationToken cancellationToken = default);

    Task UpsertContactAsync(
        ContactInfo contact,
        CancellationToken cancellationToken = default);

    Task<ClientSettings> GetSettingsAsync(CancellationToken cancellationToken = default);

    Task UpdateSettingsAsync(
        ClientSettings settings,
        CancellationToken cancellationToken = default);

    Task SendReadReceiptAsync(
        string conversationId,
        string messageId,
        CancellationToken cancellationToken = default);

    Task<AttachmentInfo?> StageAttachmentAsync(
        string messageId,
        string filePath,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Server-streaming events (inbound messages, provisioning progress, etc.).
    /// </summary>
    IAsyncEnumerable<SidecarEvent> SubscribeEventsAsync(
        CancellationToken cancellationToken = default);
}
