using System.Security.Cryptography;
using System.Text.Json;
using SignalCpf.Core.Models;
using SignalCpf.Core.Options;

namespace SignalCpf.Storage;

public interface ICredentialStore
{
    Task<AccountCredentials?> LoadAsync(CancellationToken ct = default);
    Task SaveAsync(AccountCredentials credentials, CancellationToken ct = default);
    Task ClearAsync(CancellationToken ct = default);
    AccountStatus ToAccountStatus(AccountCredentials? credentials);
}

/// <summary>
/// Encrypts credentials at rest with DPAPI on Windows, or AES keyed by a machine-local file elsewhere.
/// </summary>
public sealed class CredentialStore : ICredentialStore
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
    };

    private readonly string _path;
    private readonly string _keyPath;

    public CredentialStore(SignalServerOptions options)
    {
        Directory.CreateDirectory(options.DataDirectory);
        _path = Path.Combine(options.DataDirectory, "credentials.bin");
        _keyPath = Path.Combine(options.DataDirectory, "credentials.key");
    }

    public async Task<AccountCredentials?> LoadAsync(CancellationToken ct = default)
    {
        if (!File.Exists(_path))
            return null;

        var cipher = await File.ReadAllBytesAsync(_path, ct);
        var plain = Unprotect(cipher);
        return JsonSerializer.Deserialize<AccountCredentials>(plain, JsonOpts);
    }

    public async Task SaveAsync(AccountCredentials credentials, CancellationToken ct = default)
    {
        var plain = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOpts);
        var cipher = Protect(plain);
        await File.WriteAllBytesAsync(_path, cipher, ct);
    }

    public Task ClearAsync(CancellationToken ct = default)
    {
        if (File.Exists(_path))
            File.Delete(_path);
        return Task.CompletedTask;
    }

    public AccountStatus ToAccountStatus(AccountCredentials? credentials)
    {
        if (credentials is null)
            return new AccountStatus(IsRegistered: false);

        return new AccountStatus(
            IsRegistered: true,
            ServiceId: credentials.Aci,
            Pni: credentials.Pni,
            DeviceId: credentials.DeviceId,
            DeviceName: credentials.DeviceName,
            Number: credentials.Number);
    }

    private byte[] Protect(byte[] plain)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Protect(plain, optionalEntropy: null, DataProtectionScope.CurrentUser);

        var key = LoadOrCreateAesKey();
        using var aes = Aes.Create();
        aes.Key = key;
        aes.GenerateIV();
        using var enc = aes.CreateEncryptor();
        var cipher = enc.TransformFinalBlock(plain, 0, plain.Length);
        var result = new byte[aes.IV.Length + cipher.Length];
        Buffer.BlockCopy(aes.IV, 0, result, 0, aes.IV.Length);
        Buffer.BlockCopy(cipher, 0, result, aes.IV.Length, cipher.Length);
        return result;
    }

    private byte[] Unprotect(byte[] cipher)
    {
        if (OperatingSystem.IsWindows())
            return ProtectedData.Unprotect(cipher, optionalEntropy: null, DataProtectionScope.CurrentUser);

        var key = LoadOrCreateAesKey();
        using var aes = Aes.Create();
        aes.Key = key;
        var iv = cipher.AsSpan(0, 16).ToArray();
        var data = cipher.AsSpan(16).ToArray();
        aes.IV = iv;
        using var dec = aes.CreateDecryptor();
        return dec.TransformFinalBlock(data, 0, data.Length);
    }

    private byte[] LoadOrCreateAesKey()
    {
        if (File.Exists(_keyPath))
            return File.ReadAllBytes(_keyPath);

        var key = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(_keyPath, key);
        return key;
    }
}
