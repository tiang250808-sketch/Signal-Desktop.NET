# Signal server configuration

SignalCpf talks to a **configurable** Signal-protocol server. Choose a profile:

| Profile | Env | Endpoints |
|---------|-----|-----------|
| **SelfHosted** (default) | unset / `selfhosted` | `SIGNAL_SERVER_URL` (default `https://localhost`) |
| **Official** | `SIGNAL_SERVER_PROFILE=official` | `chat.signal.org` + cdn/storage |
| **Staging** | `SIGNAL_SERVER_PROFILE=staging` | `chat.staging.signal.org` + staging CDN |

## Environment variables

| Variable | Default | Meaning |
|----------|---------|---------|
| `SIGNAL_SERVER_PROFILE` | `selfhosted` | `official` / `staging` / `selfhosted` |
| `SIGNAL_SERVER_URL` | profile default | HTTPS API base (overrides profile) |
| `SIGNAL_CDN_URL` | profile default | Attachment CDN-0 |
| `SIGNAL_CDN2_URL` | profile default | CDN-2 |
| `SIGNAL_CDN3_URL` | profile default | CDN-3 |
| `SIGNAL_STORAGE_URL` | profile default | Storage service |
| `SIGNAL_USER_AGENT` | profile default | HTTP/WS User-Agent |
| `SIGNAL_DEVICE_NAME` | `CPF Desktop` | Default linked device name |
| `SIGNAL_DATA_DIR` | `%LocalAppData%/SignalCpf` | Credentials + SQLite |
| `SIGNAL_SERVER_INSECURE_TLS` | unset | `1`/`true` for self-signed (**ignored** on official/staging) |
| `SIGNAL_ENABLE_PQ_KEYS` | `true` | Include PQ last-resort prekey fields |
| `SIGNAL_ATTACH_FILE` | unset | Local path staged via UI「附件」 |

## Official production (PowerShell)

```powershell
# Requires native\signal_ffi.dll (.\scripts\build-libsignal-ffi.ps1)
$env:SIGNAL_SERVER_PROFILE = "official"
dotnet run --project SignalCpf.UI
```

Official profile sets:

- API: `https://chat.signal.org`
- CDN: `https://cdn.signal.org` (+ cdn2/cdn3)
- Storage: `https://storage.signal.org`
- User-Agent: `Signal-Desktop/8.20.0` (override with `SIGNAL_USER_AGENT`)
- TLS: trusts Signal Messenger root CA (same PEM as Desktop `certificateAuthority`); OS trust alone fails with PartialChain

Official servers return **HTTP 499** when the User-Agent is below the configured minimum (`RemoteDeprecationFilter`). Bump `SIGNAL_USER_AGENT` if linking fails after a Desktop release.

Then scan the QR with the official Signal Android/iOS app (Linked Devices).

## Self-hosted example

```powershell
$env:SIGNAL_SERVER_PROFILE = "selfhosted"
$env:SIGNAL_SERVER_URL = "https://signal.example.local"
$env:SIGNAL_SERVER_INSECURE_TLS = "1"
dotnet run --project SignalCpf.UI
```

## Provisioning flow

1. App opens `wss(s)://{api}/v1/websocket/provisioning/`
2. Server assigns provisioning address → QR `sgnl://linkdevice?...`
3. Primary phone encrypts `ProvisionEnvelope` to the desktop ephemeral key
4. Desktop decrypts with `ProvisioningCipher`, then `PUT /v1/devices/link`
5. Credentials persisted (DPAPI on Windows) and message WebSocket starts

## Compliance

- Connecting unofficial builds to Signal production is **unsupported by Signal** and may be limited or rejected by the service.
- libsignal outside official clients is unsupported.
- Prefer self-hosted / authorized servers for development.
- Keep AGPL obligations if you distribute modified code.
