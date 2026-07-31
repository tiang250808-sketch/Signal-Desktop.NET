using System.Security.Cryptography;
using System.Text;

namespace SignalCpf.Protocol.Crypto;

/// <summary>
/// Provisioning crypto helpers adapted from Signal-Desktop Crypto.node.ts.
/// </summary>
public static class ProvisioningCrypto
{
    public static (byte[] CipherKey, byte[] MacKey, byte[] Extra) DeriveSecrets(
        ReadOnlySpan<byte> input,
        ReadOnlySpan<byte> salt,
        ReadOnlySpan<byte> info)
    {
        var output = HKDF.DeriveKey(
            HashAlgorithmName.SHA256,
            ikm: input.ToArray(),
            outputLength: 96,
            salt: salt.ToArray(),
            info: info.ToArray());

        return (
            output.AsSpan(0, 32).ToArray(),
            output.AsSpan(32, 32).ToArray(),
            output.AsSpan(64, 32).ToArray());
    }

    public static (byte[] CipherKey, byte[] MacKey, byte[] Extra) DeriveProvisioningSecrets(
        ReadOnlySpan<byte> sharedSecret)
    {
        var salt = new byte[32];
        var info = Encoding.UTF8.GetBytes("TextSecure Provisioning Message");
        return DeriveSecrets(sharedSecret, salt, info);
    }

    public static byte[] HmacSha256(ReadOnlySpan<byte> key, ReadOnlySpan<byte> plaintext)
    {
        return HMACSHA256.HashData(key, plaintext);
    }

    public static void VerifyHmacSha256(
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> theirMac,
        int length)
    {
        var ourMac = HmacSha256(key, plaintext);
        if (theirMac.Length != length || ourMac.Length < length)
            throw new CryptographicException("Bad MAC length");

        if (!CryptographicOperations.FixedTimeEquals(ourMac.AsSpan(0, length), theirMac))
            throw new CryptographicException("Bad MAC");
    }

    public static byte[] DecryptAes256CbcPkcsPadding(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> ciphertext,
        ReadOnlySpan<byte> iv)
    {
        using var aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.IV = iv.ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes.DecryptCbc(ciphertext, iv, PaddingMode.PKCS7);
    }

    public static byte[] EncryptAes256CbcPkcsPadding(
        ReadOnlySpan<byte> key,
        ReadOnlySpan<byte> plaintext,
        ReadOnlySpan<byte> iv)
    {
        using var aes = Aes.Create();
        aes.Key = key.ToArray();
        aes.IV = iv.ToArray();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        return aes.EncryptCbc(plaintext, iv, PaddingMode.PKCS7);
    }

    public static byte[] GetRandomBytes(int size) =>
        RandomNumberGenerator.GetBytes(size);
}
