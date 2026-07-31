# SignalCpf

Cross-platform CPF (.NET) Signal desktop client. Protocol logic runs **in-process in C#**
and talks to a **configurable** (default: self-hosted) Signal server.

## Layout

```
Signal-Desktop.NET/
├── SignalCpf.Core/       # Models + ISignalSidecarClient + SignalServerOptions
├── SignalCpf.Protocol/   # ProvisioningCipher / Curve25519
├── SignalCpf.Net/        # HTTPS + provisioning/message WebSockets
├── SignalCpf.Storage/    # Credential store + SQLite
├── SignalCpf.LibSignal/  # libsignal FFI probe + managed session crypto
├── SignalCpf.Client/     # SignalClientOrchestrator
├── SignalCpf.Protobuf/   # Wire protos
├── SignalCpf.UI/         # CPF desktop shell
└── docs/                 # Server config + libsignal FFI notes
```

## Quick start

**Self-hosted**

```powershell
$env:SIGNAL_SERVER_URL = "https://localhost"
$env:SIGNAL_SERVER_INSECURE_TLS = "1"
dotnet run --project SignalCpf.UI
```

**Official Signal servers** (needs `signal_ffi`)

```powershell
.\scripts\build-libsignal-ffi.ps1
$env:SIGNAL_SERVER_PROFILE = "official"
dotnet run --project SignalCpf.UI
```

Settings should show `配置档: Official` and `libsignal FFI: True`.

See [docs/server-config.md](docs/server-config.md) and [docs/libsignal-ffi-research.md](docs/libsignal-ffi-research.md).

## Compliance

- Signal-Desktop is AGPL-3.0; extracted/adapted code remains AGPL if distributed.
- libsignal use outside official Signal clients is unsupported by Signal.
- Production (`SIGNAL_SERVER_PROFILE=official`) may reject or limit unofficial clients; you are responsible for ToS compliance. Prefer self-hosted for development.
