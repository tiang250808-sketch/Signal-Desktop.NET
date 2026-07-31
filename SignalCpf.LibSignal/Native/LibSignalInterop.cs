using System.Runtime.InteropServices;
using System.Text;

namespace SignalCpf.LibSignal.Native;

public sealed class LibSignalException : Exception
{
    public uint ErrorType { get; }

    public LibSignalException(string message, uint errorType = 0)
        : base(message)
    {
        ErrorType = errorType;
    }
}

public static class LibSignalInterop
{
    public static void Check(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return;

        uint type = 0;
        string message = "libsignal error";
        try
        {
            type = LibSignalNative.ErrorGetType(error);
            var getMsgErr = LibSignalNative.ErrorGetMessage(out var msgPtr, error);
            if (getMsgErr == IntPtr.Zero && msgPtr != IntPtr.Zero)
            {
                message = Marshal.PtrToStringUTF8(msgPtr) ?? message;
                LibSignalNative.FreeString(msgPtr);
            }
            else if (getMsgErr != IntPtr.Zero)
            {
                LibSignalNative.ErrorFree(getMsgErr);
            }
        }
        finally
        {
            LibSignalNative.ErrorFree(error);
        }

        throw new LibSignalException(message, type);
    }

    public static byte[] TakeBuffer(ref SignalOwnedBuffer owned)
    {
        if (owned.Base == IntPtr.Zero || owned.Length == 0)
            return [];

        var bytes = new byte[(int)owned.Length];
        Marshal.Copy(owned.Base, bytes, 0, bytes.Length);
        LibSignalNative.FreeBuffer(owned.Base, owned.Length);
        owned = default;
        return bytes;
    }

    public static unsafe T WithBorrowedBuffer<T>(ReadOnlySpan<byte> data, Func<SignalBorrowedBuffer, T> body)
    {
        if (data.IsEmpty)
            return body(new SignalBorrowedBuffer { Base = IntPtr.Zero, Length = 0 });

        fixed (byte* p = data)
        {
            var buf = new SignalBorrowedBuffer { Base = (IntPtr)p, Length = (nuint)data.Length };
            return body(buf);
        }
    }

    public static unsafe void WithBorrowedBuffer(ReadOnlySpan<byte> data, Action<SignalBorrowedBuffer> body)
    {
        if (data.IsEmpty)
        {
            body(new SignalBorrowedBuffer { Base = IntPtr.Zero, Length = 0 });
            return;
        }

        fixed (byte* p = data)
        {
            body(new SignalBorrowedBuffer { Base = (IntPtr)p, Length = (nuint)data.Length });
        }
    }

    public static SignalMutPointer NewAddress(string name, uint deviceId)
    {
        var utf8 = Encoding.UTF8.GetBytes(name + "\0");
        unsafe
        {
            fixed (byte* p = utf8)
            {
                Check(LibSignalNative.AddressNew(out var addr, (IntPtr)p, deviceId));
                return addr;
            }
        }
    }

    public static string GetAddressName(SignalConstPointer address)
    {
        Check(LibSignalNative.AddressGetName(out var namePtr, address));
        try
        {
            return Marshal.PtrToStringUTF8(namePtr) ?? "";
        }
        finally
        {
            if (namePtr != IntPtr.Zero)
                LibSignalNative.FreeString(namePtr);
        }
    }

    public static uint GetAddressDeviceId(SignalConstPointer address)
    {
        Check(LibSignalNative.AddressGetDeviceId(out var id, address));
        return id;
    }

    public static SignalMutPointer DeserializePrivateKey(ReadOnlySpan<byte> data) =>
        WithBorrowedBuffer(data, buf =>
        {
            Check(LibSignalNative.PrivateKeyDeserialize(out var key, buf));
            return key;
        });

    public static SignalMutPointer DeserializePublicKey(ReadOnlySpan<byte> data) =>
        WithBorrowedBuffer(data, buf =>
        {
            Check(LibSignalNative.PublicKeyDeserialize(out var key, buf));
            return key;
        });

    public static byte[] SerializePublicKey(SignalConstPointer key)
    {
        Check(LibSignalNative.PublicKeySerialize(out var buf, key));
        return TakeBuffer(ref buf);
    }

    public static byte[] SerializePrivateKey(SignalConstPointer key)
    {
        Check(LibSignalNative.PrivateKeySerialize(out var buf, key));
        return TakeBuffer(ref buf);
    }

    public static byte[] Sign(SignalConstPointer privateKey, ReadOnlySpan<byte> message) =>
        WithBorrowedBuffer(message, buf =>
        {
            Check(LibSignalNative.PrivateKeySign(out var sig, privateKey, buf));
            return TakeBuffer(ref sig);
        });

    /// <summary>Convert Envelope type to CiphertextMessage type for USMC wrapping.</summary>
    public static int EnvelopeTypeToCiphertextType(int envelopeType) =>
        envelopeType switch
        {
            SignalEnvelopeType.PreKeyBundle => SignalCiphertextMessageType.PreKey,
            SignalEnvelopeType.Ciphertext => SignalCiphertextMessageType.Whisper,
            _ => envelopeType,
        };

    /// <summary>Convert CiphertextMessage type to Envelope type for PUT /v1/messages.</summary>
    public static int CiphertextTypeToEnvelopeType(uint ciphertextType) =>
        ciphertextType switch
        {
            SignalCiphertextMessageType.PreKey => SignalEnvelopeType.PreKeyBundle,
            SignalCiphertextMessageType.Whisper => SignalEnvelopeType.Ciphertext,
            _ => (int)ciphertextType,
        };
}
