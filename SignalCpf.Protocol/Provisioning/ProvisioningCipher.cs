using Google.Protobuf;
using SignalCpf.Protocol.Crypto;
using Signalservice;

namespace SignalCpf.Protocol.Provisioning;

/// <summary>
/// Adapted from Signal-Desktop ts/textsecure/ProvisioningCipher.node.ts
/// for an in-process CPF / .NET client (no Electron, no Node).
/// </summary>
public sealed class ProvisioningCipher
{
    private Curve25519KeyPair? _keyPair;

    public byte[] GetPublicKeySerialized()
    {
        EnsureKeyPair();
        return _keyPair!.SerializePublicKey();
    }

    public string GetPublicKeyBase64() =>
        Convert.ToBase64String(GetPublicKeySerialized());

    public ProvisionDecryptResult Decrypt(ReadOnlySpan<byte> envelopeBytes)
    {
        var envelope = ProvisionEnvelope.Parser.ParseFrom(envelopeBytes);
        return Decrypt(envelope);
    }

    public ProvisionDecryptResult Decrypt(ProvisionEnvelope envelope)
    {
        if (envelope.PublicKey.IsEmpty)
            throw new InvalidOperationException("Missing publicKey in ProvisionEnvelope");
        if (envelope.Body.IsEmpty)
            throw new InvalidOperationException("Missing body in ProvisionEnvelope");

        EnsureKeyPair();

        var message = envelope.Body.Span;
        if (message.Length < 1 + 16 + 32 || message[0] != 1)
            throw new InvalidOperationException("Bad version number on ProvisioningMessage");

        var iv = message.Slice(1, 16);
        var mac = message.Slice(message.Length - 32);
        var ivAndCiphertext = message.Slice(0, message.Length - 32);
        var ciphertext = message.Slice(1 + 16, message.Length - 32 - 1 - 16);

        var shared = Curve25519.CalculateAgreement(envelope.PublicKey.Span, _keyPair!.PrivateKey);
        var (cipherKey, macKey, _) = ProvisioningCrypto.DeriveProvisioningSecrets(shared);
        ProvisioningCrypto.VerifyHmacSha256(ivAndCiphertext, macKey, mac, 32);

        var plaintext = ProvisioningCrypto.DecryptAes256CbcPkcsPadding(cipherKey, ciphertext, iv);
        var provisionMessage = ProvisionMessage.Parser.ParseFrom(plaintext);

        if (provisionMessage.AciIdentityKeyPrivate.IsEmpty)
            throw new InvalidOperationException("Missing aciKeyPrivate in ProvisionMessage");

        var aciKeyPair = Curve25519.CreateKeyPair(provisionMessage.AciIdentityKeyPrivate.Span);
        Curve25519KeyPair? pniKeyPair = null;
        if (!provisionMessage.PniIdentityKeyPrivate.IsEmpty)
            pniKeyPair = Curve25519.CreateKeyPair(provisionMessage.PniIdentityKeyPrivate.Span);

        var (aci, pni) = ResolveServiceIds(provisionMessage);

        return new ProvisionDecryptResult
        {
            AciKeyPair = aciKeyPair,
            PniKeyPair = pniKeyPair,
            Number = NullIfEmpty(provisionMessage.Number),
            Aci = aci,
            Pni = pni,
            ProvisioningCode = NullIfEmpty(provisionMessage.ProvisioningCode),
            UserAgent = NullIfEmpty(provisionMessage.UserAgent),
            ReadReceipts = provisionMessage.ReadReceipts,
            ProfileKey = ToOptionalBytes(provisionMessage.ProfileKey),
            MasterKey = ToOptionalBytes(provisionMessage.MasterKey),
            EphemeralBackupKey = ToOptionalBytes(provisionMessage.EphemeralBackupKey),
            MediaRootBackupKey = ToOptionalBytes(provisionMessage.MediaRootBackupKey),
            AccountEntropyPool = NullIfEmpty(provisionMessage.AccountEntropyPool),
        };
    }

