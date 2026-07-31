# libsignal FFI research notes

## Goal

Session crypto (Double Ratchet, PreKey / PQXDH, Sealed Sender) comes from official
`libsignal` via the C ABI (`signal_ffi`), matching the Swift bridge used by Signal-iOS.
Managed C# crypto is a local fallback only and is **not** interoperable with official clients.

## Current state in SignalCpf

| Component | Status |
|-----------|--------|
| `SignalCpf.LibSignal.Native.LibSignalNative` | P/Invoke surface for protocol / store / sealed-sender APIs |
| `FfiSignalProtocolService` | Native `ISignalProtocolService` when `signal_ffi` loads |
| `ManagedSignalProtocol` | Fallback X3DH-style codec when FFI absent (no official interop) |
| `SignalProtocolFactory` | Prefers `FfiSignalProtocolService` when native library is available |
| `LibSignalStoreCallbacks` | Adapts `IMessageStore` to libsignal store vtables |

## Building / placing the native library

### Prerequisites (Windows)

```powershell
winget install Rustlang.Rustup
winget install Google.Protobuf      # protoc
winget install Kitware.CMake
winget install LLVM.LLVM           # libclang for bindgen
# Also: Visual Studio C++ workload, NASM (script looks in .deps/tools/nasm), Git (perl)
```

Reopen the terminal after installing Rust so `cargo` is on `PATH`.

### Build

```powershell
.\scripts\build-libsignal-ffi.ps1
```

This clones `signalapp/libsignal` (unless already present), patches `libsignal-ffi` to
emit a **cdylib** (`signal_ffi.dll` — upstream is staticlib-only for Swift), builds release,
and copies the DLL plus `signal_ffi.h` into [`native/`](../native/).

`SignalCpf.UI` copies any present native library next to the app output on build.

Restart the app after placing the DLL — `ClientSettings.UsesNativeLibSignal` reflects
whether `FfiSignalProtocolService` is active.

## Binding approach

- Header source of truth: `native/signal_ffi.h` (from `swift/Sources/SignalFfi` or cbindgen)
- Marshaling stores (Identity / PreKey / Session / SignedPreKey / Kyber) adapt `IMessageStore`
- Thin P/Invoke in `LibSignalNative`; orchestration stays in `SignalClientOrchestrator`
- Envelope types used on the wire: `1` ciphertext, `3` prekey, `6` unidentified (sealed)

### Store mapping

| libsignal store | SQLite |
|-----------------|--------|
| SessionStore | `sessions.record` (binary SessionRecord) |
| IdentityKeyStore | `identities` + local keys from `AccountCredentials` |
| PreKeyStore | `prekeys.record` (serialized PreKeyRecord) |
| SignedPreKeyStore | `signed_prekeys.record` |
| KyberPreKeyStore | `kyber_prekeys.record` |

Legacy Managed JSON sessions are cleared when the native backend starts (incompatible).

## Manual interop checklist (official Android)

Against the **same authorized / self-hosted** Signal server:

1. Build and place `signal_ffi` so settings show `libsignal FFI: True`
2. Link this desktop client by scanning the QR with official Signal-Android
3. Send a text from phone → desktop (expect decrypt of type 1/3/6)
4. Send a text from desktop → phone (multi-device fan-out + optional sealed sender)
5. Confirm Sealed Sender inbound (`Envelope.Type = UNIDENTIFIED_SENDER`) decrypts

## Compliance

Use outside official Signal clients is unsupported by Signal. Point only at
self-hosted / authorized servers. Do not hard-code production hosts.
