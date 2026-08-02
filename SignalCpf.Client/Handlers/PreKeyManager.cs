using SignalCpf.Core.Options;
using SignalCpf.LibSignal;
using SignalCpf.Net.Http;
using SignalCpf.Storage;

namespace SignalCpf.Client.Handlers;

/// <summary>
/// PreKey waterline maintenance and upload to the Signal server.
/// </summary>
internal sealed class PreKeyManager
{
    private readonly SignalServerOptions _options;
    private readonly IMessageStore _store;
    private readonly SignalRestClient _rest;
    private readonly ClientState _state;

    public PreKeyManager(
        SignalServerOptions options,
        IMessageStore store,
        SignalRestClient rest,
        ClientState state)
    {
        _options = options;
        _store = store;
        _rest = rest;
        _state = state;
    }

    public async Task EnsureWaterlineAsync(CancellationToken ct = default)
    {
        var (account, protocol) = _state.Snapshot();
        if (account is null || protocol is null)
            return;

        try
        {
            var count = await _store.CountPreKeysAsync(ct);
            if (count >= 20)
                return;

            var keys = await protocol.GenerateDeviceKeysAsync(account, _options.EnablePqKeys, ct);
            await TryRegisterAsync(
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

    public async Task TryRegisterAsync(
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

    public async Task RefreshSenderCertificateAsync(CancellationToken ct = default)
    {
        try
        {
            var cert = await _rest.GetSenderCertificateAsync(ct);
            if (cert is { Length: > 0 })
                await _store.SaveSenderCertificateAsync(cert, ct);
        }
        catch
        {
            // Sealed sender optional until certificate endpoint is available.
        }
    }

    internal static SignedPreKeyEntity ToSignedEntity(SignedPreKeyRecord r) => new()
    {
        KeyId = r.KeyId,
        PublicKey = Convert.ToBase64String(r.PublicKey),
        Signature = Convert.ToBase64String(r.Signature),
    };

    internal static KyberPreKeyEntity ToKyberEntity(KyberPreKeyRecord r) => new()
    {
        KeyId = r.KeyId,
        PublicKey = Convert.ToBase64String(r.PublicKey),
        Signature = Convert.ToBase64String(r.Signature),
    };
}
