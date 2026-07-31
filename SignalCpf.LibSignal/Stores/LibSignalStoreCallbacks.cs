using System.Runtime.InteropServices;
using SignalCpf.LibSignal.Native;
using SignalCpf.Storage;

namespace SignalCpf.LibSignal.Stores;

/// <summary>
/// Pins managed store state and builds libsignal FFI store vtables for the duration of a call.
/// </summary>
public sealed unsafe class LibSignalStoreContext : IDisposable
{
    private readonly IMessageStore _store;
    private readonly AccountCredentials _credentials;
    private GCHandle _selfHandle;
    private bool _disposed;

    // Keep delegates alive for the lifetime of this context.
    private readonly IdentityGetLocalKeyPairFn _getLocalKeyPair;
    private readonly IdentityGetLocalRegistrationIdFn _getLocalRegId;
    private readonly IdentityGetKeyFn _getIdentity;
    private readonly IdentitySaveKeyFn _saveIdentity;
    private readonly IdentityIsTrustedFn _isTrusted;
    private readonly SessionLoadFn _loadSession;
    private readonly SessionStoreFn _storeSession;
    private readonly PreKeyLoadFn _loadPreKey;
    private readonly PreKeyStoreFn _storePreKey;
    private readonly PreKeyRemoveFn _removePreKey;
    private readonly SignedPreKeyLoadFn _loadSigned;
    private readonly SignedPreKeyStoreFn _storeSigned;
    private readonly KyberLoadFn _loadKyber;
    private readonly KyberStoreFn _storeKyber;
    private readonly KyberMarkUsedFn _markKyber;
    private readonly StoreDestroyFn _noopDestroy;

    public LibSignalStoreContext(IMessageStore store, AccountCredentials credentials)
    {
        _store = store;
        _credentials = credentials;
        _selfHandle = GCHandle.Alloc(this);

        _getLocalKeyPair = GetLocalIdentityKeyPair;
        _getLocalRegId = GetLocalRegistrationId;
        _getIdentity = GetIdentityKey;
        _saveIdentity = SaveIdentityKey;
        _isTrusted = IsTrustedIdentity;
        _loadSession = LoadSession;
        _storeSession = StoreSession;
        _loadPreKey = LoadPreKey;
        _storePreKey = StorePreKey;
        _removePreKey = RemovePreKey;
        _loadSigned = LoadSignedPreKey;
        _storeSigned = StoreSignedPreKey;
        _loadKyber = LoadKyberPreKey;
        _storeKyber = StoreKyberPreKey;
        _markKyber = MarkKyberPreKeyUsed;
        _noopDestroy = static _ => { };
    }

    private IntPtr Ctx => GCHandle.ToIntPtr(_selfHandle);

    private static LibSignalStoreContext From(IntPtr ctx) =>
        (LibSignalStoreContext)GCHandle.FromIntPtr(ctx).Target!;

    public SignalFfiIdentityKeyStore CreateIdentityStore() => new()
    {
        Ctx = Ctx,
        GetLocalIdentityKeyPair = Marshal.GetFunctionPointerForDelegate(_getLocalKeyPair),
        GetLocalRegistrationId = Marshal.GetFunctionPointerForDelegate(_getLocalRegId),
        GetIdentityKey = Marshal.GetFunctionPointerForDelegate(_getIdentity),
        SaveIdentityKey = Marshal.GetFunctionPointerForDelegate(_saveIdentity),
        IsTrustedIdentity = Marshal.GetFunctionPointerForDelegate(_isTrusted),
        Destroy = Marshal.GetFunctionPointerForDelegate(_noopDestroy),
    };

    public SignalFfiSessionStore CreateSessionStore() => new()
    {
        Ctx = Ctx,
        LoadSession = Marshal.GetFunctionPointerForDelegate(_loadSession),
        StoreSession = Marshal.GetFunctionPointerForDelegate(_storeSession),
        Destroy = Marshal.GetFunctionPointerForDelegate(_noopDestroy),
    };

    public SignalFfiPreKeyStore CreatePreKeyStore() => new()
    {
        Ctx = Ctx,
        LoadPreKey = Marshal.GetFunctionPointerForDelegate(_loadPreKey),
        StorePreKey = Marshal.GetFunctionPointerForDelegate(_storePreKey),
        RemovePreKey = Marshal.GetFunctionPointerForDelegate(_removePreKey),
        Destroy = Marshal.GetFunctionPointerForDelegate(_noopDestroy),
    };

