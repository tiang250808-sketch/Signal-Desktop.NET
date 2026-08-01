using System.Security.Cryptography;
using SignalCpf.LibSignal.Managed;
using SignalCpf.Storage;

namespace SignalCpf.LibSignal;

/// <summary>
/// Managed Signal-protocol-compatible session crypto used when libsignal_ffi is unavailable.
/// Thin facade over <see cref="KeyManager"/> and <see cref="SessionCipher"/>.
/// </summary>
public sealed class ManagedSignalProtocol : ISignalProtocolService
{
    private readonly KeyManager _keyManager;
    private readonly SessionCipher _sessionCipher;

    public ManagedSignalProtocol(IMessageStore store, AccountCredentials credentials)
    {
        _keyManager = new KeyManager(store);
        _sessionCipher = new SessionCipher(store, credentials);
    }

    public bool UsesNativeFfi => false;

    public int GenerateRegistrationId() =>
        RandomNumberGenerator.GetInt32(1, 0x3FFF);

    public Task<GeneratedDeviceKeys> GenerateDeviceKeysAsync(
        AccountCredentials credentials,
        bool enablePq,
        CancellationToken ct = default) =>
        _keyManager.GenerateDeviceKeysAsync(credentials, enablePq, ct);

    public Task ProcessPreKeyBundleAsync(
        string recipientServiceId,
        int deviceId,
        RemotePreKeyBundle bundle,
        CancellationToken ct = default) =>
        _sessionCipher.ProcessPreKeyBundleAsync(recipientServiceId, deviceId, bundle, ct);

    public Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        CancellationToken ct = default) =>
        _sessionCipher.EncryptAsync(recipientServiceId, deviceId, plaintext, ct);

    public Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        byte[]? senderCertificate,
        CancellationToken ct = default) =>
        // Managed path has no sealed-sender support.
        EncryptAsync(recipientServiceId, deviceId, plaintext, ct);

    public Task<DecryptResult> DecryptAsync(
        string senderServiceId,
        int senderDeviceId,
        int envelopeType,
        byte[] ciphertext,
        CancellationToken ct = default) =>
        _sessionCipher.DecryptAsync(senderServiceId, senderDeviceId, envelopeType, ciphertext, ct);
}