    /// <summary>
    /// Encrypt a ProvisionMessage for round-trip tests / tooling.
    /// Mirrors the phone-side provisioning envelope format.
    /// </summary>
    public ProvisionEnvelope EncryptForTests(ProvisionMessage message)
    {
        EnsureKeyPair();

        var ephemeral = Curve25519.GenerateKeyPair();
        var shared = Curve25519.CalculateAgreement(
            _keyPair!.SerializePublicKey(),
            ephemeral.PrivateKey);
        var (cipherKey, macKey, _) = ProvisioningCrypto.DeriveProvisioningSecrets(shared);

        var iv = ProvisioningCrypto.GetRandomBytes(16);
        var plaintext = message.ToByteArray();
        var ciphertext = ProvisioningCrypto.EncryptAes256CbcPkcsPadding(cipherKey, plaintext, iv);

        var body = new byte[1 + iv.Length + ciphertext.Length + 32];
        body[0] = 1;
        Buffer.BlockCopy(iv, 0, body, 1, iv.Length);
        Buffer.BlockCopy(ciphertext, 0, body, 1 + iv.Length, ciphertext.Length);

        var ivAndCiphertext = body.AsSpan(0, body.Length - 32);
        var mac = ProvisioningCrypto.HmacSha256(macKey, ivAndCiphertext);
        Buffer.BlockCopy(mac, 0, body, body.Length - 32, 32);

        return new ProvisionEnvelope
        {
            PublicKey = ByteString.CopyFrom(ephemeral.SerializePublicKey()),
            Body = ByteString.CopyFrom(body),
        };
    }

    private void EnsureKeyPair() =>
        _keyPair ??= Curve25519.GenerateKeyPair();

    private static (string Aci, string Pni) ResolveServiceIds(ProvisionMessage message)
    {
        if (!message.AciBinary.IsEmpty && !message.PniBinary.IsEmpty)
        {
            if (message.AciBinary.Length != 16 || message.PniBinary.Length != 16)
                throw new InvalidOperationException("aciBinary/pniBinary must be 16-byte UUIDs");

            var aci = FormatRfc4122Uuid(message.AciBinary.Span);
            var pni = $"PNI:{FormatRfc4122Uuid(message.PniBinary.Span)}";
            return (aci, pni);
        }

        if (!string.IsNullOrEmpty(message.Aci) && !string.IsNullOrEmpty(message.Pni))
        {
            if (!Guid.TryParse(message.Pni, out _))
                throw new InvalidOperationException("ProvisioningCipher: invalid untaggedPni");

            return (NormalizeAci(message.Aci), NormalizePni($"PNI:{message.Pni}"));
        }

        throw new InvalidOperationException("Missing aci/pni in provisioning message");
    }

    private static string NormalizeAci(string raw) =>
        Guid.TryParse(raw, out var g)
            ? g.ToString().ToLowerInvariant()
            : throw new InvalidOperationException($"invalid ACI: {raw}");

    private static string NormalizePni(string raw)
    {
        var tagged = raw.StartsWith("PNI:", StringComparison.OrdinalIgnoreCase)
            ? "PNI:" + raw[4..]
            : "PNI:" + raw;
        var uuidPart = tagged[4..];
        if (!Guid.TryParse(uuidPart, out var g))
            throw new InvalidOperationException($"invalid PNI: {raw}");
        return $"PNI:{g.ToString().ToLowerInvariant()}";
    }

    private static string FormatRfc4122Uuid(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException("UUID must be 16 bytes");

        static string Hex(ReadOnlySpan<byte> s) => Convert.ToHexString(s).ToLowerInvariant();

        return $"{Hex(bytes[..4])}-{Hex(bytes[4..6])}-{Hex(bytes[6..8])}-{Hex(bytes[8..10])}-{Hex(bytes[10..16])}";
    }

    private static string? NullIfEmpty(string? value) =>
        string.IsNullOrEmpty(value) ? null : value;

    private static byte[]? ToOptionalBytes(ByteString bytes) =>
        bytes.IsEmpty ? null : bytes.ToByteArray();
}
