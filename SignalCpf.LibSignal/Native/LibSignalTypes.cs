using System.Runtime.InteropServices;

namespace SignalCpf.LibSignal.Native;

/// <summary>Wire / envelope message types used by Signal servers.</summary>
public static class SignalEnvelopeType
{
    public const int Ciphertext = 1;
    public const int PreKeyBundle = 3;
    public const int UnidentifiedSender = 6;
}

/// <summary>libsignal CiphertextMessageType (distinct from envelope types).</summary>
public static class SignalCiphertextMessageType
{
    public const int Whisper = 2;
    public const int PreKey = 3;
    public const int SenderKey = 7;
    public const int Plaintext = 8;
}

[StructLayout(LayoutKind.Sequential)]
public struct SignalBorrowedBuffer
{
    public IntPtr Base;
    public nuint Length;

    public static unsafe SignalBorrowedBuffer From(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return new SignalBorrowedBuffer { Base = IntPtr.Zero, Length = 0 };
        fixed (byte* p = data)
        {
            // Caller must keep the span alive for the duration of the native call.
            return new SignalBorrowedBuffer { Base = (IntPtr)p, Length = (nuint)data.Length };
        }
    }
}

[StructLayout(LayoutKind.Sequential)]
public struct SignalOwnedBuffer
{
    public IntPtr Base;
    public nuint Length;
}

[StructLayout(LayoutKind.Sequential)]
public struct SignalMutPointer
{
    public IntPtr Raw;
}

[StructLayout(LayoutKind.Sequential)]
public struct SignalConstPointer
{
    public IntPtr Raw;

    public static SignalConstPointer From(SignalMutPointer m) => new() { Raw = m.Raw };
    public static SignalConstPointer From(IntPtr p) => new() { Raw = p };
}

[StructLayout(LayoutKind.Sequential)]
public struct SignalPairPrivatePublic
{
    public SignalMutPointer First;  // PrivateKey
    public SignalMutPointer Second; // PublicKey
}

// --- Store vtables (must match signal_ffi.h layout) ---

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int IdentityGetLocalKeyPairFn(IntPtr ctx, SignalPairPrivatePublic* outPair);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int IdentityGetLocalRegistrationIdFn(IntPtr ctx, uint* outId);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int IdentityGetKeyFn(IntPtr ctx, SignalMutPointer* outPublicKey, SignalMutPointer address);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int IdentitySaveKeyFn(IntPtr ctx, byte* outChange, SignalMutPointer address, SignalMutPointer publicKey);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int IdentityIsTrustedFn(IntPtr ctx, byte* outTrusted, SignalMutPointer address, SignalMutPointer publicKey, uint direction);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public delegate void StoreDestroyFn(IntPtr ctx);

[StructLayout(LayoutKind.Sequential)]
public struct SignalFfiIdentityKeyStore
{
    public IntPtr Ctx;
    public IntPtr GetLocalIdentityKeyPair;
    public IntPtr GetLocalRegistrationId;
    public IntPtr GetIdentityKey;
    public IntPtr SaveIdentityKey;
    public IntPtr IsTrustedIdentity;
    public IntPtr Destroy;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int SessionLoadFn(IntPtr ctx, SignalMutPointer* outRecord, SignalMutPointer address);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int SessionStoreFn(IntPtr ctx, SignalMutPointer address, SignalMutPointer record);

[StructLayout(LayoutKind.Sequential)]
public struct SignalFfiSessionStore
{
    public IntPtr Ctx;
    public IntPtr LoadSession;
    public IntPtr StoreSession;
    public IntPtr Destroy;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int PreKeyLoadFn(IntPtr ctx, SignalMutPointer* outRecord, uint id);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int PreKeyStoreFn(IntPtr ctx, uint id, SignalMutPointer record);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int PreKeyRemoveFn(IntPtr ctx, uint id);

[StructLayout(LayoutKind.Sequential)]
public struct SignalFfiPreKeyStore
{
    public IntPtr Ctx;
    public IntPtr LoadPreKey;
    public IntPtr StorePreKey;
    public IntPtr RemovePreKey;
    public IntPtr Destroy;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int SignedPreKeyLoadFn(IntPtr ctx, SignalMutPointer* outRecord, uint id);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int SignedPreKeyStoreFn(IntPtr ctx, uint id, SignalMutPointer record);

[StructLayout(LayoutKind.Sequential)]
public struct SignalFfiSignedPreKeyStore
{
    public IntPtr Ctx;
    public IntPtr LoadSignedPreKey;
    public IntPtr StoreSignedPreKey;
    public IntPtr Destroy;
}

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int KyberLoadFn(IntPtr ctx, SignalMutPointer* outRecord, uint id);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int KyberStoreFn(IntPtr ctx, uint id, SignalMutPointer record);

[UnmanagedFunctionPointer(CallingConvention.Cdecl)]
public unsafe delegate int KyberMarkUsedFn(IntPtr ctx, uint id, uint signedPreKeyId, SignalMutPointer baseKey);

[StructLayout(LayoutKind.Sequential)]
public struct SignalFfiKyberPreKeyStore
{
    public IntPtr Ctx;
    public IntPtr LoadKyberPreKey;
    public IntPtr StoreKyberPreKey;
    public IntPtr MarkKyberPreKeyUsed;
    public IntPtr Destroy;
}

[StructLayout(LayoutKind.Sequential)]
public struct SignalConstPointerStore
{
    public IntPtr Raw;
}