    public SignalFfiSignedPreKeyStore CreateSignedPreKeyStore() => new()
    {
        Ctx = Ctx,
        LoadSignedPreKey = Marshal.GetFunctionPointerForDelegate(_loadSigned),
        StoreSignedPreKey = Marshal.GetFunctionPointerForDelegate(_storeSigned),
        Destroy = Marshal.GetFunctionPointerForDelegate(_noopDestroy),
    };

    public SignalFfiKyberPreKeyStore CreateKyberPreKeyStore() => new()
    {
        Ctx = Ctx,
        LoadKyberPreKey = Marshal.GetFunctionPointerForDelegate(_loadKyber),
        StoreKyberPreKey = Marshal.GetFunctionPointerForDelegate(_storeKyber),
        MarkKyberPreKeyUsed = Marshal.GetFunctionPointerForDelegate(_markKyber),
        Destroy = Marshal.GetFunctionPointerForDelegate(_noopDestroy),
    };

    private static int Catch(Func<int> body)
    {
        try
        {
            return body();
        }
        catch
        {
            return -1;
        }
    }

    private static int GetLocalIdentityKeyPair(IntPtr ctx, SignalPairPrivatePublic* outPair) => Catch(() =>
    {
        var self = From(ctx);
        var priv = LibSignalInterop.DeserializePrivateKey(self._credentials.AciIdentityPrivateKey);
        LibSignalInterop.Check(LibSignalNative.PrivateKeyGetPublicKey(out var pub, SignalConstPointer.From(priv)));
        *outPair = new SignalPairPrivatePublic { First = priv, Second = pub };
        return 0;
    });

    private static int GetLocalRegistrationId(IntPtr ctx, uint* outId) => Catch(() =>
    {
        *outId = (uint)From(ctx)._credentials.RegistrationId;
        return 0;
    });

    private static int GetIdentityKey(IntPtr ctx, SignalMutPointer* outPublicKey, SignalMutPointer address) => Catch(() =>
    {
        var self = From(ctx);
        // Callback takes ownership of address.
        var name = LibSignalInterop.GetAddressName(SignalConstPointer.From(address));
        LibSignalNative.AddressDestroy(address);

        var key = self._store.LoadIdentityAsync(name).GetAwaiter().GetResult();
        if (key is null || key.Length == 0)
        {
            *outPublicKey = default;
            return 0;
        }

        // Identity keys in store may be 32-byte raw or 33-byte DJB; deserialize accepts both via publickey_deserialize.
        var withType = EnsureDjbTypeByte(key);
        *outPublicKey = LibSignalInterop.DeserializePublicKey(withType);
        return 0;
    });

    private static int SaveIdentityKey(IntPtr ctx, byte* outChange, SignalMutPointer address, SignalMutPointer publicKey) => Catch(() =>
    {
        var self = From(ctx);
        var name = LibSignalInterop.GetAddressName(SignalConstPointer.From(address));
        LibSignalNative.AddressDestroy(address);

        var serialized = LibSignalInterop.SerializePublicKey(SignalConstPointer.From(publicKey));
        LibSignalNative.PublicKeyDestroy(publicKey);

        var existing = self._store.LoadIdentityAsync(name).GetAwaiter().GetResult();
        self._store.SaveIdentityAsync(name, serialized).GetAwaiter().GetResult();
        *outChange = existing is null || existing.SequenceEqual(serialized) ? (byte)0 : (byte)1;
        return 0;
    });

    private static int IsTrustedIdentity(IntPtr ctx, byte* outTrusted, SignalMutPointer address, SignalMutPointer publicKey, uint direction) => Catch(() =>
    {
        var self = From(ctx);
        var name = LibSignalInterop.GetAddressName(SignalConstPointer.From(address));
        LibSignalNative.AddressDestroy(address);

        var serialized = LibSignalInterop.SerializePublicKey(SignalConstPointer.From(publicKey));
        LibSignalNative.PublicKeyDestroy(publicKey);

        var existing = self._store.LoadIdentityAsync(name).GetAwaiter().GetResult();
        // TOFU: trust if missing or equal.
        *outTrusted = existing is null || existing.SequenceEqual(serialized) ||
                      (existing.Length == 32 && serialized.Length == 33 && existing.SequenceEqual(serialized.AsSpan(1).ToArray()))
            ? (byte)1
            : (byte)0;
        return 0;
    });

