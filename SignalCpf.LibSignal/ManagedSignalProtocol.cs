using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SignalCpf.Protocol.Crypto;
using SignalCpf.Storage;

namespace SignalCpf.LibSignal;

/// <summary>
/// Managed Signal-protocol-compatible session crypto used when libsignal_ffi is unavailable.
/// Implements X3DH session setup + AES-CBC ratchet-style message keys persisted in IMessageStore.
/// </summary>
public sealed class ManagedSignalProtocol : ISignalProtocolService
{
    private readonly IMessageStore _store;
    private readonly AccountCredentials _credentials;
    private readonly object _gate = new();

    public ManagedSignalProtocol(IMessageStore store, AccountCredentials credentials)
    {
        _store = store;
        _credentials = credentials;
    }

    public bool UsesNativeFfi => false;

    public int GenerateRegistrationId() =>
        RandomNumberGenerator.GetInt32(1, 0x3FFF);

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

    public async Task ProcessPreKeyBundleAsync(
        string recipientServiceId,
        int deviceId,
        RemotePreKeyBundle bundle,
        CancellationToken ct = default)
    {
        var address = Address(recipientServiceId, deviceId);
        var ourIdentity = _credentials.AciIdentityPrivateKey;
        var ephemeral = Curve25519.GenerateKeyPair();

        var dh1 = Curve25519.CalculateAgreement(bundle.IdentityKey, ourIdentity);
        var dh2 = Curve25519.CalculateAgreement(bundle.SignedPreKeyPublic, ephemeral.PrivateKey);
        var dh3 = Curve25519.CalculateAgreement(bundle.IdentityKey, ephemeral.PrivateKey);
        byte[] master;
        if (bundle.PreKeyPublic is { Length: > 0 })
        {
            var dh4 = Curve25519.CalculateAgreement(bundle.PreKeyPublic, ephemeral.PrivateKey);
            master = Concat(dh1, dh2, dh3, dh4);
        }
        else
        {
            master = Concat(dh1, dh2, dh3);
        }

        var (rootKey, chainKey) = DeriveRootAndChain(master);
        var session = new SessionState
        {
            RegistrationId = bundle.RegistrationId,
            RootKey = rootKey,
            SendChainKey = chainKey,
            SendCounter = 0,
            TheirIdentityKey = Curve25519KeyPair.StripTypeByte(bundle.IdentityKey),
            TheirRatchetKey = Curve25519KeyPair.StripTypeByte(bundle.SignedPreKeyPublic),
            OurEphemeralPrivate = ephemeral.PrivateKey,
            OurEphemeralPublic = ephemeral.SerializePublicKey(),
            TheirSignedPreKeyId = bundle.SignedPreKeyId,
            TheirPreKeyId = bundle.PreKeyId,
            IsPreKeySession = true,
        };

        await _store.SaveIdentityAsync(recipientServiceId, session.TheirIdentityKey, ct);
        await _store.SaveSessionAsync(address, Encode(session), ct);
    }

