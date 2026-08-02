using Google.Protobuf;
using SignalCpf.Core.Models;
using SignalCpf.Core.Options;
using SignalCpf.LibSignal;
using SignalCpf.Net.Messaging;
using SignalCpf.Storage;
using Signalservice;

namespace SignalCpf.Client.Handlers;

/// <summary>
/// Authenticated message WebSocket lifecycle and inbound envelope handling.
/// </summary>
internal sealed class MessageSocketHandler : IAsyncDisposable
{
    private readonly SignalServerOptions _options;
    private readonly IMessageStore _messages;
    private readonly ClientState _state;
    private readonly PreKeyManager _preKeys;

    private AuthenticatedMessageSocket? _messageSocket;
    private CancellationTokenSource? _messageLoopCts;

    public MessageSocketHandler(
        SignalServerOptions options,
        IMessageStore messages,
        ClientState state,
        PreKeyManager preKeys)
    {
        _options = options;
        _messages = messages;
        _state = state;
        _preKeys = preKeys;
    }

    public async Task StartAsync(CancellationToken ct = default)
    {
        var (account, _) = _state.Snapshot();
        if (account is null)
            return;

        _messageLoopCts?.Cancel();
        if (_messageSocket is not null)
            await _messageSocket.DisposeAsync();

        _messageSocket = new AuthenticatedMessageSocket(
            _options, account.Aci, account.DeviceId, account.Password);
        _messageLoopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        await _messageSocket.StartAsync(_messageLoopCts.Token);
        _ = ConsumeEnvelopesAsync(_messageLoopCts.Token);
    }

    public async ValueTask DisposeAsync()
    {
        _messageLoopCts?.Cancel();
        if (_messageSocket is not null)
            await _messageSocket.DisposeAsync();
        _messageSocket = null;
    }

    private async Task ConsumeEnvelopesAsync(CancellationToken ct)
    {
        if (_messageSocket is null)
            return;

        try
        {
            await foreach (var incoming in _messageSocket.Envelopes.ReadAllAsync(ct))
            {
                try
                {
                    await HandleEnvelopeAsync(incoming.Envelope, ct);
                }
                catch (Exception ex)
                {
                    await _state.EmitAsync(new SidecarEvent.Error("DECRYPT_FAILED", ex.Message), ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
    }

    private async Task HandleEnvelopeAsync(Envelope envelope, CancellationToken ct)
    {
        var (account, protocol) = _state.Snapshot();
        if (protocol is null || account is null)
            return;

        if (envelope.Type == Envelope.Types.Type.ServerDeliveryReceipt)
            return;

        var sender = envelope.SourceServiceId;
        if (string.IsNullOrEmpty(sender))
            sender = envelope.SourceServiceIdBinary.IsEmpty
                ? null
                : FormatUuid(envelope.SourceServiceIdBinary.Span);

        if (envelope.Content.IsEmpty)
            return;

        // Sealed sender may omit source; DecryptResult fills it in.
        if (string.IsNullOrEmpty(sender) && envelope.Type != Envelope.Types.Type.UnidentifiedSender)
            return;

        var deviceId = (int)envelope.SourceDeviceId;
        DecryptResult decrypted;
        try
        {
            decrypted = await protocol.DecryptAsync(
                sender ?? "",
                deviceId == 0 ? 1 : deviceId,
                (int)envelope.Type,
                envelope.Content.ToByteArray(),
                ct);
        }
        catch (Exception ex)
        {
            await _state.EmitAsync(
                new SidecarEvent.Error("DECRYPT_FAILED",
                    $"type={(int)envelope.Type} from={sender ?? "?"}: {ex.Message}"),
                ct);
            return;
        }

        // PreKey messages consume one-time keys; top up when below waterline.
        _ = _preKeys.EnsureWaterlineAsync(ct);

        sender = decrypted.SenderServiceId ?? sender;
        if (string.IsNullOrEmpty(sender))
            return;

        Content content;
        try
        {
            content = Content.Parser.ParseFrom(decrypted.Plaintext);
        }
        catch
        {
            return;
        }

        if (content.SyncMessage is not null)
        {
            await HandleSyncMessageAsync(content.SyncMessage, account, ct);
            return;
        }

        if (content.DataMessage is null)
            return;

        var body = content.DataMessage.Body;
        var conversationId = sender;
        var title = sender;
        var contacts = await _messages.ListContactsAsync(ct);
        var contact = contacts.FirstOrDefault(c =>
            string.Equals(c.ServiceId, sender, StringComparison.OrdinalIgnoreCase));
        if (contact?.ProfileName is { Length: > 0 })
            title = contact.ProfileName;

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var sentAt = envelope.ClientTimestamp != 0
            ? (long)envelope.ClientTimestamp
            : now;

        var msg = new ChatMessage(
            Id: Guid.NewGuid().ToString("N"),
            ConversationId: conversationId,
            SenderServiceId: sender,
            Body: body,
            SentAtMs: sentAt,
            ReceivedAtMs: now,
            IsOutgoing: false,
            Status: MessageStatus.Delivered);

        await _messages.AddMessageAsync(msg, ct);
        var conv = new Conversation(
            Id: conversationId,
            ServiceId: sender,
            Title: title,
            LastMessagePreview: body,
            LastMessageAtMs: sentAt,
            UnreadCount: 1,
            IsGroup: false);
        await _messages.UpsertConversationAsync(conv, ct);

        await _state.EmitAsync(new SidecarEvent.MessageReceived(msg), ct);
        await _state.EmitAsync(new SidecarEvent.ConversationUpdated(conv), ct);

        if (_state.NotificationsEnabled)
        {
            await _state.EmitAsync(
                new SidecarEvent.Error("NOTIFICATION", $"{title}: {body}"),
                ct);
        }
    }

    private async Task HandleSyncMessageAsync(
        SyncMessage sync,
        AccountCredentials account,
        CancellationToken ct)
    {
        if (sync.Contacts?.Blob is not null)
        {
            await _state.EmitAsync(
                new SidecarEvent.Error("SYNC_CONTACTS", "Contacts sync blob received"),
                ct);
        }

        if (sync.Sent?.Message is not null)
        {
            var destination = sync.Sent.DestinationServiceId;
            if (string.IsNullOrEmpty(destination))
                return;
            var body = sync.Sent.Message.Body;
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var msg = new ChatMessage(
                Id: Guid.NewGuid().ToString("N"),
                ConversationId: destination,
                SenderServiceId: account.Aci,
                Body: body,
                SentAtMs: (long)(sync.Sent.Timestamp == 0 ? (ulong)now : sync.Sent.Timestamp),
                ReceivedAtMs: now,
                IsOutgoing: true,
                Status: MessageStatus.Sent);
            await _messages.AddMessageAsync(msg, ct);
            var conv = new Conversation(
                Id: destination,
                ServiceId: destination,
                Title: destination,
                LastMessagePreview: body,
                LastMessageAtMs: msg.SentAtMs,
                UnreadCount: 0,
                IsGroup: false);
            await _messages.UpsertConversationAsync(conv, ct);
            await _state.EmitAsync(new SidecarEvent.MessageReceived(msg), ct);
            await _state.EmitAsync(new SidecarEvent.ConversationUpdated(conv), ct);
        }
    }

    private static string FormatUuid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            return Convert.ToHexString(bytes).ToLowerInvariant();
        return new Guid(bytes).ToString().ToLowerInvariant();
    }
}
