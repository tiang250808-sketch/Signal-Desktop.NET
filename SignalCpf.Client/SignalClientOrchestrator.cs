using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Channels;
using Google.Protobuf;
using SignalCpf.Core.Abstractions;
using SignalCpf.Core.Models;
using SignalCpf.Core.Options;
using SignalCpf.LibSignal;
using SignalCpf.Net.Http;
using SignalCpf.Net.Messaging;
using SignalCpf.Net.Provisioning;
using SignalCpf.Protocol.Crypto;
using SignalCpf.Protocol.Provisioning;
using SignalCpf.Storage;
using Signalservice;

namespace SignalCpf.Client;

/// <summary>
/// Real in-process Signal client: provisioning WS, linkDevice, credentials,
/// authenticated message WS, session crypto, SQLite persistence.
/// </summary>
public sealed class SignalClientOrchestrator : ISignalSidecarClient, IDisposable
{
    private readonly SignalServerOptions _options;
    private readonly ICredentialStore _credentials;
    private readonly IMessageStore _messages;
    private readonly SignalRestClient _rest;
    private readonly Channel<SidecarEvent> _events = Channel.CreateUnbounded<SidecarEvent>();

    private readonly object _gate = new();
    private AccountCredentials? _account;
    private ISignalProtocolService? _protocol;
    private AuthenticatedMessageSocket? _messageSocket;
    private CancellationTokenSource? _provisionCts;
    private CancellationTokenSource? _messageLoopCts;
    private bool _notificationsEnabled = true;

