using SignalCpf.Storage;

namespace SignalCpf.LibSignal;

public interface ISignalProtocolService
{
    bool UsesNativeFfi { get; }

    int GenerateRegistrationId();

    Task<GeneratedDeviceKeys> GenerateDeviceKeysAsync(
        AccountCredentials credentials,
        bool enablePq,
        CancellationToken ct = default);

    Task ProcessPreKeyBundleAsync(
        string recipientServiceId,
        int deviceId,
        RemotePreKeyBundle bundle,
        CancellationToken ct = default);

    Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        CancellationToken ct = default);

    /// <summary>
    /// Encrypt with sealed sender when <paramref name="senderCertificate"/> is provided;
    /// otherwise behaves like <see cref="EncryptAsync"/>.
    /// </summary>
    Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        byte[]? senderCertificate,
        CancellationToken ct = default);

    Task<DecryptResult> DecryptAsync(
        string senderServiceId,
        int senderDeviceId,
        int envelopeType,
        byte[] ciphertext,
        CancellationToken ct = default);
}

public sealed class DecryptResult
{
    public required byte[] Plaintext { get; init; }
    public string? SenderServiceId { get; init; }
    public int SenderDeviceId { get; init; }
}

public sealed class RemotePreKeyBundle
{
    public required byte[] IdentityKey { get; init; }
    public int RegistrationId { get; init; }
    public uint? PreKeyId { get; init; }
    public byte[]? PreKeyPublic { get; init; }
    public uint SignedPreKeyId { get; init; }
    public required byte[] SignedPreKeyPublic { get; init; }
    public required byte[] SignedPreKeySignature { get; init; }
    public uint? KyberPreKeyId { get; init; }
    public byte[]? KyberPreKeyPublic { get; init; }
    public byte[]? KyberPreKeySignature { get; init; }
}

public sealed class GeneratedDeviceKeys
{
    public required SignedPreKeyRecord AciSignedPreKey { get; init; }
    public required SignedPreKeyRecord PniSignedPreKey { get; init; }
    public KyberPreKeyRecord? AciPqLastResortPreKey { get; init; }
    public KyberPreKeyRecord? PniPqLastResortPreKey { get; init; }
    public required List<OneTimePreKeyRecord> OneTimePreKeys { get; init; }
    public List<KyberPreKeyRecord> OneTimeKyberPreKeys { get; init; } = [];
}

public sealed class SignedPreKeyRecord
{
    public uint KeyId { get; set; }
    public byte[] PublicKey { get; set; } = [];
    public byte[] PrivateKey { get; set; } = [];
    public byte[] Signature { get; set; } = [];
    /// <summary>Serialized libsignal SignedPreKeyRecord when produced by FFI.</summary>
    public byte[]? SerializedRecord { get; set; }
}

public sealed class KyberPreKeyRecord
{
    public uint KeyId { get; set; }
    public byte[] PublicKey { get; set; } = [];
    public byte[] Signature { get; set; } = [];
    public byte[] PrivateKey { get; set; } = [];
    public byte[]? SerializedRecord { get; set; }
}

public sealed class OneTimePreKeyRecord
{
    public uint KeyId { get; set; }
    public byte[] PublicKey { get; set; } = [];
    public byte[] PrivateKey { get; set; } = [];
    public byte[]? SerializedRecord { get; set; }
}

public sealed class EncryptedPayload
{
    /// <summary>Signal envelope type: 1 ciphertext, 3 prekey, 6 unidentified.</summary>
    public int Type { get; init; }
    public required byte[] Ciphertext { get; init; }
    public int RegistrationId { get; init; }
}
