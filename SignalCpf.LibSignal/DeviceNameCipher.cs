using System.Security.Cryptography;
using System.Text;
using Google.Protobuf;
using SignalCpf.Protocol.Crypto;
using Signalservice;

namespace SignalCpf.LibSignal;

/// <summary>
/// Encrypts linked-device display names (Signal DeviceName proto + profile key).
/// </summary>
public static class DeviceNameCipher
{
    public static string EncryptToBase64(string deviceName, ReadOnlySpan<byte> profileKey)
    {
        if (profileKey.Length != 32)
            throw new ArgumentException("profileKey must be 32 bytes");

        var ephemeral = Curve25519.GenerateKeyPair();
        // Synthetic IV: first 16 bytes of HMAC(profileKey, deviceName)
        var nameBytes = Encoding.UTF8.GetBytes(deviceName);
        var mac = HMACSHA256.HashData(profileKey, nameBytes);
        var syntheticIv = mac.AsSpan(0, 16).ToArray();

        // Key: HMAC(profileKey, syntheticIv)[:32]
        var cipherKey = HMACSHA256.HashData(profileKey, syntheticIv);

        var ciphertext = AesCtr(cipherKey, syntheticIv, nameBytes);

        var proto = new DeviceName
        {
            EphemeralPublic = ByteString.CopyFrom(ephemeral.SerializePublicKey()),
            SyntheticIv = ByteString.CopyFrom(syntheticIv),
            Ciphertext = ByteString.CopyFrom(ciphertext),
        };
        return Convert.ToBase64String(proto.ToByteArray());
    }

    private static byte[] AesCtr(byte[] key, byte[] iv, byte[] plaintext)
    {
        // AES-CTR via Aes.EncryptCbc of counter blocks is awkward; use AesGcm-compatible
        // incremental counter XOR as used by Signal's device-name encryption.
        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.ECB;
        aes.Padding = PaddingMode.None;
        var encryptor = aes.CreateEncryptor();

        var counter = new byte[16];
        Buffer.BlockCopy(iv, 0, counter, 0, Math.Min(16, iv.Length));
        var output = new byte[plaintext.Length];
        var keystream = new byte[16];
        var offset = 0;
        while (offset < plaintext.Length)
        {
            encryptor.TransformBlock(counter, 0, 16, keystream, 0);
            var n = Math.Min(16, plaintext.Length - offset);
            for (var i = 0; i < n; i++)
                output[offset + i] = (byte)(plaintext[offset + i] ^ keystream[i]);
            offset += n;
            // increment counter big-endian
            for (var i = 15; i >= 0; i--)
            {
                if (++counter[i] != 0)
                    break;
            }
        }

        return output;
    }
}
