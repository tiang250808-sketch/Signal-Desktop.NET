using System.Runtime.InteropServices;

namespace SignalCpf.LibSignal.Native;

/// <summary>
/// P/Invoke surface for libsignal_ffi (aligned with signal_ffi.h / Signal-iOS bridge).
/// Place signal_ffi.dll / libsignal_ffi.so / libsignal_ffi.dylib next to the app binary.
/// </summary>
public static class LibSignalNative
{
    public const string LibraryName = "signal_ffi";

    public static bool IsAvailable { get; } = Probe();

    private static bool Probe()
    {
        try
        {
            return NativeLibrary.TryLoad(LibraryName, typeof(LibSignalNative).Assembly, null, out _)
                   || NativeLibrary.TryLoad("libsignal_ffi", typeof(LibSignalNative).Assembly, null, out _);
        }
        catch
        {
            return false;
        }
    }

    // --- Error / buffer ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_error_free")]
    public static extern void ErrorFree(IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_error_get_message")]
    public static extern IntPtr ErrorGetMessage(out IntPtr outMessage, IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_error_get_type")]
    public static extern uint ErrorGetType(IntPtr error);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_free_string")]
    public static extern void FreeString(IntPtr s);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_free_buffer")]
    public static extern void FreeBuffer(IntPtr buf, nuint bufLen);

    // --- Protocol address ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_address_new")]
    public static extern IntPtr AddressNew(out SignalMutPointer outAddr, IntPtr nameUtf8, uint deviceId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_address_destroy")]
    public static extern IntPtr AddressDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_address_get_name")]
    public static extern IntPtr AddressGetName(out IntPtr outName, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_address_get_device_id")]
    public static extern IntPtr AddressGetDeviceId(out uint outId, SignalConstPointer obj);

    // --- Keys ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_privatekey_generate")]
    public static extern IntPtr PrivateKeyGenerate(out SignalMutPointer outKey);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_privatekey_deserialize")]
    public static extern IntPtr PrivateKeyDeserialize(out SignalMutPointer outKey, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_privatekey_destroy")]
    public static extern IntPtr PrivateKeyDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_privatekey_serialize")]
    public static extern IntPtr PrivateKeySerialize(out SignalOwnedBuffer outBuf, SignalConstPointer key);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_privatekey_get_public_key")]
    public static extern IntPtr PrivateKeyGetPublicKey(out SignalMutPointer outPub, SignalConstPointer key);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_privatekey_sign")]
    public static extern IntPtr PrivateKeySign(out SignalOwnedBuffer outBuf, SignalConstPointer key, SignalBorrowedBuffer message);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_publickey_deserialize")]
    public static extern IntPtr PublicKeyDeserialize(out SignalMutPointer outKey, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_publickey_destroy")]
    public static extern IntPtr PublicKeyDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_publickey_serialize")]
    public static extern IntPtr PublicKeySerialize(out SignalOwnedBuffer outBuf, SignalConstPointer key);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_publickey_clone")]
    public static extern IntPtr PublicKeyClone(out SignalMutPointer outKey, SignalConstPointer key);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_privatekey_clone")]
    public static extern IntPtr PrivateKeyClone(out SignalMutPointer outKey, SignalConstPointer key);

    // --- Kyber ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_key_pair_generate")]
    public static extern IntPtr KyberKeyPairGenerate(out SignalMutPointer outPair);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_key_pair_destroy")]
    public static extern IntPtr KyberKeyPairDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_key_pair_get_public_key")]
    public static extern IntPtr KyberKeyPairGetPublicKey(out SignalMutPointer outPub, SignalConstPointer pair);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_public_key_serialize")]
    public static extern IntPtr KyberPublicKeySerialize(out SignalOwnedBuffer outBuf, SignalConstPointer key);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_public_key_deserialize")]
    public static extern IntPtr KyberPublicKeyDeserialize(out SignalMutPointer outKey, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_public_key_destroy")]
    public static extern IntPtr KyberPublicKeyDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_pre_key_record_new")]
    public static extern IntPtr KyberPreKeyRecordNew(
        out SignalMutPointer outRec,
        uint id,
        ulong timestamp,
        SignalConstPointer keyPair,
        SignalBorrowedBuffer signature);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_pre_key_record_serialize")]
    public static extern IntPtr KyberPreKeyRecordSerialize(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_pre_key_record_deserialize")]
    public static extern IntPtr KyberPreKeyRecordDeserialize(out SignalMutPointer outRec, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_pre_key_record_destroy")]
    public static extern IntPtr KyberPreKeyRecordDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_pre_key_record_get_public_key")]
    public static extern IntPtr KyberPreKeyRecordGetPublicKey(out SignalMutPointer outPub, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_pre_key_record_get_signature")]
    public static extern IntPtr KyberPreKeyRecordGetSignature(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_kyber_pre_key_record_clone")]
    public static extern IntPtr KyberPreKeyRecordClone(out SignalMutPointer outRec, SignalConstPointer obj);

    // --- PreKey / SignedPreKey records ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_record_new")]
    public static extern IntPtr PreKeyRecordNew(
        out SignalMutPointer outRec,
        uint id,
        SignalConstPointer pubKey,
        SignalConstPointer privKey);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_record_serialize")]
    public static extern IntPtr PreKeyRecordSerialize(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_record_deserialize")]
    public static extern IntPtr PreKeyRecordDeserialize(out SignalMutPointer outRec, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_record_destroy")]
    public static extern IntPtr PreKeyRecordDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_record_clone")]
    public static extern IntPtr PreKeyRecordClone(out SignalMutPointer outRec, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_record_get_public_key")]
    public static extern IntPtr PreKeyRecordGetPublicKey(out SignalMutPointer outPub, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_signed_pre_key_record_new")]
    public static extern IntPtr SignedPreKeyRecordNew(
        out SignalMutPointer outRec,
        uint id,
        ulong timestamp,
        SignalConstPointer pubKey,
        SignalConstPointer privKey,
        SignalBorrowedBuffer signature);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_signed_pre_key_record_serialize")]
    public static extern IntPtr SignedPreKeyRecordSerialize(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_signed_pre_key_record_deserialize")]
    public static extern IntPtr SignedPreKeyRecordDeserialize(out SignalMutPointer outRec, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_signed_pre_key_record_destroy")]
    public static extern IntPtr SignedPreKeyRecordDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_signed_pre_key_record_clone")]
    public static extern IntPtr SignedPreKeyRecordClone(out SignalMutPointer outRec, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_signed_pre_key_record_get_public_key")]
    public static extern IntPtr SignedPreKeyRecordGetPublicKey(out SignalMutPointer outPub, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_signed_pre_key_record_get_signature")]
    public static extern IntPtr SignedPreKeyRecordGetSignature(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    // --- Session / PreKeyBundle / Cipher ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_session_record_deserialize")]
    public static extern IntPtr SessionRecordDeserialize(out SignalMutPointer outRec, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_session_record_serialize")]
    public static extern IntPtr SessionRecordSerialize(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_session_record_destroy")]
    public static extern IntPtr SessionRecordDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_session_record_clone")]
    public static extern IntPtr SessionRecordClone(out SignalMutPointer outRec, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_session_record_get_remote_registration_id")]
    public static extern IntPtr SessionRecordGetRemoteRegistrationId(out uint outId, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_bundle_new")]
    public static extern IntPtr PreKeyBundleNew(
        out SignalMutPointer outBundle,
        uint registrationId,
        uint deviceId,
        uint prekeyId,
        SignalConstPointer prekey,
        uint signedPrekeyId,
        SignalConstPointer signedPrekey,
        SignalBorrowedBuffer signedPrekeySignature,
        SignalConstPointer identityKey,
        uint kyberPrekeyId,
        SignalConstPointer kyberPrekey,
        SignalBorrowedBuffer kyberPrekeySignature);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_bundle_destroy")]
    public static extern IntPtr PreKeyBundleDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_process_prekey_bundle")]
    public static extern IntPtr ProcessPrekeyBundle(
        SignalConstPointer bundle,
        SignalConstPointer protocolAddress,
        SignalConstPointer localAddress,
        SignalConstPointerStore sessionStore,
        SignalConstPointerStore identityKeyStore,
        ulong now);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_encrypt_message")]
    public static extern IntPtr EncryptMessage(
        out SignalMutPointer outMsg,
        SignalBorrowedBuffer ptext,
        SignalConstPointer protocolAddress,
        SignalConstPointer localAddress,
        SignalConstPointerStore sessionStore,
        SignalConstPointerStore identityKeyStore,
        ulong now);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_ciphertext_message_serialize")]
    public static extern IntPtr CiphertextMessageSerialize(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_ciphertext_message_type")]
    public static extern IntPtr CiphertextMessageType(out uint outType, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_ciphertext_message_destroy")]
    public static extern IntPtr CiphertextMessageDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_message_deserialize")]
    public static extern IntPtr MessageDeserialize(out SignalMutPointer outMsg, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_message_destroy")]
    public static extern IntPtr MessageDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_decrypt_message")]
    public static extern IntPtr DecryptMessage(
        out SignalOwnedBuffer outBuf,
        SignalConstPointer message,
        SignalConstPointer protocolAddress,
        SignalConstPointer localAddress,
        SignalConstPointerStore sessionStore,
        SignalConstPointerStore identityKeyStore);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_signal_message_deserialize")]
    public static extern IntPtr PreKeySignalMessageDeserialize(out SignalMutPointer outMsg, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_pre_key_signal_message_destroy")]
    public static extern IntPtr PreKeySignalMessageDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_decrypt_pre_key_message")]
    public static extern IntPtr DecryptPreKeyMessage(
        out SignalOwnedBuffer outBuf,
        SignalConstPointer message,
        SignalConstPointer protocolAddress,
        SignalConstPointer localAddress,
        SignalConstPointerStore sessionStore,
        SignalConstPointerStore identityKeyStore,
        SignalConstPointerStore prekeyStore,
        SignalConstPointerStore signedPrekeyStore,
        SignalConstPointerStore kyberPrekeyStore);

    // --- Sealed sender ---

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_sender_certificate_deserialize")]
    public static extern IntPtr SenderCertificateDeserialize(out SignalMutPointer outCert, SignalBorrowedBuffer data);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_sender_certificate_destroy")]
    public static extern IntPtr SenderCertificateDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_sender_certificate_get_sender_uuid")]
    public static extern IntPtr SenderCertificateGetSenderUuid(out IntPtr outUuid, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_sender_certificate_get_device_id")]
    public static extern IntPtr SenderCertificateGetDeviceId(out uint outId, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_unidentified_sender_message_content_new")]
    public static extern IntPtr UsmcNew(
        out SignalMutPointer outContent,
        SignalConstPointer message,
        SignalConstPointer sender,
        uint contentHint,
        SignalBorrowedBuffer groupId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_unidentified_sender_message_content_new_from_content_and_type")]
    public static extern IntPtr UsmcNewFromContentAndType(
        out SignalMutPointer outContent,
        SignalBorrowedBuffer message,
        uint msgType,
        SignalConstPointer sender,
        uint contentHint,
        SignalBorrowedBuffer groupId);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_unidentified_sender_message_content_destroy")]
    public static extern IntPtr UsmcDestroy(SignalMutPointer p);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_unidentified_sender_message_content_get_contents")]
    public static extern IntPtr UsmcGetContents(out SignalOwnedBuffer outBuf, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_unidentified_sender_message_content_get_msg_type")]
    public static extern IntPtr UsmcGetMsgType(out uint outType, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_unidentified_sender_message_content_get_sender_cert")]
    public static extern IntPtr UsmcGetSenderCert(out SignalMutPointer outCert, SignalConstPointer obj);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_sealed_session_cipher_encrypt")]
    public static extern IntPtr SealedEncrypt(
        out SignalOwnedBuffer outBuf,
        SignalConstPointer destination,
        SignalConstPointer content,
        SignalConstPointerStore identityKeyStore);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_sealed_session_cipher_decrypt_to_usmc")]
    public static extern IntPtr SealedDecryptToUsmc(
        out SignalMutPointer outContent,
        SignalBorrowedBuffer ctext,
        SignalConstPointerStore identityStore);

    [DllImport(LibraryName, CallingConvention = CallingConvention.Cdecl, EntryPoint = "signal_profile_key_derive_access_key")]
    public static extern IntPtr ProfileKeyDeriveAccessKey(IntPtr out16, IntPtr profileKey32);
}
