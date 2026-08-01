using System.Text.Json;
using SignalCpf.Protocol.Crypto;
using SignalCpf.Storage;

namespace SignalCpf.LibSignal.Managed;

/// <summary>
/// Generates device keys, signed prekeys, one-time prekeys, and placeholder Kyber records.
/// </summary>
internal sealed class KeyManager
{
    private readonly IMessageStore _store;

    public KeyManager(IMessageStore store) => _store = store;

    public async Task<GeneratedDeviceKeys> GenerateDeviceKeysAsync(
        AccountCredentials credentials,
        bool enablePq,
        CancellationToken ct = default)
    {
        var aciSigned = CreateSignedPreKey(1, credentials.AciIdentityPrivateKey);
        await _store.SaveSignedPreKeyAsync(aciSigned.KeyId, SerializeKeyPair(aciSigned), ct);

        var pniPriv = credentials.PniIdentityPrivateKey ?? credentials.AciIdentityPrivateKey;
        var pniSigned = CreateSignedPreKey(1, pniPriv);
        await _store.SaveSignedPreKeyAsync(1000 + pniSigned.KeyId, SerializeKeyPair(pniSigned), ct);

        var oneTime = new List<OneTimePreKeyRecord>();
        for (uint i = 1; i <= 100; i++)
        {
            var kp = Curve25519.GenerateKeyPair();
            var rec = new OneTimePreKeyRecord
            {
                KeyId = i,
                PublicKey = kp.SerializePublicKey(),
                PrivateKey = kp.PrivateKey,
            };
            oneTime.Add(rec);
            await _store.SavePreKeyAsync(i, kp.PrivateKey, ct);
        }

        KyberPreKeyRecord? aciPq = null;
        KyberPreKeyRecord? pniPq = null;
        if (enablePq)
        {
            // Placeholder PQ records for servers that expect the fields.
            // Real Kyber requires libsignal_ffi; random material keeps JSON shape valid for older forks.
            aciPq = CreatePlaceholderKyber(1, credentials.AciIdentityPrivateKey);
            pniPq = CreatePlaceholderKyber(1, pniPriv);
        }

        return new GeneratedDeviceKeys
        {
            AciSignedPreKey = aciSigned,
            PniSignedPreKey = pniSigned,
            AciPqLastResortPreKey = aciPq,
            PniPqLastResortPreKey = pniPq,
            OneTimePreKeys = oneTime,
        };
    }

    internal static SignedPreKeyRecord CreateSignedPreKey(uint keyId, byte[] identityPrivate)
    {
        var kp = Curve25519.GenerateKeyPair();
        var signature = XEd25519.SignPreKey(identityPrivate, kp.SerializePublicKey());
        return new SignedPreKeyRecord
        {
            KeyId = keyId,
            PublicKey = kp.SerializePublicKey(),
            PrivateKey = kp.PrivateKey,
            Signature = signature,
        };
    }

    internal static KyberPreKeyRecord CreatePlaceholderKyber(uint keyId, byte[] identityPrivate)
    {
        var pub = ProvisioningCrypto.GetRandomBytes(1568);
        var sig = XEd25519.Sign(identityPrivate, pub);
        return new KyberPreKeyRecord
        {
            KeyId = keyId,
            PublicKey = pub,
            PrivateKey = ProvisioningCrypto.GetRandomBytes(32),
            Signature = sig,
        };
    }

    private static byte[] SerializeKeyPair(SignedPreKeyRecord rec) =>
        JsonSerializer.SerializeToUtf8Bytes(new { rec.KeyId, rec.PublicKey, rec.PrivateKey, rec.Signature });
}