    private string? _registrationSessionId;
    private string? _registrationNumber;
    private string _registrationTransport = "sms";

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
    }

    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_options.DataDirectory);
        await _messages.InitializeAsync(cancellationToken);
        _account = await _credentials.LoadAsync(cancellationToken);
        if (_account is not null)
        {
            _rest.SetDeviceAuth(_account.Aci, _account.DeviceId, _account.Password);
            _protocol = SignalProtocolFactory.Create(_messages, _account);
            await RefreshSenderCertificateAsync(cancellationToken);
            await EnsurePreKeyWaterlineAsync(cancellationToken);
            _ = StartMessageSocketAsync(cancellationToken);
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
        lock (_gate)
            return Task.FromResult(_credentials.ToAccountStatus(_account));
    }

    public async Task<ProvisioningQr> StartProvisioningAsync(
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        _provisionCts?.Cancel();
        _provisionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _provisionCts.Token;

        var cipher = new ProvisioningCipher();
        var pubKey = cipher.GetPublicKeyBase64();

        await EmitProvisioningAsync(
            ProvisioningProgressKind.WaitingForScan,
            $"Connecting provisioning socket ({_options.ApiBaseUrl})…",
            null,
            ct);

        // Run provisioning on background so QR can be emitted when address arrives.
        ProvisioningQr? qrHolder = null;
        var tcsQr = new TaskCompletionSource<ProvisioningQr>(TaskCreationOptions.RunContinuationsAsynchronously);

        _ = Task.Run(async () =>
        {
            try
            {
                await using var socket = new ProvisioningSocket(_options);
                var result = await socket.RunAsync(async address =>
                {
                    var url = LinkDeviceUrl.Build(address, pubKey);
                    var qr = new ProvisioningQr(Url: url);
                    qrHolder = qr;
                    tcsQr.TrySetResult(qr);
                    await EmitProvisioningAsync(
                        ProvisioningProgressKind.QrReady,
                        "Scan this QR with your primary Signal device",
                        qr,
                        ct);
                }, ct);

                await EmitProvisioningAsync(
                    ProvisioningProgressKind.WaitingForScan,
                    "Received provision envelope — linking device…",
                    qrHolder,
                    ct);

                var decrypted = cipher.Decrypt(result.EnvelopeBytes);
                await CompleteLinkAsync(decrypted, deviceName, ct);
            }
            catch (OperationCanceledException)
            {
                tcsQr.TrySetCanceled(ct);
            }
            catch (Exception ex)
            {
                tcsQr.TrySetException(ex);
                await EmitProvisioningAsync(
                    ProvisioningProgressKind.Failed,
                    ex.Message,
                    null,
                    CancellationToken.None);
                await _events.Writer.WriteAsync(
                    new SidecarEvent.Error("PROVISIONING_FAILED", ex.Message),
                    CancellationToken.None);
            }
        }, CancellationToken.None);

        // Wait until QR is ready (or failure)
        return await tcsQr.Task.WaitAsync(ct);
    }

    private async Task CompleteLinkAsync(
        ProvisionDecryptResult decrypted,
        string deviceName,
        CancellationToken ct)
    {
        var password = GeneratePassword();
        var registrationId = RandomNumberGenerator.GetInt32(1, 0x3FFF);
        var pniRegistrationId = RandomNumberGenerator.GetInt32(1, 0x3FFF);
        var normalizedName = LinkDeviceUrl.NormalizeDeviceName(
            string.IsNullOrWhiteSpace(deviceName) ? _options.DeviceName : deviceName);
        if (normalizedName.Length > 50)
            normalizedName = normalizedName[..50];

        var profileKey = decrypted.ProfileKey ?? ProvisioningCryptoRandom(32);
        var encryptedName = DeviceNameCipher.EncryptToBase64(normalizedName, profileKey);

        var pending = new AccountCredentials
        {
            Aci = decrypted.Aci,
            Pni = decrypted.Pni,
            Number = decrypted.Number,
            DeviceName = normalizedName,
            Password = password,
            RegistrationId = registrationId,
            PniRegistrationId = pniRegistrationId,
            AciIdentityPrivateKey = decrypted.AciKeyPair.PrivateKey,
            AciIdentityPublicKey = decrypted.AciKeyPair.SerializePublicKey(),
            PniIdentityPrivateKey = decrypted.PniKeyPair?.PrivateKey,
            PniIdentityPublicKey = decrypted.PniKeyPair?.SerializePublicKey(),
            ProfileKey = profileKey,
            MasterKey = decrypted.MasterKey,
            AccountEntropyPool = decrypted.AccountEntropyPool,
            MediaRootBackupKey = decrypted.MediaRootBackupKey,
            ReadReceipts = decrypted.ReadReceipts,
        };

        // Temporary protocol instance to generate keys before deviceId is known.
        var protocol = SignalProtocolFactory.Create(_messages, pending);
        var keys = await protocol.GenerateDeviceKeysAsync(pending, _options.EnablePqKeys, ct);

        _rest.SetLinkAuth(pending.Aci, password);
        var linkReq = new LinkDeviceRequest
        {
            VerificationCode = decrypted.ProvisioningCode
                ?? throw new InvalidOperationException("Missing provisioningCode"),
            AccountAttributes = new AccountAttributes
            {
                FetchesMessages = true,
                RegistrationId = registrationId,
                PniRegistrationId = pniRegistrationId,
                Name = encryptedName,
            },
            AciSignedPreKey = ToSignedEntity(keys.AciSignedPreKey),
            PniSignedPreKey = ToSignedEntity(keys.PniSignedPreKey),
            AciPqLastResortPreKey = keys.AciPqLastResortPreKey is null
                ? null
                : ToKyberEntity(keys.AciPqLastResortPreKey),
            PniPqLastResortPreKey = keys.PniPqLastResortPreKey is null
                ? null
                : ToKyberEntity(keys.PniPqLastResortPreKey),
        };

        var linkResp = await _rest.LinkDeviceAsync(linkReq, ct);
        pending.DeviceId = linkResp.DeviceId;
        pending.LinkedAt = DateTimeOffset.UtcNow;

        await _credentials.SaveAsync(pending, ct);
        lock (_gate)
        {
            _account = pending;
            _protocol = SignalProtocolFactory.Create(_messages, pending);
        }

        _rest.SetDeviceAuth(pending.Aci, pending.DeviceId, pending.Password);

        // Upload one-time prekeys (ACI + PNI)
        await TryRegisterPreKeysAsync("v2/keys?identity=aci", keys.AciSignedPreKey, keys.OneTimePreKeys,
            keys.AciPqLastResortPreKey, keys.OneTimeKyberPreKeys, ct);
        await TryRegisterPreKeysAsync("v2/keys?identity=pni", keys.PniSignedPreKey, keys.OneTimePreKeys,
            keys.PniPqLastResortPreKey, [], ct);

        await RefreshSenderCertificateAsync(ct);

        await EmitProvisioningAsync(
            ProvisioningProgressKind.Linked,
            $"Linked as device {pending.DeviceId}",
            null,
            ct);

        var status = _credentials.ToAccountStatus(pending);
        await _events.Writer.WriteAsync(new SidecarEvent.AccountStatusChanged(status), ct);
        _ = StartMessageSocketAsync(ct);
    }

    private async Task StartMessageSocketAsync(CancellationToken ct)
    {
        AccountCredentials? account;
        lock (_gate) account = _account;
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
                    await _events.Writer.WriteAsync(
                        new SidecarEvent.Error("DECRYPT_FAILED", ex.Message), ct);
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
        var protocol = _protocol;
        var account = _account;
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
            await _events.Writer.WriteAsync(
                new SidecarEvent.Error("DECRYPT_FAILED",
                    $"type={(int)envelope.Type} from={sender ?? "?"}: {ex.Message}"),
                ct);
            return;
        }

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
            await HandleSyncMessageAsync(content.SyncMessage, ct);
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

        await _events.Writer.WriteAsync(new SidecarEvent.MessageReceived(msg), ct);
        await _events.Writer.WriteAsync(new SidecarEvent.ConversationUpdated(conv), ct);

        if (_notificationsEnabled)
        {
            await _events.Writer.WriteAsync(
                new SidecarEvent.Error("NOTIFICATION", $"{title}: {body}"),
                ct);
        }
    }

    private async Task HandleSyncMessageAsync(SyncMessage sync, CancellationToken ct)
    {
        if (sync.Contacts?.Blob is not null)
        {
            // Contact sync blob parsing is binary; create placeholder contact update event.
            await _events.Writer.WriteAsync(
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
                SenderServiceId: _account?.Aci,
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
            await _events.Writer.WriteAsync(new SidecarEvent.MessageReceived(msg), ct);
            await _events.Writer.WriteAsync(new SidecarEvent.ConversationUpdated(conv), ct);
        }
    }

    public Task CancelProvisioningAsync(CancellationToken cancellationToken = default)
    {
        _provisionCts?.Cancel();
        return Task.CompletedTask;
    }

    public async Task<RegistrationSessionStatus> StartPhoneRegistrationAsync(
        string e164Number,
        string? captchaToken = null,
        string transport = "sms",
        CancellationToken cancellationToken = default)
    {
        if (_options.IsOfficialLike && !LibSignal.Native.LibSignalNative.IsAvailable)
        {
            throw new InvalidOperationException(
                "官方/Staging 服务器注册需要 libsignal FFI。请先运行 scripts/build-libsignal-ffi.ps1。");
        }

        var number = NormalizeE164(e164Number);
        _registrationNumber = number;
        _registrationTransport = NormalizeTransport(transport);
        _registrationSessionId = null;

        try
        {
            var session = await _rest.CreateVerificationSessionAsync(
                new CreateVerificationSessionRequest
                {
                    Number = number,
                    Captcha = NormalizeCaptcha(captchaToken),
                },
                cancellationToken);

            return await AdvanceRegistrationSessionAsync(session, requestCode: true, cancellationToken);
        }
        catch (SignalApiException ex)
        {
            var status = MapRegistrationApiError(ex);
            await EmitRegistrationAsync(status, cancellationToken);
            throw new InvalidOperationException(status.Message, ex);
        }
    }

    public async Task<RegistrationSessionStatus> SubmitRegistrationCaptchaAsync(
        string captchaToken,
        CancellationToken cancellationToken = default)
    {
        var sessionId = _registrationSessionId
            ?? throw new InvalidOperationException("尚未创建验证会话，请先输入手机号开始注册");
        var token = NormalizeCaptcha(captchaToken)
            ?? throw new ArgumentException("Captcha token 不能为空", nameof(captchaToken));

        try
        {
            var session = await _rest.UpdateVerificationSessionAsync(
                sessionId,
                new UpdateVerificationSessionRequest { Captcha = token },
                cancellationToken);
            return await AdvanceRegistrationSessionAsync(session, requestCode: true, cancellationToken);
        }
        catch (SignalApiException ex)
        {
            var status = MapRegistrationApiError(ex);
            await EmitRegistrationAsync(status, cancellationToken);
            throw new InvalidOperationException(status.Message, ex);
        }
    }

    public async Task<RegistrationSessionStatus> RequestRegistrationCodeAsync(
        string transport = "sms",
        CancellationToken cancellationToken = default)
    {
        var sessionId = _registrationSessionId
            ?? throw new InvalidOperationException("尚未创建验证会话，请先输入手机号开始注册");
        _registrationTransport = NormalizeTransport(transport);

        try
        {
            var session = await _rest.RequestVerificationCodeAsync(
                sessionId,
                new VerificationCodeRequest
                {
                    Transport = _registrationTransport,
                    Client = "ios",
                },
                cancellationToken);
            return await AdvanceRegistrationSessionAsync(session, requestCode: false, cancellationToken);
        }
        catch (SignalApiException ex)
        {
            var status = MapRegistrationApiError(ex);
            await EmitRegistrationAsync(status, cancellationToken);
            throw new InvalidOperationException(status.Message, ex);
        }
    }

    public async Task<AccountStatus> CompletePhoneRegistrationAsync(
        string verificationCode,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var sessionId = _registrationSessionId
            ?? throw new InvalidOperationException("尚未创建验证会话，请先输入手机号开始注册");
        var number = _registrationNumber
            ?? throw new InvalidOperationException("缺少注册手机号");

        if (_options.IsOfficialLike && !LibSignal.Native.LibSignalNative.IsAvailable)
        {
            throw new InvalidOperationException(
                "官方/Staging 服务器注册需要 libsignal FFI。请先运行 scripts/build-libsignal-ffi.ps1。");
        }

        var code = NormalizeVerificationCode(verificationCode);
        try
        {
            var verified = await _rest.SubmitVerificationCodeAsync(
                sessionId,
                new SubmitVerificationCodeRequest { Code = code },
                cancellationToken);

            if (!verified.Verified)
            {
                var waiting = ToRegistrationStatus(
                    verified,
                    RegistrationProgressKind.CodeRequested,
                    "验证码不正确或尚未生效，请重试");
                await EmitRegistrationAsync(waiting, cancellationToken);
                throw new InvalidOperationException(waiting.Message);
            }

            await EmitRegistrationAsync(
                ToRegistrationStatus(verified, RegistrationProgressKind.Verified, "手机号已验证，正在创建账户…"),
                cancellationToken);

            var status = await FinishPrimaryRegistrationAsync(sessionId, number, deviceName, cancellationToken);
            ClearRegistrationState();
            return status;
        }
        catch (SignalApiException ex)
        {
            var mapped = MapRegistrationApiError(ex);
            await EmitRegistrationAsync(mapped, cancellationToken);
            throw new InvalidOperationException(mapped.Message, ex);
        }
    }

    public Task CancelRegistrationAsync(CancellationToken cancellationToken = default)
    {
        ClearRegistrationState();
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
        var account = _account ?? throw new InvalidOperationException("Not registered");
        var protocol = _protocol ?? throw new InvalidOperationException("Protocol not ready");
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

        // Fetch all devices (*); fall back to device 1.
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
        await _events.Writer.WriteAsync(new SidecarEvent.ConversationUpdated(conv), cancellationToken);
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
        await _events.Writer.WriteAsync(new SidecarEvent.ConversationUpdated(conv), cancellationToken);
    }

    public Task<ClientSettings> GetSettingsAsync(CancellationToken cancellationToken = default)
    {
        var settings = new ClientSettings(
            ApiBaseUrl: _options.ApiBaseUrl,
            DataDirectory: _options.DataDirectory,
            DeviceName: _account?.DeviceName ?? _options.DeviceName,
            AllowInsecureTls: _options.AllowInsecureTls,
            EnablePqKeys: _options.EnablePqKeys,
            NotificationsEnabled: _notificationsEnabled,
            ReadReceiptsEnabled: _account?.ReadReceipts ?? true,
            UsesNativeLibSignal: _protocol?.UsesNativeFfi
                ?? SignalCpf.LibSignal.Native.LibSignalNative.IsAvailable,
            ServerProfile: _options.Profile.ToString(),
            CdnUrl: _options.CdnUrl,
            StorageUrl: _options.StorageUrl,
            ChallengeUrl: _options.ChallengeUrl);
        return Task.FromResult(settings);
    }

    public Task UpdateSettingsAsync(ClientSettings settings, CancellationToken cancellationToken = default)
    {
        _notificationsEnabled = settings.NotificationsEnabled;
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
        // Receipts are sent as sync/receipt DataMessages; mark local message read for UI.
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
        // CDN upload hook
        await _rest.UploadAttachmentAsync(bytes, record.ContentType!, cancellationToken);

        return new AttachmentInfo(
            record.Id, record.MessageId, record.FileName, record.ContentType, record.Size, record.LocalPath);
    }

    public async IAsyncEnumerable<SidecarEvent> SubscribeEventsAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var ev in _events.Reader.ReadAllAsync(cancellationToken))
            yield return ev;
    }

    private int _disposed;

    public void Dispose() =>
        DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _provisionCts?.Cancel();
        _messageLoopCts?.Cancel();
        ClearRegistrationState();
        if (_messageSocket is not null)
            await _messageSocket.DisposeAsync();
        if (_messages is IAsyncDisposable d)
            await d.DisposeAsync();
        else if (_messages is IDisposable sync)
            sync.Dispose();
        _rest.Dispose();
        _events.Writer.TryComplete();
    }

    private async Task EmitProvisioningAsync(
        ProvisioningProgressKind kind,
        string message,
        ProvisioningQr? qr,
        CancellationToken ct)
    {
        await _events.Writer.WriteAsync(
            new SidecarEvent.ProvisioningUpdated(new ProvisioningProgress(kind, message, qr)),
            ct);
    }

    private async Task EmitRegistrationAsync(RegistrationSessionStatus status, CancellationToken ct)
    {
        await _events.Writer.WriteAsync(new SidecarEvent.RegistrationUpdated(status), ct);
    }

    private async Task<RegistrationSessionStatus> AdvanceRegistrationSessionAsync(
        VerificationSessionResponse session,
        bool requestCode,
        CancellationToken ct)
    {
        _registrationSessionId = session.Id;

        if (session.Verified)
        {
            var verified = ToRegistrationStatus(
                session,
                RegistrationProgressKind.Verified,
                "手机号已验证，请提交以完成注册");
            await EmitRegistrationAsync(verified, ct);
            return verified;
        }

        var requested = session.RequestedInformation ?? [];
        var needsCaptcha = requested.Any(i =>
            string.Equals(i, "captcha", StringComparison.OrdinalIgnoreCase));
        var needsPush = requested.Any(i =>
            string.Equals(i, "pushChallenge", StringComparison.OrdinalIgnoreCase));

        if (needsPush && !needsCaptcha && !session.AllowedToRequestCode)
        {
            var pushBlocked = ToRegistrationStatus(
                session,
                RegistrationProgressKind.Failed,
                "服务器要求 push challenge，桌面端无法完成。请改用自建服务器，或提供 captcha。");
            await EmitRegistrationAsync(pushBlocked, ct);
            return pushBlocked;
        }

        if (needsCaptcha && !session.AllowedToRequestCode)
        {
            var captchaNeeded = ToRegistrationStatus(
                session,
                RegistrationProgressKind.CaptchaRequired,
                "需要完成 Captcha 后才能获取验证码");
            await EmitRegistrationAsync(captchaNeeded, ct);
            return captchaNeeded;
        }

        if (requestCode && session.AllowedToRequestCode)
        {
            session = await _rest.RequestVerificationCodeAsync(
                session.Id,
                new VerificationCodeRequest
                {
                    Transport = _registrationTransport,
                    Client = "ios",
                },
                ct);
            _registrationSessionId = session.Id;

            requested = session.RequestedInformation ?? [];
            needsCaptcha = requested.Any(i =>
                string.Equals(i, "captcha", StringComparison.OrdinalIgnoreCase));
            if (needsCaptcha && !session.AllowedToRequestCode && !session.Verified)
            {
                var captchaNeeded = ToRegistrationStatus(
                    session,
                    RegistrationProgressKind.CaptchaRequired,
                    "需要完成 Captcha 后才能获取验证码");
                await EmitRegistrationAsync(captchaNeeded, ct);
                return captchaNeeded;
            }
        }

        var kind = session.AllowedToRequestCode || !string.IsNullOrEmpty(session.Id)
            ? RegistrationProgressKind.CodeRequested
            : RegistrationProgressKind.SessionCreated;
        var message = kind == RegistrationProgressKind.CodeRequested
            ? $"验证码已发送至 {_registrationNumber}（{_registrationTransport}）"
            : "验证会话已创建";
        if (!session.AllowedToRequestCode && needsCaptcha)
        {
            kind = RegistrationProgressKind.CaptchaRequired;
            message = "需要完成 Captcha 后才能获取验证码";
        }

        var status = ToRegistrationStatus(session, kind, message);
        await EmitRegistrationAsync(status, ct);
        return status;
    }

    private async Task<AccountStatus> FinishPrimaryRegistrationAsync(
        string sessionId,
        string number,
        string deviceName,
        CancellationToken ct)
    {
        var password = GeneratePassword();
        var registrationId = RandomNumberGenerator.GetInt32(1, 0x3FFF);
        var pniRegistrationId = RandomNumberGenerator.GetInt32(1, 0x3FFF);
        var normalizedName = LinkDeviceUrl.NormalizeDeviceName(
            string.IsNullOrWhiteSpace(deviceName) ? _options.DeviceName : deviceName);
        if (normalizedName.Length > 50)
            normalizedName = normalizedName[..50];

        var aciKeyPair = Curve25519.GenerateKeyPair();
        var pniKeyPair = Curve25519.GenerateKeyPair();
        var profileKey = ProvisioningCryptoRandom(32);
        var uak = DeriveUnidentifiedAccessKey(profileKey);

        var pending = new AccountCredentials
        {
            Aci = "", // filled from registration response
            Pni = "",
            Number = number,
            DeviceId = 1,
            DeviceName = normalizedName,
            Password = password,
            RegistrationId = registrationId,
            PniRegistrationId = pniRegistrationId,
            AciIdentityPrivateKey = aciKeyPair.PrivateKey,
            AciIdentityPublicKey = aciKeyPair.SerializePublicKey(),
            PniIdentityPrivateKey = pniKeyPair.PrivateKey,
            PniIdentityPublicKey = pniKeyPair.SerializePublicKey(),
            ProfileKey = profileKey,
            ReadReceipts = true,
        };

        var protocol = SignalProtocolFactory.Create(_messages, pending);
        var keys = await protocol.GenerateDeviceKeysAsync(pending, enablePq: true, ct);
        if (keys.AciPqLastResortPreKey is null || keys.PniPqLastResortPreKey is null)
        {
            throw new InvalidOperationException(
                "注册需要 PQ（Kyber）last-resort 预密钥。官方服务器请启用 libsignal FFI。");
        }

        _rest.SetNumberAuth(number, password);
        AccountCreationResponse created;
        try
        {
            created = await _rest.RegisterAccountAsync(new RegistrationRequest
            {
                SessionId = sessionId,
                SkipDeviceTransfer = true,
                AciIdentityKey = Convert.ToBase64String(aciKeyPair.SerializePublicKey()),
                PniIdentityKey = Convert.ToBase64String(pniKeyPair.SerializePublicKey()),
                AccountAttributes = new RegistrationAccountAttributes
                {
                    FetchesMessages = true,
                    RegistrationId = registrationId,
                    PniRegistrationId = pniRegistrationId,
                    Name = null,
                    Capabilities = new Dictionary<string, bool>
                    {
                        ["spqr"] = true,
                        ["storage"] = true,
                    },
                    UnidentifiedAccessKey = Convert.ToBase64String(uak),
                    UnrestrictedUnidentifiedAccess = false,
                    DiscoverableByPhoneNumber = true,
                },
                AciSignedPreKey = ToSignedEntity(keys.AciSignedPreKey),
                PniSignedPreKey = ToSignedEntity(keys.PniSignedPreKey),
                AciPqLastResortPreKey = ToKyberEntity(keys.AciPqLastResortPreKey),
                PniPqLastResortPreKey = ToKyberEntity(keys.PniPqLastResortPreKey),
            }, ct);
        }
        catch (SignalApiException ex) when (ex.StatusCode == 423)
        {
            throw new InvalidOperationException(
                "该号码已启用 Registration Lock（PIN）。本客户端暂不支持 PIN 解锁注册。", ex);
        }
        catch (SignalApiException ex) when (ex.StatusCode == 409)
        {
            throw new InvalidOperationException(
                "该账户可从其他设备迁移数据。本客户端以 skipDeviceTransfer 重新注册失败，请确认服务器响应。",
                ex);
        }

        var aci = created.Uuid
            ?? throw new InvalidOperationException("注册响应缺少 uuid");
        var pni = created.Pni ?? "";
        pending.Aci = SignalAuth.NormalizeAci(aci);
        pending.Pni = string.IsNullOrWhiteSpace(pni) ? "" : SignalAuth.NormalizeAci(pni);
        pending.Number = created.Number ?? number;
        pending.DeviceId = 1;
        pending.LinkedAt = DateTimeOffset.UtcNow;

        await _credentials.SaveAsync(pending, ct);
        lock (_gate)
        {
            _account = pending;
            _protocol = SignalProtocolFactory.Create(_messages, pending);
        }

        _rest.SetDeviceAuth(pending.Aci, pending.DeviceId, pending.Password);

        await TryRegisterPreKeysAsync(
            "v2/keys?identity=aci",
            keys.AciSignedPreKey,
            keys.OneTimePreKeys,
            keys.AciPqLastResortPreKey,
            keys.OneTimeKyberPreKeys,
            ct);
        await TryRegisterPreKeysAsync(
            "v2/keys?identity=pni",
            keys.PniSignedPreKey,
            keys.OneTimePreKeys,
            keys.PniPqLastResortPreKey,
            [],
            ct);

        await RefreshSenderCertificateAsync(ct);

        var accountStatus = _credentials.ToAccountStatus(pending);
        await EmitRegistrationAsync(
            new RegistrationSessionStatus(
                RegistrationProgressKind.Registered,
                $"已注册为主设备：{pending.Number ?? pending.Aci}",
                SessionId: sessionId,
                Number: pending.Number,
                CaptchaRequired: false,
                AllowedToRequestCode: false,
                Verified: true,
                ChallengeUrl: _options.ChallengeUrl),
            ct);
        await _events.Writer.WriteAsync(new SidecarEvent.AccountStatusChanged(accountStatus), ct);
        _ = StartMessageSocketAsync(ct);
        return accountStatus;
    }

    private RegistrationSessionStatus ToRegistrationStatus(
        VerificationSessionResponse session,
        RegistrationProgressKind kind,
        string message)
    {
        var requested = session.RequestedInformation ?? [];
        var captchaRequired = requested.Any(i =>
            string.Equals(i, "captcha", StringComparison.OrdinalIgnoreCase));
        return new RegistrationSessionStatus(
            kind,
            message,
            SessionId: session.Id,
            Number: _registrationNumber,
            CaptchaRequired: captchaRequired || kind == RegistrationProgressKind.CaptchaRequired,
            AllowedToRequestCode: session.AllowedToRequestCode,
            Verified: session.Verified,
            ChallengeUrl: _options.ChallengeUrl);
    }

    private RegistrationSessionStatus MapRegistrationApiError(SignalApiException ex)
    {
        var message = ex.StatusCode switch
        {
            423 => "该号码已启用 Registration Lock（PIN），本客户端暂不支持。",
            429 => "请求过于频繁，请稍后重试。",
            401 => "验证会话未通过，请重新开始注册。",
            403 => "Captcha 或验证被拒绝，请重试。",
            404 => "验证会话不存在或已过期，请重新开始注册。",
            422 => "请求参数无效，请检查手机号格式（E.164，如 +8613812345678）。",
            440 => "短信/语音通道拒绝投递验证码，请换通道或稍后再试。",
            499 => "服务器要求更新的客户端能力（含 PQ）。请确认已构建 libsignal FFI。",
            _ => $"注册失败（HTTP {ex.StatusCode}）：{ex.Message}",
        };

        return new RegistrationSessionStatus(
            RegistrationProgressKind.Failed,
            message,
            SessionId: _registrationSessionId,
            Number: _registrationNumber,
            CaptchaRequired: false,
            AllowedToRequestCode: false,
            Verified: false,
            ChallengeUrl: _options.ChallengeUrl);
    }

    private void ClearRegistrationState()
    {
        _registrationSessionId = null;
        _registrationNumber = null;
        _registrationTransport = "sms";
    }

    private static string NormalizeE164(string raw)
    {
        var n = raw.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (!n.StartsWith('+'))
            throw new ArgumentException("手机号须为 E.164 格式（以 + 开头，如 +8613812345678）", nameof(raw));
        if (n.Length < 8 || n.Length > 16 || !n[1..].All(char.IsDigit))
            throw new ArgumentException("手机号格式无效", nameof(raw));
        return n;
    }

    private static string NormalizeTransport(string? transport) =>
        string.Equals(transport, "voice", StringComparison.OrdinalIgnoreCase) ? "voice" : "sms";

    private static string? NormalizeCaptcha(string? captcha)
    {
        if (string.IsNullOrWhiteSpace(captcha))
            return null;
        var t = captcha.Trim();
        const string prefix = "signalcaptcha://";
        if (t.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            t = t[prefix.Length..];
        return string.IsNullOrWhiteSpace(t) ? null : t;
    }

    private static string NormalizeVerificationCode(string code)
    {
        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (digits.Length < 6)
            throw new ArgumentException("验证码无效", nameof(code));
        return digits;
    }

    private static byte[] DeriveUnidentifiedAccessKey(byte[] profileKey)
    {
        var hash = SHA256.HashData(profileKey);
        return hash.AsSpan(0, 16).ToArray();
    }

    private async Task TryRegisterPreKeysAsync(
        string path,
        SignedPreKeyRecord signed,
        List<OneTimePreKeyRecord> oneTime,
        KyberPreKeyRecord? lastResort,
        List<KyberPreKeyRecord> oneTimeKyber,
        CancellationToken ct)
    {
        try
        {
            await _rest.RegisterPreKeysAsync(path, new PreKeyUploadRequest
            {
                SignedPreKey = ToSignedEntity(signed),
                PreKeys = oneTime.Select(k => new PreKeyEntity
                {
                    KeyId = k.KeyId,
                    PublicKey = Convert.ToBase64String(k.PublicKey),
                }).ToList(),
                PqLastResortPreKey = lastResort is null ? null : ToKyberEntity(lastResort),
                PqPreKeys = oneTimeKyber.Count == 0
                    ? null
                    : oneTimeKyber.Select(ToKyberEntity).ToList(),
            }, ct);
        }
        catch (SignalApiException)
        {
            // Some self-hosted builds use different key paths; linking still succeeded.
        }
    }

    private async Task RefreshSenderCertificateAsync(CancellationToken ct)
    {
        try
        {
            var cert = await _rest.GetSenderCertificateAsync(ct);
            if (cert is { Length: > 0 })
                await _messages.SaveSenderCertificateAsync(cert, ct);
        }
        catch
        {
            // Sealed sender optional until certificate endpoint is available.
        }
    }

    private async Task EnsurePreKeyWaterlineAsync(CancellationToken ct)
    {
        var account = _account;
        var protocol = _protocol;
        if (account is null || protocol is null)
            return;

        try
        {
            var count = await _messages.CountPreKeysAsync(ct);
            if (count >= 20)
                return;

            var keys = await protocol.GenerateDeviceKeysAsync(account, _options.EnablePqKeys, ct);
            await TryRegisterPreKeysAsync(
                "v2/keys?identity=aci",
                keys.AciSignedPreKey,
                keys.OneTimePreKeys,
                keys.AciPqLastResortPreKey,
                keys.OneTimeKyberPreKeys,
                ct);
        }
        catch
        {
            // Non-fatal; send/receive may still work with existing keys.
        }
    }

    private static SignedPreKeyEntity ToSignedEntity(SignedPreKeyRecord r) => new()
    {
        KeyId = r.KeyId,
        PublicKey = Convert.ToBase64String(r.PublicKey),
        Signature = Convert.ToBase64String(r.Signature),
    };

    private static KyberPreKeyEntity ToKyberEntity(KyberPreKeyRecord r) => new()
    {
        KeyId = r.KeyId,
        PublicKey = Convert.ToBase64String(r.PublicKey),
        Signature = Convert.ToBase64String(r.Signature),
    };

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', 'A').Replace('/', 'B');
    }

    private static byte[] ProvisioningCryptoRandom(int n) =>
        Protocol.Crypto.ProvisioningCrypto.GetRandomBytes(n);

    private static string FormatUuid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            return Convert.ToHexString(bytes).ToLowerInvariant();
        return new Guid(bytes).ToString().ToLowerInvariant();
    }
}
