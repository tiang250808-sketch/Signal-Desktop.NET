using System.Security.Cryptography;
using SignalCpf.Core.Models;
using SignalCpf.Core.Options;
using SignalCpf.LibSignal;
using SignalCpf.Net.Http;
using SignalCpf.Net.Provisioning;
using SignalCpf.Protocol.Crypto;
using SignalCpf.Protocol.Provisioning;
using SignalCpf.Storage;

namespace SignalCpf.Client.Handlers;

/// <summary>
/// Linked-device provisioning: QR, provision envelope decrypt, and linkDevice.
/// </summary>
internal sealed class ProvisioningHandler
{
    private readonly SignalServerOptions _options;
    private readonly ICredentialStore _credentials;
    private readonly IMessageStore _messages;
    private readonly SignalRestClient _rest;
    private readonly ClientState _state;
    private readonly PreKeyManager _preKeys;
    private readonly Func<CancellationToken, Task> _startMessageSocket;

    private CancellationTokenSource? _provisionCts;

    public ProvisioningHandler(
        SignalServerOptions options,
        ICredentialStore credentials,
        IMessageStore messages,
        SignalRestClient rest,
        ClientState state,
        PreKeyManager preKeys,
        Func<CancellationToken, Task> startMessageSocket)
    {
        _options = options;
        _credentials = credentials;
        _messages = messages;
        _rest = rest;
        _state = state;
        _preKeys = preKeys;
        _startMessageSocket = startMessageSocket;
    }

    public async Task<ProvisioningQr> StartAsync(
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        _provisionCts?.Cancel();
        _provisionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var ct = _provisionCts.Token;

        var cipher = new ProvisioningCipher();
        var pubKey = cipher.GetPublicKeyBase64();

        await EmitAsync(
            ProvisioningProgressKind.WaitingForScan,
            $"Connecting provisioning socket ({_options.ApiBaseUrl})…",
            null,
            ct);

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
                    await EmitAsync(
                        ProvisioningProgressKind.QrReady,
                        "Scan this QR with your primary Signal device",
                        qr,
                        ct);
                }, ct);

                await EmitAsync(
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
                await EmitAsync(
                    ProvisioningProgressKind.Failed,
                    ex.Message,
                    null,
                    CancellationToken.None);
                await _state.EmitAsync(
                    new SidecarEvent.Error("PROVISIONING_FAILED", ex.Message),
                    CancellationToken.None);
            }
        }, CancellationToken.None);

        return await tcsQr.Task.WaitAsync(ct);
    }

    public void Cancel() => _provisionCts?.Cancel();

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

        var profileKey = decrypted.ProfileKey ?? ProvisioningCrypto.GetRandomBytes(32);
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
            AciSignedPreKey = PreKeyManager.ToSignedEntity(keys.AciSignedPreKey),
            PniSignedPreKey = PreKeyManager.ToSignedEntity(keys.PniSignedPreKey),
            AciPqLastResortPreKey = keys.AciPqLastResortPreKey is null
                ? null
                : PreKeyManager.ToKyberEntity(keys.AciPqLastResortPreKey),
            PniPqLastResortPreKey = keys.PniPqLastResortPreKey is null
                ? null
                : PreKeyManager.ToKyberEntity(keys.PniPqLastResortPreKey),
        };

        var linkResp = await _rest.LinkDeviceAsync(linkReq, ct);
        pending.DeviceId = linkResp.DeviceId;
        pending.LinkedAt = DateTimeOffset.UtcNow;

        await _credentials.SaveAsync(pending, ct);
        var liveProtocol = SignalProtocolFactory.Create(_messages, pending);
        _state.SetAccountAndProtocol(pending, liveProtocol);

        _rest.SetDeviceAuth(pending.Aci, pending.DeviceId, pending.Password);

        await _preKeys.TryRegisterAsync("v2/keys?identity=aci", keys.AciSignedPreKey, keys.OneTimePreKeys,
            keys.AciPqLastResortPreKey, keys.OneTimeKyberPreKeys, ct);
        await _preKeys.TryRegisterAsync("v2/keys?identity=pni", keys.PniSignedPreKey, keys.OneTimePreKeys,
            keys.PniPqLastResortPreKey, [], ct);

        await _preKeys.RefreshSenderCertificateAsync(ct);

        await EmitAsync(
            ProvisioningProgressKind.Linked,
            $"Linked as device {pending.DeviceId}",
            null,
            ct);

        var status = _credentials.ToAccountStatus(pending);
        await _state.EmitAsync(new SidecarEvent.AccountStatusChanged(status), ct);
        _ = _startMessageSocket(ct);
    }

    private ValueTask EmitAsync(
        ProvisioningProgressKind kind,
        string message,
        ProvisioningQr? qr,
        CancellationToken ct) =>
        _state.EmitAsync(
            new SidecarEvent.ProvisioningUpdated(new ProvisioningProgress(kind, message, qr)),
            ct);

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', 'A').Replace('/', 'B');
    }
}