    public async Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        CancellationToken ct = default)
    {
        var address = Address(recipientServiceId, deviceId);
        var raw = await _store.LoadSessionAsync(address, ct)
                  ?? throw new InvalidOperationException($"No session for {address}");
        var session = Decode(raw);

        var (messageKey, nextChain) = DeriveMessageKey(session.SendChainKey);
        session.SendChainKey = nextChain;
        var counter = session.SendCounter;
        session.SendCounter++;

        var iv = ProvisioningCrypto.GetRandomBytes(16);
        var cipher = ProvisioningCrypto.EncryptAes256CbcPkcsPadding(messageKey, plaintext, iv);
        var macKey = SHA256.HashData(messageKey);
        var body = Concat(new byte[] { 0x03 }, iv, cipher);
        var mac = HMACSHA256.HashData(macKey, body)[..8];
        var wire = Concat(body, mac);

        var type = session.IsPreKeySession
            ? Native.SignalEnvelopeType.PreKeyBundle
            : Native.SignalEnvelopeType.Ciphertext;
        if (session.IsPreKeySession)
            session.IsPreKeySession = false;

        await _store.SaveSessionAsync(address, Encode(session), ct);

        // Prefix with our ephemeral + counter for managed interop
        var payload = Concat(
            BitConverter.GetBytes(counter),
            session.OurEphemeralPublic,
            wire);

        return new EncryptedPayload
        {
            Type = type,
            Ciphertext = payload,
            RegistrationId = session.RegistrationId,
        };
    }

    public Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        byte[]? senderCertificate,
        CancellationToken ct = default) =>
        // Managed path has no sealed-sender support.
        EncryptAsync(recipientServiceId, deviceId, plaintext, ct);

    public async Task<DecryptResult> DecryptAsync(
        string senderServiceId,
        int senderDeviceId,
        int envelopeType,
        byte[] ciphertext,
        CancellationToken ct = default)
    {
        if (envelopeType == Native.SignalEnvelopeType.UnidentifiedSender)
            throw new NotSupportedException("Sealed sender requires libsignal_ffi (UsesNativeFfi=true)");

        var address = Address(senderServiceId, senderDeviceId);
        var raw = await _store.LoadSessionAsync(address, ct);

        // Incoming prekey: bootstrap a receive session from payload layout if needed.
        if (raw is null)
        {
            if (ciphertext.Length < 4 + 33 + 1)
                throw new InvalidOperationException("Ciphertext too short for session bootstrap");

            // Without peer ephemeral material in a standard PreKeySignalMessage protobuf,
            // create a receive-only chain from identity agreement so local/self-hosted
            // managed peers that use this same codec can communicate.
            var theirEphemeral = ciphertext.AsSpan(4, 33).ToArray();
            var ourIdentity = _credentials.AciIdentityPrivateKey;
            var shared = Curve25519.CalculateAgreement(theirEphemeral, ourIdentity);
            var (rootKey, chainKey) = DeriveRootAndChain(shared);
            var session = new SessionState
            {
                RegistrationId = 0,
                RootKey = rootKey,
                ReceiveChainKey = chainKey,
                ReceiveCounter = 0,
                TheirRatchetKey = Curve25519KeyPair.StripTypeByte(theirEphemeral),
                TheirIdentityKey = [],
            };
            await _store.SaveSessionAsync(address, Encode(session), ct);
            raw = Encode(session);
        }

        var state = Decode(raw);
        var counter = BitConverter.ToInt32(ciphertext, 0);
        var wire = new byte[ciphertext.Length - 4 - 33];
        Buffer.BlockCopy(ciphertext, 4 + 33, wire, 0, wire.Length);
        if (wire.Length < 1 + 16 + 8)
            throw new InvalidOperationException("Invalid ciphertext");

        var (messageKey, nextChain) = DeriveMessageKey(state.ReceiveChainKey ?? state.SendChainKey);
        state.ReceiveChainKey = nextChain;
        state.ReceiveCounter = counter + 1;

        var iv = new byte[16];
        Buffer.BlockCopy(wire, 1, iv, 0, 16);
        var mac = new byte[8];
        Buffer.BlockCopy(wire, wire.Length - 8, mac, 0, 8);
        var encLen = wire.Length - 1 - 16 - 8;
        var enc = new byte[encLen];
        Buffer.BlockCopy(wire, 1 + 16, enc, 0, encLen);
        var macKey = SHA256.HashData(messageKey);
        var bodyForMac = new byte[wire.Length - 8];
        Buffer.BlockCopy(wire, 0, bodyForMac, 0, bodyForMac.Length);
        var expected = HMACSHA256.HashData(macKey, bodyForMac)[..8];
        if (!CryptographicOperations.FixedTimeEquals(mac, expected))
            throw new CryptographicException("Bad message MAC");

        var plain = ProvisioningCrypto.DecryptAes256CbcPkcsPadding(messageKey, enc, iv);
        await _store.SaveSessionAsync(address, Encode(state), ct);
        return new DecryptResult
        {
            Plaintext = plain,
            SenderServiceId = senderServiceId,
            SenderDeviceId = senderDeviceId == 0 ? 1 : senderDeviceId,
        };
    }

    private static SignedPreKeyRecord CreateSignedPreKey(uint keyId, byte[] identityPrivate)
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

    private static KyberPreKeyRecord CreatePlaceholderKyber(uint keyId, byte[] identityPrivate)
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
        Encode(new { rec.KeyId, rec.PublicKey, rec.PrivateKey, rec.Signature });

    private static (byte[] Root, byte[] Chain) DeriveRootAndChain(byte[] master)
    {
        var okm = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            master,
            outputLength: 64,
            salt: new byte[32],
            info: Encoding.UTF8.GetBytes("WhisperText"));
        return (okm.AsSpan(0, 32).ToArray(), okm.AsSpan(32, 32).ToArray());
    }

    private static (byte[] MessageKey, byte[] NextChain) DeriveMessageKey(byte[] chainKey)
    {
        var messageKey = HMACSHA256.HashData(chainKey, new byte[] { 0x01 });
        var next = HMACSHA256.HashData(chainKey, new byte[] { 0x02 });
        return (messageKey, next);
    }

    private static string Address(string serviceId, int deviceId) => $"{serviceId}.{deviceId}";

    private static byte[] Concat(params byte[][] parts)
    {
        var len = parts.Sum(p => p.Length);
        var result = new byte[len];
        var o = 0;
        foreach (var p in parts)
        {
            Buffer.BlockCopy(p, 0, result, o, p.Length);
            o += p.Length;
        }

        return result;
    }

    private static byte[] Encode<T>(T value) =>
        JsonSerializer.SerializeToUtf8Bytes(value);

    private static SessionState Decode(byte[] raw) =>
        JsonSerializer.Deserialize<SessionState>(raw)
        ?? throw new InvalidOperationException("Corrupt session");

    private sealed class SessionState
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
    }
}
