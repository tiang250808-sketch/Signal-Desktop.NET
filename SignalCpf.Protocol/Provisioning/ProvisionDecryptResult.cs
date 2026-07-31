using SignalCpf.Protocol.Crypto;

namespace SignalCpf.Protocol.Provisioning;

/// <summary>
/// Result of decrypting a ProvisionEnvelope.
/// Adapted from Signal-Desktop ProvisioningCipher.node.ts ProvisionDecryptResult.
/// </summary>
public sealed class ProvisionDecryptResult
{
    public required Curve25519KeyPair AciKeyPair { get; init; }
    public Curve25519KeyPair? PniKeyPair { get; init; }
    public string? Number { get; init; }
    public required string Aci { get; init; }
    public required string Pni { get; init; }
    public string? ProvisioningCode { get; init; }
    public string? UserAgent { get; init; }
    public bool ReadReceipts { get; init; }
    public byte[]? ProfileKey { get; init; }
    public byte[]? MasterKey { get; init; }
    public string? AccountEntropyPool { get; init; }
    public byte[]? MediaRootBackupKey { get; init; }
    public byte[]? EphemeralBackupKey { get; init; }
}
