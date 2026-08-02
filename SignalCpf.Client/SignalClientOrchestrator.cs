using System.Runtime.CompilerServices;
using Google.Protobuf;
using SignalCpf.Client.Handlers;
using SignalCpf.Core.Abstractions;
using SignalCpf.Core.Models;
using SignalCpf.Core.Options;
using SignalCpf.LibSignal;
using SignalCpf.Net.Http;
using SignalCpf.Storage;
using Signalservice;

namespace SignalCpf.Client;

/// <summary>
/// Thin facade over provisioning, registration, message socket, and PreKey modules.
/// Implements <see cref="ISignalSidecarClient"/> for the UI / DI boundary.
/// </summary>
public sealed class SignalClientOrchestrator : ISignalSidecarClient, IAsyncDisposable, IDisposable
{
    private readonly SignalServerOptions _options;
    private readonly ICredentialStore _credentials;
    private readonly IMessageStore _messages;
    private readonly SignalRestClient _rest;
    private readonly ClientState _state;
    private readonly PreKeyManager _preKeys;
    private readonly MessageSocketHandler _messageSocket;
    private readonly ProvisioningHandler _provisioning;
    private readonly RegistrationHandler _registration;

    private int _disposed;

    public SignalClientOrchestrator(
        SignalServerOptions options,
        ICredentialStore credentials,
        IMessageStore messages,
        SignalRestClient rest)
    {
        _options = options;
        _credentials = credentials;
        _messages = messages;
        _rest = rest;
        _state = new ClientState();
        _preKeys = new PreKeyManager(options, messages, rest, _state);
        _messageSocket = new MessageSocketHandler(options, messages, _state, _preKeys);
        _provisioning = new ProvisioningHandler(
            options, credentials, messages, rest, _state, _preKeys, _messageSocket.StartAsync);
        _registration = new RegistrationHandler(
            options, credentials, messages, rest, _state, _preKeys, _messageSocket.StartAsync);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.DataDirectory);
        await _messages.InitializeAsync(cancellationToken);
        var account = await _credentials.LoadAsync(cancellationToken);
        if (account is not null)
        {
            _rest.SetDeviceAuth(account.Aci, account.DeviceId, account.Password);
            var protocol = SignalProtocolFactory.Create(_messages, account);
            _state.SetAccountAndProtocol(account, protocol);
            await _preKeys.RefreshSenderCertificateAsync(cancellationToken);
            await _preKeys.EnsureWaterlineAsync(cancellationToken);
            _ = _messageSocket.StartAsync(cancellationToken);
        }
    }

    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _rest.HealthAsync(cancellationToken);
        }
        catch
        {
            return false;
        }
    }

    public Task<AccountStatus> GetAccountStatusAsync(CancellationToken cancellationToken = default)
    {
        var (account, _) = _state.Snapshot();
        return Task.FromResult(_credentials.ToAccountStatus(account));
    }

    public Task<ProvisioningQr> StartProvisioningAsync(
        string deviceName,
        CancellationToken cancellationToken = default) =>
        _provisioning.StartAsync(deviceName, cancellationToken);

    public Task CancelProvisioningAsync(CancellationToken cancellationToken = default)
    {
        _provisioning.Cancel();
        return Task.CompletedTask;
    }

    public Task<RegistrationSessionStatus> StartPhoneRegistrationAsync(
        string e164Number,
        string? captchaToken = null,
        string transport = "sms",
        CancellationToken cancellationToken = default) =>
        _registration.StartAsync(e164Number, captchaToken, transport, cancellationToken);

    public Task<RegistrationSessionStatus> SubmitRegistrationCaptchaAsync(
        string captchaToken,
        CancellationToken cancellationToken = default) =>
        _registration.SubmitCaptchaAsync(captchaToken, cancellationToken);

    public Task<RegistrationSessionStatus> RequestRegistrationCodeAsync(
        string transport = "sms",
        CancellationToken cancellationToken = default) =>
        _registration.RequestCodeAsync(transport, cancellationToken);

    public Task<AccountStatus> CompletePhoneRegistrationAsync(
        string verificationCode,
        string deviceName,
        CancellationToken cancellationToken = default) =>
        _registration.CompleteAsync(verificationCode, deviceName, cancellationToken);

    public Task CancelRegistrationAsync(CancellationToken cancellationToken = default)
    {
        _registration.Cancel();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<Conversation>> ListConversationsAsync(
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        _messages.ListConversationsAsync(limit, cancellationToken);

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(
        string conversationId,
        int limit = 50,
        CancellationToken cancellationToken = default) =>
        _messages.GetMessagesAsync(conversationId, limit, cancellationToken);

    public async Task<ChatMessage> SendTextMessageAsync(
        string? conversationId,
        string? recipientServiceId,
        string body,
        CancellationToken cancellationToken = default)
    {
        var (account, protocol) = _state.Snapshot();
        if (account is null)
            throw new InvalidOperationException("Not registered");
        if (protocol is null)
            throw new InvalidOperationException("Protocol not ready");

        var recipient = recipientServiceId ?? conversationId
            ?? throw new ArgumentException("recipientServiceId or conversationId required");

        var content = new Content
        {
            DataMessage = new DataMessage
            {
                Body = body,
                Timestamp = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            },
        };
        var plaintext = content.ToByteArray();
        var senderCert = await _messages.LoadSenderCertificateAsync(cancellationToken);

        var bundle = await _rest.GetPreKeyBundleAsync(recipient, deviceId: -1, cancellationToken)
                     ?? await _rest.GetPreKeyBundleAsync(recipient, deviceId: 1, cancellationToken);

        if (bundle?.Devices is { Count: > 0 } && bundle.IdentityKey is not null)
        {
            var identityKey = Convert.FromBase64String(bundle.IdentityKey);
            var outgoing = new List<OutgoingMessage>();

            foreach (var device in bundle.Devices)
            {
                if (device.SignedPreKey is null)
                    continue;

                await protocol.ProcessPreKeyBundleAsync(recipient, device.DeviceId, new RemotePreKeyBundle
                {
                    IdentityKey = identityKey,
                    RegistrationId = device.RegistrationId,
                    PreKeyId = device.PreKey?.KeyId,
                    PreKeyPublic = device.PreKey is null
                        ? null
                        : Convert.FromBase64String(device.PreKey.PublicKey),
                    SignedPreKeyId = device.SignedPreKey.KeyId,
                    SignedPreKeyPublic = Convert.FromBase64String(device.SignedPreKey.PublicKey),
                    SignedPreKeySignature = Convert.FromBase64String(device.SignedPreKey.Signature),
                    KyberPreKeyId = device.PqPreKey?.KeyId,
                    KyberPreKeyPublic = device.PqPreKey is null
                        ? null
                        : Convert.FromBase64String(device.PqPreKey.PublicKey),
                    KyberPreKeySignature = device.PqPreKey is null
                        ? null
                        : Convert.FromBase64String(device.PqPreKey.Signature),
                }, cancellationToken);

                var encrypted = await protocol.EncryptAsync(
                    recipient, device.DeviceId, plaintext, senderCert, cancellationToken);

                outgoing.Add(new OutgoingMessage
                {
                    Type = encrypted.Type,
                    DestinationDeviceId = device.DeviceId,
                    DestinationRegistrationId = encrypted.RegistrationId != 0
                        ? encrypted.RegistrationId
                        : device.RegistrationId,
                    Content = Convert.ToBase64String(encrypted.Ciphertext),
                });
            }

            if (outgoing.Count > 0)
            {
                await _rest.SendMessagesAsync(recipient, new OutgoingMessageRequest
                {
                    Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                    Online = false,
                    Urgent = true,
                    Messages = outgoing,
                }, ct: cancellationToken);
            }
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var convId = conversationId ?? recipient;
        var msg = new ChatMessage(
            Id: Guid.NewGuid().ToString("N"),
            ConversationId: convId,
            SenderServiceId: account.Aci,
            Body: body,
            SentAtMs: now,
            ReceivedAtMs: now,
            IsOutgoing: true,
            Status: MessageStatus.Sent);

        await _messages.AddMessageAsync(msg, cancellationToken);
        var conv = new Conversation(
            Id: convId,
            ServiceId: recipient,
            Title: recipient,
            LastMessagePreview: body,
            LastMessageAtMs: now,
            UnreadCount: 0,
            IsGroup: false);
        await _messages.UpsertConversationAsync(conv, cancellationToken);
        await _state.EmitAsync(new SidecarEvent.ConversationUpdated(conv), cancellationToken);
        return msg;
    }

    public async Task<IReadOnlyList<ContactInfo>> ListContactsAsync(
        CancellationToken cancellationToken = default)
    {
        var list = await _messages.ListContactsAsync(cancellationToken);
        return list.Select(c => new ContactInfo(c.ServiceId, c.Number, c.ProfileName, c.About)).ToList();
    }

    public async Task UpsertContactAsync(ContactInfo contact, CancellationToken cancellationToken = default)
    {
        await _messages.UpsertContactAsync(new Storage.ContactRecord
        {
            ServiceId = contact.ServiceId,
            Number = contact.Number,
            ProfileName = contact.ProfileName,
            About = contact.About,
        }, cancellationToken);

        var conv = new Conversation(
            Id: contact.ServiceId,
            ServiceId: contact.ServiceId,
            Title: contact.ProfileName ?? contact.Number ?? contact.ServiceId,
            LastMessagePreview: null,
            LastMessageAtMs: DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            UnreadCount: 0,
            IsGroup: false);
        await _messages.UpsertConversationAsync(conv, cancellationToken);
        await _state.EmitAsync(new SidecarEvent.ConversationUpdated(conv), cancellationToken);
    }

    public Task<ClientSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var (account, protocol) = _state.Snapshot();
        var settings = new ClientSettings(
            ApiBaseUrl: _options.ApiBaseUrl,
            DataDirectory: _options.DataDirectory,
            DeviceName: account?.DeviceName ?? _options.DeviceName,
            AllowInsecureTls: _options.AllowInsecureTls,
            EnablePqKeys: _options.EnablePqKeys,
            NotificationsEnabled: _state.NotificationsEnabled,
            ReadReceiptsEnabled: account?.ReadReceipts ?? true,
            UsesNativeLibSignal: protocol?.UsesNativeFfi
                ?? LibSignal.Native.LibSignalNative.IsAvailable,
            ServerProfile: _options.Profile.ToString(),
            CdnUrl: _options.CdnUrl,
            StorageUrl: _options.StorageUrl,
            ChallengeUrl: _options.ChallengeUrl);
        return Task.FromResult(settings);
    }

    public Task UpdateSettingsAsync(ClientSettings settings, CancellationToken cancellationToken = default)
    {
        _state.NotificationsEnabled = settings.NotificationsEnabled;
        _options.AllowInsecureTls = settings.AllowInsecureTls && !_options.IsOfficialLike;
        _options.EnablePqKeys = settings.EnablePqKeys;
        if (!string.IsNullOrWhiteSpace(settings.ApiBaseUrl) && !_options.IsOfficialLike)
            _options.ApiBaseUrl = settings.ApiBaseUrl;
        if (!string.IsNullOrWhiteSpace(settings.DeviceName))
            _options.DeviceName = settings.DeviceName;
        return Task.CompletedTask;
    }

    public async Task SendReadReceiptAsync(
        string conversationId,
        string messageId,
        CancellationToken cancellationToken = default)
    {
        var msgs = await _messages.GetMessagesAsync(conversationId, 200, cancellationToken);
        var target = msgs.FirstOrDefault(m => m.Id == messageId);
        if (target is null)
            return;

        var updated = target with { Status = MessageStatus.Read };
        await _messages.AddMessageAsync(updated, cancellationToken);
    }

    public async Task<AttachmentInfo?> StageAttachmentAsync(
        string messageId,
        string filePath,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(filePath))
            return null;

        var bytes = await File.ReadAllBytesAsync(filePath, cancellationToken);
        var attachDir = Path.Combine(_options.DataDirectory, "attachments");
        Directory.CreateDirectory(attachDir);
        var id = Guid.NewGuid().ToString("N");
        var dest = Path.Combine(attachDir, id + Path.GetExtension(filePath));
        await File.WriteAllBytesAsync(dest, bytes, cancellationToken);

        var record = new AttachmentRecord
        {
            Id = id,
            MessageId = messageId,
            FileName = Path.GetFileName(filePath),
            ContentType = "application/octet-stream",
            Size = bytes.Length,
            LocalPath = dest,
        };
        await _messages.SaveAttachmentMetaAsync(record, cancellationToken);
        await _rest.UploadAttachmentAsync(bytes, record.ContentType!, cancellationToken);

        return new AttachmentInfo(
            record.Id, record.MessageId, record.FileName, record.ContentType, record.Size, record.LocalPath);
    }

    public async IAsyncEnumerable<SidecarEvent> SubscribeEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var ev in _state.Events.Reader.ReadAllAsync(cancellationToken))
            yield return ev;
    }

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _provisioning.Cancel();
        _registration.Cancel();
        await _messageSocket.DisposeAsync();
        if (_messages is IAsyncDisposable d)
            await d.DisposeAsync();
        else if (_messages is IDisposable sync)
            sync.Dispose();
        _rest.Dispose();
        _state.Events.Writer.TryComplete();
    }
}
