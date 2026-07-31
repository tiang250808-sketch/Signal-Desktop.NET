using System.Runtime.InteropServices;
using System.Security.Cryptography;
using SignalCpf.LibSignal.Stores;
using SignalCpf.Storage;

namespace SignalCpf.LibSignal.Native;

/// <summary>
/// Official libsignal FFI-backed protocol service (Double Ratchet + PQXDH + Sealed Sender).
/// </summary>
public sealed class FfiSignalProtocolService : ISignalProtocolService
{
    private readonly IMessageStore _store;
    private readonly AccountCredentials _credentials;
    private readonly object _gate = new();

    public FfiSignalProtocolService(IMessageStore store, AccountCredentials credentials)
    {
        _store = store;
        _credentials = credentials;
    }

    public bool UsesNativeFfi => true;

    public int GenerateRegistrationId() =>
        RandomNumberGenerator.GetInt32(1, 0x3FFF);

    public async Task<GeneratedDeviceKeys> GenerateDeviceKeysAsync(
        AccountCredentials credentials,
        bool enablePq,
        CancellationToken ct = default)
    {
        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var aciSigned = CreateSignedPreKey(1, credentials.AciIdentityPrivateKey, now);
        await _store.SaveSignedPreKeyAsync(aciSigned.KeyId, aciSigned.SerializedRecord!, ct);

        var pniPriv = credentials.PniIdentityPrivateKey ?? credentials.AciIdentityPrivateKey;
        var pniSigned = CreateSignedPreKey(1, pniPriv, now);
        await _store.SaveSignedPreKeyAsync(1000 + pniSigned.KeyId, pniSigned.SerializedRecord!, ct);

        var oneTime = new List<OneTimePreKeyRecord>();
        for (uint i = 1; i <= 100; i++)
        {
            var rec = CreateOneTimePreKey(i);
            oneTime.Add(rec);
            await _store.SavePreKeyAsync(i, rec.SerializedRecord!, ct);
        }

        KyberPreKeyRecord? aciPq = null;
        KyberPreKeyRecord? pniPq = null;
        var oneTimeKyber = new List<KyberPreKeyRecord>();
        if (enablePq)
        {
            aciPq = CreateKyberPreKey(1, credentials.AciIdentityPrivateKey, now);
            await _store.SaveKyberPreKeyAsync(aciPq.KeyId, aciPq.SerializedRecord!, ct);
            pniPq = CreateKyberPreKey(1, pniPriv, now);
            await _store.SaveKyberPreKeyAsync(2000 + pniPq.KeyId, pniPq.SerializedRecord!, ct);

            for (uint i = 2; i <= 101; i++)
            {
                var k = CreateKyberPreKey(i, credentials.AciIdentityPrivateKey, now);
                oneTimeKyber.Add(k);
                await _store.SaveKyberPreKeyAsync(k.KeyId, k.SerializedRecord!, ct);
            }
        }

        return new GeneratedDeviceKeys
        {
            AciSignedPreKey = aciSigned,
            PniSignedPreKey = pniSigned,
            AciPqLastResortPreKey = aciPq,
            PniPqLastResortPreKey = pniPq,
            OneTimePreKeys = oneTime,
            OneTimeKyberPreKeys = oneTimeKyber,
        };
    }

