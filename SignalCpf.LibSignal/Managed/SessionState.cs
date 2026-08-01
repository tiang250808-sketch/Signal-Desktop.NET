using System.Text.Json;

namespace SignalCpf.LibSignal.Managed;

/// <summary>
/// Persisted session state model and JSON encode/decode for managed protocol sessions.
/// </summary>
internal sealed class SessionState
{
    public int RegistrationId { get; set; }
    public byte[] RootKey { get; set; } = [];
    public byte[] SendChainKey { get; set; } = [];
    public byte[]? ReceiveChainKey { get; set; }
    public int SendCounter { get; set; }
    public int ReceiveCounter { get; set; }
    public byte[] TheirIdentityKey { get; set; } = [];
    public byte[] TheirRatchetKey { get; set; } = [];
    public byte[] OurEphemeralPrivate { get; set; } = [];
    public byte[] OurEphemeralPublic { get; set; } = [];
    public uint TheirSignedPreKeyId { get; set; }
    public uint? TheirPreKeyId { get; set; }
    public bool IsPreKeySession { get; set; }

    public static byte[] Encode(SessionState value) =>
        JsonSerializer.SerializeToUtf8Bytes(value);

    public static SessionState Decode(byte[] raw) =>
        JsonSerializer.Deserialize<SessionState>(raw)
        ?? throw new InvalidOperationException("Corrupt session");
}
