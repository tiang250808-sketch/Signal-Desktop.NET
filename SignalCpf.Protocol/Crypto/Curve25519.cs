using NSec.Cryptography;

namespace SignalCpf.Protocol.Crypto;

/// <summary>
/// X25519 helpers adapted from Signal-Desktop Curve.node.ts.
/// </summary>
public static class Curve25519
{
    private static readonly KeyAgreementAlgorithm Algorithm = KeyAgreementAlgorithm.X25519;

    private static KeyCreationParameters Exportable => new()
    {
        ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
    };

    public static Curve25519KeyPair GenerateKeyPair()
    {
        using var key = Key.Create(Algorithm, Exportable);
        var privateKey = key.Export(KeyBlobFormat.RawPrivateKey);
        var publicKey = key.Export(KeyBlobFormat.RawPublicKey);
        ClampPrivateKey(privateKey);
        return new Curve25519KeyPair(privateKey, publicKey);
    }

    public static Curve25519KeyPair CreateKeyPair(ReadOnlySpan<byte> incomingPrivateKey)
    {
        if (incomingPrivateKey.Length != 32)
            throw new ArgumentException("key must be 32 bytes long");

        var copy = incomingPrivateKey.ToArray();
        ClampPrivateKey(copy);

        using var key = Key.Import(Algorithm, copy, KeyBlobFormat.RawPrivateKey, Exportable);
        var publicKey = key.Export(KeyBlobFormat.RawPublicKey);
        return new Curve25519KeyPair(copy, publicKey);
    }

    public static byte[] CalculateAgreement(
        ReadOnlySpan<byte> theirPublicKeySerialized,
        ReadOnlySpan<byte> ourPrivateKey)
    {
        var theirRaw = Curve25519KeyPair.StripTypeByte(theirPublicKeySerialized);
        var publicKey = PublicKey.Import(Algorithm, theirRaw, KeyBlobFormat.RawPublicKey);

        using var privateKey = Key.Import(
            Algorithm,
            ourPrivateKey,
            KeyBlobFormat.RawPrivateKey,
            Exportable);

        using var shared = Algorithm.Agree(
            privateKey,
            publicKey,
            new SharedSecretCreationParameters
            {
                ExportPolicy = KeyExportPolicies.AllowPlaintextExport,
            }) ?? throw new InvalidOperationException("X25519 agreement failed");

        return shared.Export(SharedSecretBlobFormat.RawSharedSecret);
    }

    public static void ClampPrivateKey(Span<byte> privateKey)
    {
        if (privateKey.Length != 32)
            throw new ArgumentException("private key must be 32 bytes");

        privateKey[0] &= 248;
        privateKey[31] &= 127;
        privateKey[31] |= 64;
    }
}