    public Task ProcessPreKeyBundleAsync(
        string recipientServiceId,
        int deviceId,
        RemotePreKeyBundle bundle,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var remote = LibSignalInterop.NewAddress(recipientServiceId, (uint)deviceId);
            var local = LibSignalInterop.NewAddress(_credentials.Aci, (uint)_credentials.DeviceId);
            try
            {
                var identity = LibSignalInterop.DeserializePublicKey(EnsureDjb(bundle.IdentityKey));
                var signedPub = LibSignalInterop.DeserializePublicKey(EnsureDjb(bundle.SignedPreKeyPublic));
                SignalMutPointer prekey = default;
                SignalMutPointer kyberPub = default;
                try
                {
                    if (bundle.PreKeyPublic is { Length: > 0 })
                        prekey = LibSignalInterop.DeserializePublicKey(EnsureDjb(bundle.PreKeyPublic));

                    if (bundle.KyberPreKeyPublic is { Length: > 0 })
                    {
                        LibSignalInterop.WithBorrowedBuffer(bundle.KyberPreKeyPublic, buf =>
                        {
                            LibSignalInterop.Check(LibSignalNative.KyberPublicKeyDeserialize(out kyberPub, buf));
                        });
                    }

                    SignalMutPointer bundleHandle = default;
                    LibSignalInterop.WithBorrowedBuffer(bundle.SignedPreKeySignature, signedSig =>
                    {
                        LibSignalInterop.WithBorrowedBuffer(bundle.KyberPreKeySignature ?? [], kyberSig =>
                        {
                            LibSignalInterop.Check(LibSignalNative.PreKeyBundleNew(
                                out bundleHandle,
                                (uint)bundle.RegistrationId,
                                (uint)deviceId,
                                bundle.PreKeyId ?? 0,
                                SignalConstPointer.From(prekey),
                                bundle.SignedPreKeyId,
                                SignalConstPointer.From(signedPub),
                                signedSig,
                                SignalConstPointer.From(identity),
                                bundle.KyberPreKeyId ?? 0,
                                SignalConstPointer.From(kyberPub),
                                kyberSig));
                        });
                    });

                    try
                    {
                        using var storeCtx = new LibSignalStoreContext(_store, _credentials);
                        var pinned = new PinnedStores(storeCtx);
                        var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        pinned.WithPointers((identityStore, sessionStore, _, _, _) =>
                        {
                            LibSignalInterop.Check(LibSignalNative.ProcessPrekeyBundle(
                                SignalConstPointer.From(bundleHandle),
                                SignalConstPointer.From(remote),
                                SignalConstPointer.From(local),
                                sessionStore,
                                identityStore,
                                now));
                        });
                    }
                    finally
                    {
                        if (bundleHandle.Raw != IntPtr.Zero)
                            LibSignalNative.PreKeyBundleDestroy(bundleHandle);
                    }
                }
                finally
                {
                    if (prekey.Raw != IntPtr.Zero)
                        LibSignalNative.PublicKeyDestroy(prekey);
                    if (kyberPub.Raw != IntPtr.Zero)
                        LibSignalNative.KyberPublicKeyDestroy(kyberPub);
                    LibSignalNative.PublicKeyDestroy(signedPub);
                    LibSignalNative.PublicKeyDestroy(identity);
                }
            }
            finally
            {
                LibSignalNative.AddressDestroy(remote);
                LibSignalNative.AddressDestroy(local);
            }
        }

