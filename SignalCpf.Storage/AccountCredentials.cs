namespace SignalCpf.Storage;

/// <summary>Persisted linked-device credentials and identity material.</summary>
public sealed class AccountCredentials
{
    public string Aci { get; set; } = "";
    public string Pni { get; set; } = "";
    public string? Number { get; set; }
    public int DeviceId { get; set; }
    public string DeviceName { get; set; } = "";
    public string Password { get; set; } = "";
    public int RegistrationId { get; set; }
    public int PniRegistrationId { get; set; }
    public byte[] AciIdentityPrivateKey { get; set; } = [];
    public byte[] AciIdentityPublicKey { get; set; } = [];
    public byte[]? PniIdentityPrivateKey { get; set; }
    public byte[]? PniIdentityPublicKey { get; set; }
    public byte[]? ProfileKey { get; set; }
    public byte[]? MasterKey { get; set; }
    public string? AccountEntropyPool { get; set; }
    public byte[]? MediaRootBackupKey { get; set; }
    public bool ReadReceipts { get; set; } = true;
    public DateTimeOffset LinkedAt { get; set; } = DateTimeOffset.UtcNow;
}
