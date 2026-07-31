namespace SignalCpf.Protocol.Crypto;

/// <summary>
/// Signal identity / provisioning Curve25519 key pair (DJB type byte 0x05 on public key).
/// Adapted from Signal-Desktop Curve.node.ts + libsignal PublicKey serialization.
/// </summary>
public sealed class Curve25519KeyPair
{
    public const byte DjbType = 0x05;

    public Curve25519KeyPair(byte[] privateKey, byte[] publicKeyRaw)
    {
        if (privateKey.Length != 32)
            throw new ArgumentException("private key must be 32 bytes", nameof(privateKey));
        if (publicKeyRaw.Length != 32)
            throw new ArgumentException("public key must be 32 bytes", nameof(publicKeyRaw));

        PrivateKey = (byte[])privateKey.Clone();
        PublicKeyRaw = (byte[])publicKeyRaw.Clone();
    }

    /// <summary>32-byte clamped private scalar.</summary>
    public byte[] PrivateKey { get; }

    /// <summary>32-byte raw public key without type byte.</summary>
    public byte[] PublicKeyRaw { get; }

    /// <summary>33-byte libsignal-compatible public key (0x05 || raw).</summary>
    public byte[] SerializePublicKey()
    {
        var result = new byte[33];
        result[0] = DjbType;
        Buffer.BlockCopy(PublicKeyRaw, 0, result, 1, 32);
        return result;
    }

    public static byte[] StripTypeByte(ReadOnlySpan<byte> serializedPublicKey)
    {
        if (serializedPublicKey.Length == 32)
            return serializedPublicKey.ToArray();

        if (serializedPublicKey.Length != 33 || serializedPublicKey[0] != DjbType)
            throw new ArgumentException("Expected 33-byte DJB public key or 32-byte raw key");

        return serializedPublicKey.Slice(1).ToArray();
    }
}