        return Task.CompletedTask;
    }

    public Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        CancellationToken ct = default) =>
        EncryptAsync(recipientServiceId, deviceId, plaintext, senderCertificate: null, ct);

    public Task<EncryptedPayload> EncryptAsync(
        string recipientServiceId,
        int deviceId,
        byte[] plaintext,
        byte[]? senderCertificate,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            var remote = LibSignalInterop.NewAddress(recipientServiceId, (uint)deviceId);
            var local = LibSignalInterop.NewAddress(_credentials.Aci, (uint)_credentials.DeviceId);
            try
            {
                using var storeCtx = new LibSignalStoreContext(_store, _credentials);
                var pinned = new PinnedStores(storeCtx);
                var now = (ulong)DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                SignalMutPointer cipherMsg = default;
                int registrationId = 0;

                pinned.WithPointers((identityStore, sessionStore, _, _, _) =>
                {
                    LibSignalInterop.WithBorrowedBuffer(plaintext, ptext =>
                    {
                        LibSignalInterop.Check(LibSignalNative.EncryptMessage(
                            out cipherMsg,
                            ptext,
                            SignalConstPointer.From(remote),
                            SignalConstPointer.From(local),
                            sessionStore,
                            identityStore,
                            now));
                    });
                });

                try
                {
                    LibSignalInterop.Check(LibSignalNative.CiphertextMessageType(out var ctType, SignalConstPointer.From(cipherMsg)));
                    LibSignalInterop.Check(LibSignalNative.CiphertextMessageSerialize(out var ser, SignalConstPointer.From(cipherMsg)));
                    var ciphertext = LibSignalInterop.TakeBuffer(ref ser);
                    var envelopeType = LibSignalInterop.CiphertextTypeToEnvelopeType(ctType);

                    // Best-effort remote registration id from session.
                    var sessionRaw = _store.LoadSessionAsync($"{recipientServiceId}.{deviceId}").GetAwaiter().GetResult();
                    if (sessionRaw is { Length: > 0 })
                    {
                        LibSignalInterop.WithBorrowedBuffer(sessionRaw, buf =>
                        {
                            LibSignalInterop.Check(LibSignalNative.SessionRecordDeserialize(out var rec, buf));
                            try
                            {
                                LibSignalInterop.Check(LibSignalNative.SessionRecordGetRemoteRegistrationId(
                                    out var rid, SignalConstPointer.From(rec)));
                                registrationId = (int)rid;
                            }
                            finally
                            {
                                LibSignalNative.SessionRecordDestroy(rec);
                            }
                        });
                    }

                    if (senderCertificate is { Length: > 0 })
                    {
                        var sealedBytes = Seal(ciphertext, (uint)ctType, senderCertificate, remote, storeCtx);
                        return Task.FromResult(new EncryptedPayload
                        {
                            Type = SignalEnvelopeType.UnidentifiedSender,
                            Ciphertext = sealedBytes,
                            RegistrationId = registrationId,
                        });
                    }

                    return Task.FromResult(new EncryptedPayload
                    {
                        Type = envelopeType,
                        Ciphertext = ciphertext,
                        RegistrationId = registrationId,
                    });
                }
                finally
                {
                    if (cipherMsg.Raw != IntPtr.Zero)
                        LibSignalNative.CiphertextMessageDestroy(cipherMsg);
                }
            }
            finally
            {
                LibSignalNative.AddressDestroy(remote);
                LibSignalNative.AddressDestroy(local);
            }
        }
    }

    public Task<DecryptResult> DecryptAsync(
        string senderServiceId,
        int senderDeviceId,
        int envelopeType,
        byte[] ciphertext,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (envelopeType == SignalEnvelopeType.UnidentifiedSender)
                return Task.FromResult(DecryptSealed(ciphertext));

            var sender = string.IsNullOrEmpty(senderServiceId)
                ? throw new InvalidOperationException("Missing sender for identified envelope")
                : senderServiceId;
            var device = senderDeviceId == 0 ? 1 : senderDeviceId;
            var plaintext = DecryptIdentified(sender, device, envelopeType, ciphertext);
            return Task.FromResult(new DecryptResult
            {
                Plaintext = plaintext,
                SenderServiceId = sender,
                SenderDeviceId = device,
            });
        }
    }

    private byte[] DecryptIdentified(string sender, int deviceId, int envelopeType, byte[] ciphertext)
    {
        var remote = LibSignalInterop.NewAddress(sender, (uint)deviceId);
        var local = LibSignalInterop.NewAddress(_credentials.Aci, (uint)_credentials.DeviceId);
        try
        {
            using var storeCtx = new LibSignalStoreContext(_store, _credentials);
            var pinned = new PinnedStores(storeCtx);
            SignalOwnedBuffer outBuf = default;

            if (envelopeType == SignalEnvelopeType.PreKeyBundle)
            {
                SignalMutPointer msg = default;
                LibSignalInterop.WithBorrowedBuffer(ciphertext, buf =>
                {
                    LibSignalInterop.Check(LibSignalNative.PreKeySignalMessageDeserialize(out msg, buf));
                });
                try
                {
                    pinned.WithPointers((identity, session, preKey, signed, kyber) =>
                    {
                        LibSignalInterop.Check(LibSignalNative.DecryptPreKeyMessage(
                            out outBuf,
                            SignalConstPointer.From(msg),
                            SignalConstPointer.From(remote),
                            SignalConstPointer.From(local),
                            session,
                            identity,
                            preKey,
                            signed,
                            kyber));
                    });
                }
                finally
                {
                    if (msg.Raw != IntPtr.Zero)
                        LibSignalNative.PreKeySignalMessageDestroy(msg);
                }
            }
            else
            {
                SignalMutPointer msg = default;
                LibSignalInterop.WithBorrowedBuffer(ciphertext, buf =>
                {
                    LibSignalInterop.Check(LibSignalNative.MessageDeserialize(out msg, buf));
                });
                try
                {
                    pinned.WithPointers((identity, session, _, _, _) =>
                    {
                        LibSignalInterop.Check(LibSignalNative.DecryptMessage(
                            out outBuf,
                            SignalConstPointer.From(msg),
                            SignalConstPointer.From(remote),
                            SignalConstPointer.From(local),
                            session,
                            identity));
                    });
                }
                finally
                {
                    if (msg.Raw != IntPtr.Zero)
                        LibSignalNative.MessageDestroy(msg);
                }
            }

            return LibSignalInterop.TakeBuffer(ref outBuf);
        }
        finally
        {
            LibSignalNative.AddressDestroy(remote);
            LibSignalNative.AddressDestroy(local);
        }
    }

    private DecryptResult DecryptSealed(byte[] ciphertext)
    {
        using var storeCtx = new LibSignalStoreContext(_store, _credentials);
        var pinned = new PinnedStores(storeCtx);
        SignalMutPointer usmc = default;
        pinned.WithPointers((identity, _, _, _, _) =>
        {
            LibSignalInterop.WithBorrowedBuffer(ciphertext, buf =>
            {
                LibSignalInterop.Check(LibSignalNative.SealedDecryptToUsmc(out usmc, buf, identity));
            });
        });

        try
        {
            LibSignalInterop.Check(LibSignalNative.UsmcGetContents(out var contentsBuf, SignalConstPointer.From(usmc)));
            var inner = LibSignalInterop.TakeBuffer(ref contentsBuf);
            LibSignalInterop.Check(LibSignalNative.UsmcGetMsgType(out var msgType, SignalConstPointer.From(usmc)));
            LibSignalInterop.Check(LibSignalNative.UsmcGetSenderCert(out var cert, SignalConstPointer.From(usmc)));
            try
            {
                LibSignalInterop.Check(LibSignalNative.SenderCertificateGetSenderUuid(out var uuidPtr, SignalConstPointer.From(cert)));
                string sender;
                try
                {
                    sender = Marshal.PtrToStringUTF8(uuidPtr) ?? "";
                }
                finally
                {
                    if (uuidPtr != IntPtr.Zero)
                        LibSignalNative.FreeString(uuidPtr);
                }

                LibSignalInterop.Check(LibSignalNative.SenderCertificateGetDeviceId(out var deviceId, SignalConstPointer.From(cert)));
                var envelopeType = LibSignalInterop.CiphertextTypeToEnvelopeType(msgType);
                var plaintext = DecryptIdentified(sender, (int)deviceId, envelopeType, inner);
                return new DecryptResult
                {
                    Plaintext = plaintext,
                    SenderServiceId = sender,
                    SenderDeviceId = (int)deviceId,
                };
            }
            finally
            {
                if (cert.Raw != IntPtr.Zero)
                    LibSignalNative.SenderCertificateDestroy(cert);
            }
        }
        finally
        {
            if (usmc.Raw != IntPtr.Zero)
                LibSignalNative.UsmcDestroy(usmc);
        }
    }

    private byte[] Seal(
        byte[] ciphertext,
        uint ciphertextType,
        byte[] senderCertificate,
        SignalMutPointer destination,
        LibSignalStoreContext storeCtx)
    {
        SignalMutPointer cert = default;
        LibSignalInterop.WithBorrowedBuffer(senderCertificate, buf =>
        {
            LibSignalInterop.Check(LibSignalNative.SenderCertificateDeserialize(out cert, buf));
        });

        SignalMutPointer usmc = default;
        try
        {
            LibSignalInterop.WithBorrowedBuffer(ciphertext, msgBuf =>
            {
                LibSignalInterop.WithBorrowedBuffer(ReadOnlySpan<byte>.Empty, groupId =>
                {
                    LibSignalInterop.Check(LibSignalNative.UsmcNewFromContentAndType(
                        out usmc,
                        msgBuf,
                        ciphertextType,
                        SignalConstPointer.From(cert),
                        contentHint: 0,
                        groupId));
                });
            });

            var pinned = new PinnedStores(storeCtx);
            SignalOwnedBuffer sealedBuf = default;
            pinned.WithPointers((identity, _, _, _, _) =>
            {
                LibSignalInterop.Check(LibSignalNative.SealedEncrypt(
                    out sealedBuf,
                    SignalConstPointer.From(destination),
                    SignalConstPointer.From(usmc),
                    identity));
            });
            return LibSignalInterop.TakeBuffer(ref sealedBuf);
        }
        finally
        {
            if (usmc.Raw != IntPtr.Zero)
                LibSignalNative.UsmcDestroy(usmc);
            if (cert.Raw != IntPtr.Zero)
                LibSignalNative.SenderCertificateDestroy(cert);
        }
    }

    private SignedPreKeyRecord CreateSignedPreKey(uint keyId, byte[] identityPrivate, ulong timestamp)
    {
        LibSignalInterop.Check(LibSignalNative.PrivateKeyGenerate(out var priv));
        try
        {
            LibSignalInterop.Check(LibSignalNative.PrivateKeyGetPublicKey(out var pub, SignalConstPointer.From(priv)));
            try
            {
                var pubBytes = LibSignalInterop.SerializePublicKey(SignalConstPointer.From(pub));
                var identity = LibSignalInterop.DeserializePrivateKey(identityPrivate);
                try
                {
                    var signature = LibSignalInterop.Sign(SignalConstPointer.From(identity), pubBytes);
                    SignalMutPointer record = default;
                    LibSignalInterop.WithBorrowedBuffer(signature, sigBuf =>
                    {
                        LibSignalInterop.Check(LibSignalNative.SignedPreKeyRecordNew(
                            out record,
                            keyId,
                            timestamp,
                            SignalConstPointer.From(pub),
                            SignalConstPointer.From(priv),
                            sigBuf));
                    });
                    try
                    {
                        LibSignalInterop.Check(LibSignalNative.SignedPreKeyRecordSerialize(
                            out var ser, SignalConstPointer.From(record)));
                        var serialized = LibSignalInterop.TakeBuffer(ref ser);
                        return new SignedPreKeyRecord
                        {
                            KeyId = keyId,
                            PublicKey = pubBytes,
                            PrivateKey = LibSignalInterop.SerializePrivateKey(SignalConstPointer.From(priv)),
                            Signature = signature,
                            SerializedRecord = serialized,
                        };
                    }
                    finally
                    {
                        LibSignalNative.SignedPreKeyRecordDestroy(record);
                    }
                }
                finally
                {
                    LibSignalNative.PrivateKeyDestroy(identity);
                }
            }
            finally
            {
                LibSignalNative.PublicKeyDestroy(pub);
            }
        }
        finally
        {
            LibSignalNative.PrivateKeyDestroy(priv);
        }
    }

    private static OneTimePreKeyRecord CreateOneTimePreKey(uint keyId)
    {
        LibSignalInterop.Check(LibSignalNative.PrivateKeyGenerate(out var priv));
        try
        {
            LibSignalInterop.Check(LibSignalNative.PrivateKeyGetPublicKey(out var pub, SignalConstPointer.From(priv)));
            try
            {
                LibSignalInterop.Check(LibSignalNative.PreKeyRecordNew(
                    out var record, keyId, SignalConstPointer.From(pub), SignalConstPointer.From(priv)));
                try
                {
                    LibSignalInterop.Check(LibSignalNative.PreKeyRecordSerialize(out var ser, SignalConstPointer.From(record)));
                    var serialized = LibSignalInterop.TakeBuffer(ref ser);
                    return new OneTimePreKeyRecord
                    {
                        KeyId = keyId,
                        PublicKey = LibSignalInterop.SerializePublicKey(SignalConstPointer.From(pub)),
                        PrivateKey = LibSignalInterop.SerializePrivateKey(SignalConstPointer.From(priv)),
                        SerializedRecord = serialized,
                    };
                }
                finally
                {
                    LibSignalNative.PreKeyRecordDestroy(record);
                }
            }
            finally
            {
                LibSignalNative.PublicKeyDestroy(pub);
            }
        }
        finally
        {
            LibSignalNative.PrivateKeyDestroy(priv);
        }
    }

    private KyberPreKeyRecord CreateKyberPreKey(uint keyId, byte[] identityPrivate, ulong timestamp)
    {
        LibSignalInterop.Check(LibSignalNative.KyberKeyPairGenerate(out var pair));
        try
        {
            LibSignalInterop.Check(LibSignalNative.KyberKeyPairGetPublicKey(out var pub, SignalConstPointer.From(pair)));
            try
            {
                LibSignalInterop.Check(LibSignalNative.KyberPublicKeySerialize(out var pubBuf, SignalConstPointer.From(pub)));
                var pubBytes = LibSignalInterop.TakeBuffer(ref pubBuf);
                var identity = LibSignalInterop.DeserializePrivateKey(identityPrivate);
                try
                {
                    var signature = LibSignalInterop.Sign(SignalConstPointer.From(identity), pubBytes);
                    SignalMutPointer record = default;
                    LibSignalInterop.WithBorrowedBuffer(signature, sigBuf =>
                    {
                        LibSignalInterop.Check(LibSignalNative.KyberPreKeyRecordNew(
                            out record, keyId, timestamp, SignalConstPointer.From(pair), sigBuf));
                    });
                    try
                    {
                        LibSignalInterop.Check(LibSignalNative.KyberPreKeyRecordSerialize(
                            out var ser, SignalConstPointer.From(record)));
                        var serialized = LibSignalInterop.TakeBuffer(ref ser);
                        return new KyberPreKeyRecord
                        {
                            KeyId = keyId,
                            PublicKey = pubBytes,
                            Signature = signature,
                            SerializedRecord = serialized,
                        };
                    }
                    finally
                    {
                        LibSignalNative.KyberPreKeyRecordDestroy(record);
                    }
                }
                finally
                {
                    LibSignalNative.PrivateKeyDestroy(identity);
                }
            }
            finally
            {
                LibSignalNative.KyberPublicKeyDestroy(pub);
            }
        }
        finally
        {
            LibSignalNative.KyberKeyPairDestroy(pair);
        }
    }

    private static byte[] EnsureDjb(byte[] key)
    {
        if (key.Length == 33)
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
}
