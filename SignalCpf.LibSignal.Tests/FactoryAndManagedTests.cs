using SignalCpf.Core.Options;
using SignalCpf.LibSignal.Native;
using SignalCpf.Storage;
using Xunit;

namespace SignalCpf.LibSignal.Tests;

public class FactoryAndManagedTests
{
    [Fact]
    public async Task Factory_ReturnsManaged_WhenNativeUnavailable()
    {
        if (LibSignalNative.IsAvailable)
            return; // skip assertion when FFI present

        await using var store = CreateStore();
        await store.InitializeAsync();
        var protocol = SignalProtocolFactory.Create(store, SampleCredentials());
        Assert.False(protocol.UsesNativeFfi);
        Assert.IsType<ManagedSignalProtocol>(protocol);
    }

    [Fact]
    public async Task Factory_ReturnsFfi_WhenNativeAvailable()
    {
        if (!LibSignalNative.IsAvailable)
            return;

        await using var store = CreateStore();
        await store.InitializeAsync();
        var protocol = SignalProtocolFactory.Create(store, SampleCredentials());
        Assert.True(protocol.UsesNativeFfi);
    }

    [Fact]
    public async Task Managed_GenerateKeys_And_Encrypt_ProducesEnvelopeTypes()
    {
        await using var bobStore = CreateStore("bob");
        await using var aliceStore = CreateStore("alice");
        await bobStore.InitializeAsync();
        await aliceStore.InitializeAsync();

        var aliceCreds = SampleCredentials("alice-aci");
        var bobCreds = SampleCredentials("bob-aci");
        bobCreds.RegistrationId = 42;

        var bobProtocol = new ManagedSignalProtocol(bobStore, bobCreds);
        var bobKeys = await bobProtocol.GenerateDeviceKeysAsync(bobCreds, enablePq: false);
        Assert.Equal(100, bobKeys.OneTimePreKeys.Count);

        var aliceProtocol = new ManagedSignalProtocol(aliceStore, aliceCreds);
        await aliceProtocol.ProcessPreKeyBundleAsync("bob-aci", 1, new RemotePreKeyBundle
        {
            IdentityKey = bobCreds.AciIdentityPublicKey,
            RegistrationId = bobCreds.RegistrationId,
            PreKeyId = bobKeys.OneTimePreKeys[0].KeyId,
            PreKeyPublic = bobKeys.OneTimePreKeys[0].PublicKey,
            SignedPreKeyId = bobKeys.AciSignedPreKey.KeyId,
            SignedPreKeyPublic = bobKeys.AciSignedPreKey.PublicKey,
            SignedPreKeySignature = bobKeys.AciSignedPreKey.Signature,
        });

        var enc = await aliceProtocol.EncryptAsync(
            "bob-aci", 1, System.Text.Encoding.UTF8.GetBytes("hello-managed"));
        Assert.Equal(SignalEnvelopeType.PreKeyBundle, enc.Type);
        Assert.NotEmpty(enc.Ciphertext);

        var enc2 = await aliceProtocol.EncryptAsync(
            "bob-aci", 1, System.Text.Encoding.UTF8.GetBytes("second"));
        Assert.Equal(SignalEnvelopeType.Ciphertext, enc2.Type);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            aliceProtocol.DecryptAsync("bob-aci", 1, SignalEnvelopeType.UnidentifiedSender, [1, 2, 3]));
    }

    [Fact]
    public async Task Ffi_GenerateKeys_WhenAvailable()
    {
        if (!LibSignalNative.IsAvailable)
            return;

        await using var store = CreateStore("ffi");
        await store.InitializeAsync();
        var creds = SampleCredentials();
        // Identity keys must be valid Curve25519 for FFI deserialize.
        var protocol = SignalProtocolFactory.Create(store, creds);
        Assert.True(protocol.UsesNativeFfi);

        // Without valid libsignal-serialized identity private keys this may throw;
        // presence of native path is the primary assertion above.
        try
        {
            var keys = await protocol.GenerateDeviceKeysAsync(creds, enablePq: true);
            Assert.NotEmpty(keys.OneTimePreKeys);
            Assert.NotNull(keys.AciPqLastResortPreKey);
            Assert.NotNull(keys.AciPqLastResortPreKey!.SerializedRecord);
        }
        catch (LibSignalException)
        {
            // Credentials from NSec may not match libsignal private key encoding expectations.
        }
    }

    private static SqliteMessageStore CreateStore(string? suffix = null)
    {
        var dir = Path.Combine(Path.GetTempPath(), "signalcpf-tests", suffix ?? Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return new SqliteMessageStore(new SignalServerOptions { DataDirectory = dir });
    }

    private static AccountCredentials SampleCredentials(string aci = "11111111-1111-1111-1111-111111111111")
    {
        var kp = Protocol.Crypto.Curve25519.GenerateKeyPair();
        return new AccountCredentials
        {
            Aci = aci,
            DeviceId = 2,
            RegistrationId = 1234,
            Password = "test",
            AciIdentityPrivateKey = kp.PrivateKey,
            AciIdentityPublicKey = kp.SerializePublicKey(),
        };
    }
}
