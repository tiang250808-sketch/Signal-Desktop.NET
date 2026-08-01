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
| `SIGNAL_CHALLENGE_URL` | profile default | Captcha page for phone registration |
| `SIGNAL_USER_AGENT` | profile default | HTTP/WS User-Agent |
| `SIGNAL_DEVICE_NAME` | `CPF Desktop` | Default linked / primary device name |
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
- Captcha: `https://signalcaptchas.org/registration/generate.html`
- User-Agent: `Signal-Desktop/8.20.0` (override with `SIGNAL_USER_AGENT`)
- TLS: trusts Signal Messenger root CA (same PEM as Desktop `certificateAuthority`); OS trust alone fails with PartialChain

Official servers return **HTTP 499** when the User-Agent is below the configured minimum (`RemoteDeprecationFilter`). Bump `SIGNAL_USER_AGENT` if linking fails after a Desktop release.

**Link device:** scan the QR with the official Signal Android/iOS app (Linked Devices).

**Register account:** use the install screen「注册账户」tab (primary device / deviceId=1). Official registration usually requires a captcha token and `signal_ffi`.

## Self-hosted example

```powershell
$env:SIGNAL_SERVER_PROFILE = "selfhosted"
$env:SIGNAL_SERVER_URL = "https://signal.example.local"
$env:SIGNAL_SERVER_INSECURE_TLS = "1"
dotnet run --project SignalCpf.UI
```

## Provisioning flow (link existing account)

1. App opens `wss(s)://{api}/v1/websocket/provisioning/`
2. Server assigns provisioning address → QR `sgnl://linkdevice?...`
3. Primary phone encrypts `ProvisionEnvelope` to the desktop ephemeral key
4. Desktop decrypts with `ProvisioningCipher`, then `PUT /v1/devices/link`
5. Credentials persisted (DPAPI on Windows) and message WebSocket starts

## Primary registration flow (new account)

Requires a Signal-Server that exposes the modern verification API (`/v1/verification/session`, `/v1/registration`).

1. UI「注册账户」→ enter E.164 number (e.g. `+8613812345678`)
2. `POST /v1/verification/session` creates a session
3. If `requestedInformation` includes `captcha`: open `SIGNAL_CHALLENGE_URL`, complete the challenge, paste the `signalcaptcha://…` token (prefix optional) → `PATCH /v1/verification/session/{id}`
4. `POST …/code` sends SMS (or voice) verification code
5. User enters code → `PUT …/code`, then `POST /v1/registration` with ACI/PNI identity keys + signed/PQ prekeys (`skipDeviceTransfer=true`, `fetchesMessages=true`)
6. Credentials saved as **deviceId=1**; one-time prekeys uploaded; message WebSocket starts

Not supported yet: FCM/APNs push challenge, Registration Lock PIN (HTTP 423).

## Compliance

- Connecting unofficial builds to Signal production is **unsupported by Signal** and may be limited or rejected by the service.
- libsignal outside official clients is unsupported.
- Prefer self-hosted / authorized servers for development.
- Keep AGPL obligations if you distribute modified code.