    private static int LoadSession(IntPtr ctx, SignalMutPointer* outRecord, SignalMutPointer address) => Catch(() =>
    {
        var self = From(ctx);
        var name = LibSignalInterop.GetAddressName(SignalConstPointer.From(address));
        var deviceId = LibSignalInterop.GetAddressDeviceId(SignalConstPointer.From(address));
        LibSignalNative.AddressDestroy(address);

        var key = $"{name}.{deviceId}";
        var raw = self._store.LoadSessionAsync(key).GetAwaiter().GetResult();
        if (raw is null || raw.Length == 0 || LooksLikeManagedJson(raw))
        {
            *outRecord = default;
            return 0;
        }

        LibSignalInterop.WithBorrowedBuffer(raw, buf =>
        {
            LibSignalInterop.Check(LibSignalNative.SessionRecordDeserialize(out var rec, buf));
            *outRecord = rec;
        });
        return 0;
    });

    private static int StoreSession(IntPtr ctx, SignalMutPointer address, SignalMutPointer record) => Catch(() =>
    {
        var self = From(ctx);
        var name = LibSignalInterop.GetAddressName(SignalConstPointer.From(address));
        var deviceId = LibSignalInterop.GetAddressDeviceId(SignalConstPointer.From(address));
        LibSignalNative.AddressDestroy(address);

        LibSignalInterop.Check(LibSignalNative.SessionRecordSerialize(out var buf, SignalConstPointer.From(record)));
        var bytes = LibSignalInterop.TakeBuffer(ref buf);
        LibSignalNative.SessionRecordDestroy(record);

        self._store.SaveSessionAsync($"{name}.{deviceId}", bytes).GetAwaiter().GetResult();
        return 0;
    });

    private static int LoadPreKey(IntPtr ctx, SignalMutPointer* outRecord, uint id) => Catch(() =>
    {
        var raw = From(ctx)._store.LoadPreKeyAsync(id).GetAwaiter().GetResult()
                  ?? throw new InvalidOperationException($"Missing prekey {id}");
        LibSignalInterop.WithBorrowedBuffer(raw, buf =>
        {
            LibSignalInterop.Check(LibSignalNative.PreKeyRecordDeserialize(out var rec, buf));
            *outRecord = rec;
        });
        return 0;
    });

    private static int StorePreKey(IntPtr ctx, uint id, SignalMutPointer record) => Catch(() =>
    {
        LibSignalInterop.Check(LibSignalNative.PreKeyRecordSerialize(out var buf, SignalConstPointer.From(record)));
        var bytes = LibSignalInterop.TakeBuffer(ref buf);
        LibSignalNative.PreKeyRecordDestroy(record);
        From(ctx)._store.SavePreKeyAsync(id, bytes).GetAwaiter().GetResult();
        return 0;
    });

    private static int RemovePreKey(IntPtr ctx, uint id) => Catch(() =>
    {
        From(ctx)._store.RemovePreKeyAsync(id).GetAwaiter().GetResult();
        return 0;
    });

    private static int LoadSignedPreKey(IntPtr ctx, SignalMutPointer* outRecord, uint id) => Catch(() =>
    {
        var raw = From(ctx)._store.LoadSignedPreKeyAsync(id).GetAwaiter().GetResult()
                  ?? throw new InvalidOperationException($"Missing signed prekey {id}");
        LibSignalInterop.WithBorrowedBuffer(raw, buf =>
        {
            LibSignalInterop.Check(LibSignalNative.SignedPreKeyRecordDeserialize(out var rec, buf));
            *outRecord = rec;
        });
        return 0;
    });

    private static int StoreSignedPreKey(IntPtr ctx, uint id, SignalMutPointer record) => Catch(() =>
    {
        LibSignalInterop.Check(LibSignalNative.SignedPreKeyRecordSerialize(out var buf, SignalConstPointer.From(record)));
        var bytes = LibSignalInterop.TakeBuffer(ref buf);
        LibSignalNative.SignedPreKeyRecordDestroy(record);
        From(ctx)._store.SaveSignedPreKeyAsync(id, bytes).GetAwaiter().GetResult();
        return 0;
    });

    private static int LoadKyberPreKey(IntPtr ctx, SignalMutPointer* outRecord, uint id) => Catch(() =>
    {
        var raw = From(ctx)._store.LoadKyberPreKeyAsync(id).GetAwaiter().GetResult()
                  ?? throw new InvalidOperationException($"Missing kyber prekey {id}");
        LibSignalInterop.WithBorrowedBuffer(raw, buf =>
        {
            LibSignalInterop.Check(LibSignalNative.KyberPreKeyRecordDeserialize(out var rec, buf));
            *outRecord = rec;
        });
        return 0;
    });

    private static int StoreKyberPreKey(IntPtr ctx, uint id, SignalMutPointer record) => Catch(() =>
    {
        LibSignalInterop.Check(LibSignalNative.KyberPreKeyRecordSerialize(out var buf, SignalConstPointer.From(record)));
        var bytes = LibSignalInterop.TakeBuffer(ref buf);
        LibSignalNative.KyberPreKeyRecordDestroy(record);
        From(ctx)._store.SaveKyberPreKeyAsync(id, bytes).GetAwaiter().GetResult();
        return 0;
    });

    private static int MarkKyberPreKeyUsed(IntPtr ctx, uint id, uint signedPreKeyId, SignalMutPointer baseKey) => Catch(() =>
    {
        LibSignalNative.PublicKeyDestroy(baseKey);
        // Last-resort keys are reusable; one-time kyber keys can be removed.
        // Keep last-resort (id typically small); remove higher one-time ids.
        if (id > 1)
            From(ctx)._store.RemoveKyberPreKeyAsync(id).GetAwaiter().GetResult();
        return 0;
    });

    private static byte[] EnsureDjbTypeByte(byte[] key)
    {
        if (key.Length == 33 && key[0] == 0x05)
            return key;
        if (key.Length == 32)
        {
            var with = new byte[33];
            with[0] = 0x05;
            Buffer.BlockCopy(key, 0, with, 1, 32);
            return with;
        }

        return key;
    }

    private static bool LooksLikeManagedJson(byte[] raw) =>
        raw.Length > 0 && raw[0] == (byte)'{';

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_selfHandle.IsAllocated)
            _selfHandle.Free();
        GC.KeepAlive(_getLocalKeyPair);
        GC.KeepAlive(_getLocalRegId);
        GC.KeepAlive(_getIdentity);
        GC.KeepAlive(_saveIdentity);
        GC.KeepAlive(_isTrusted);
        GC.KeepAlive(_loadSession);
        GC.KeepAlive(_storeSession);
        GC.KeepAlive(_loadPreKey);
        GC.KeepAlive(_storePreKey);
        GC.KeepAlive(_removePreKey);
        GC.KeepAlive(_loadSigned);
        GC.KeepAlive(_storeSigned);
        GC.KeepAlive(_loadKyber);
        GC.KeepAlive(_storeKyber);
        GC.KeepAlive(_markKyber);
        GC.KeepAlive(_noopDestroy);
    }
}

/// <summary>Pins store structs on the stack and exposes const pointers for one FFI call.</summary>
public readonly unsafe ref struct PinnedStores
{
    private readonly LibSignalStoreContext _ctx;
    private readonly SignalFfiIdentityKeyStore _identity;
    private readonly SignalFfiSessionStore _session;
    private readonly SignalFfiPreKeyStore _preKey;
    private readonly SignalFfiSignedPreKeyStore _signed;
    private readonly SignalFfiKyberPreKeyStore _kyber;

    public PinnedStores(LibSignalStoreContext ctx)
    {
        _ctx = ctx;
        _identity = ctx.CreateIdentityStore();
        _session = ctx.CreateSessionStore();
        _preKey = ctx.CreatePreKeyStore();
        _signed = ctx.CreateSignedPreKeyStore();
        _kyber = ctx.CreateKyberPreKeyStore();
    }

    public void WithPointers(StorePointersAction action)
    {
        fixed (SignalFfiIdentityKeyStore* identity = &_identity)
        fixed (SignalFfiSessionStore* session = &_session)
        fixed (SignalFfiPreKeyStore* preKey = &_preKey)
        fixed (SignalFfiSignedPreKeyStore* signed = &_signed)
        fixed (SignalFfiKyberPreKeyStore* kyber = &_kyber)
        {
            action(
                new SignalConstPointerStore { Raw = (IntPtr)identity },
                new SignalConstPointerStore { Raw = (IntPtr)session },
                new SignalConstPointerStore { Raw = (IntPtr)preKey },
                new SignalConstPointerStore { Raw = (IntPtr)signed },
                new SignalConstPointerStore { Raw = (IntPtr)kyber });
        }

        // Keep store callbacks / ctx alive across the native call.
        GC.KeepAlive(_ctx);
    }
}

public unsafe delegate void StorePointersAction(
    SignalConstPointerStore identity,
    SignalConstPointerStore session,
    SignalConstPointerStore preKey,
    SignalConstPointerStore signedPreKey,
    SignalConstPointerStore kyber);
