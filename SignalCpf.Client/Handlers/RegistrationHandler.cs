using System.Security.Cryptography;
using SignalCpf.Core.Models;
using SignalCpf.Core.Options;
using SignalCpf.LibSignal;
using SignalCpf.Net.Http;
using SignalCpf.Protocol.Crypto;
using SignalCpf.Protocol.Provisioning;
using SignalCpf.Storage;

namespace SignalCpf.Client.Handlers;

/// <summary>
/// Primary-phone registration state machine (verification session + account create).
/// </summary>
internal sealed class RegistrationHandler
{
    private readonly SignalServerOptions _options;
    private readonly ICredentialStore _credentials;
    private readonly IMessageStore _messages;
    private readonly SignalRestClient _rest;
    private readonly ClientState _state;
    private readonly PreKeyManager _preKeys;
    private readonly Func<CancellationToken, Task> _startMessageSocket;

    private string? _registrationSessionId;
    private string? _registrationNumber;
    private string _registrationTransport = "sms";

    public RegistrationHandler(
        SignalServerOptions options,
        ICredentialStore credentials,
        IMessageStore messages,
        SignalRestClient rest,
        ClientState state,
        PreKeyManager preKeys,
        Func<CancellationToken, Task> startMessageSocket)
    {
        _options = options;
        _credentials = credentials;
        _messages = messages;
        _rest = rest;
        _state = state;
        _preKeys = preKeys;
        _startMessageSocket = startMessageSocket;
    }

    public async Task<RegistrationSessionStatus> StartAsync(
        string e164Number,
        string? captchaToken = null,
        string transport = "sms",
        CancellationToken cancellationToken = default)
    {
        if (_options.IsOfficialLike && !LibSignal.Native.LibSignalNative.IsAvailable)
        {
            throw new InvalidOperationException(
                "官方/Staging 服务器注册需要 libsignal FFI。请先运行 scripts/build-libsignal-ffi.ps1。");
        }

        var number = NormalizeE164(e164Number);
        _registrationNumber = number;
        _registrationTransport = NormalizeTransport(transport);
        _registrationSessionId = null;

        try
        {
            var session = await _rest.CreateVerificationSessionAsync(
                new CreateVerificationSessionRequest
                {
                    Number = number,
                    Captcha = NormalizeCaptcha(captchaToken),
                },
                cancellationToken);

            return await AdvanceSessionAsync(session, requestCode: true, cancellationToken);
        }
        catch (SignalApiException ex)
        {
            var status = MapApiError(ex);
            await EmitAsync(status, cancellationToken);
            throw new InvalidOperationException(status.Message, ex);
        }
    }

    public async Task<RegistrationSessionStatus> SubmitCaptchaAsync(
        string captchaToken,
        CancellationToken cancellationToken = default)
    {
        var sessionId = _registrationSessionId
            ?? throw new InvalidOperationException("尚未创建验证会话，请先输入手机号开始注册");
        var token = NormalizeCaptcha(captchaToken)
            ?? throw new ArgumentException("Captcha token 不能为空", nameof(captchaToken));

        try
        {
            var session = await _rest.UpdateVerificationSessionAsync(
                sessionId,
                new UpdateVerificationSessionRequest { Captcha = token },
                cancellationToken);
            return await AdvanceSessionAsync(session, requestCode: true, cancellationToken);
        }
        catch (SignalApiException ex)
        {
            var status = MapApiError(ex);
            await EmitAsync(status, cancellationToken);
            throw new InvalidOperationException(status.Message, ex);
        }
    }

    public async Task<RegistrationSessionStatus> RequestCodeAsync(
        string transport = "sms",
        CancellationToken cancellationToken = default)
    {
        var sessionId = _registrationSessionId
            ?? throw new InvalidOperationException("尚未创建验证会话，请先输入手机号开始注册");
        _registrationTransport = NormalizeTransport(transport);

        try
        {
            var session = await _rest.RequestVerificationCodeAsync(
                sessionId,
                new VerificationCodeRequest
                {
                    Transport = _registrationTransport,
                    Client = "ios",
                },
                cancellationToken);
            // Code was already sent above; don't request again — just emit CodeRequested status.
            _registrationSessionId = session.Id;
            var status = ToStatus(
                session,
                RegistrationProgressKind.CodeRequested,
                _registrationTransport == "voice"
                    ? $"语音验证码已拨打至 {_registrationNumber}，请接听"
                    : $"验证码已请求发送至 {_registrationNumber}。若未收到可改用 Call，或稍后再点 Send SMS");
            await EmitAsync(status, cancellationToken);
            return status;
        }
        catch (SignalApiException ex)
        {
            var status = MapApiError(ex);
            await EmitAsync(status, cancellationToken);
            throw new InvalidOperationException(status.Message, ex);
        }
    }

    public async Task<AccountStatus> CompleteAsync(
        string verificationCode,
        string deviceName,
        CancellationToken cancellationToken = default)
    {
        var sessionId = _registrationSessionId
            ?? throw new InvalidOperationException("尚未创建验证会话，请先输入手机号开始注册");
        var number = _registrationNumber
            ?? throw new InvalidOperationException("缺少注册手机号");

        if (_options.IsOfficialLike && !LibSignal.Native.LibSignalNative.IsAvailable)
        {
            throw new InvalidOperationException(
                "官方/Staging 服务器注册需要 libsignal FFI。请先运行 scripts/build-libsignal-ffi.ps1。");
        }

        var code = NormalizeVerificationCode(verificationCode);
        try
        {
            var verified = await _rest.SubmitVerificationCodeAsync(
                sessionId,
                new SubmitVerificationCodeRequest { Code = code },
                cancellationToken);

            if (!verified.Verified)
            {
                var waiting = ToStatus(
                    verified,
                    RegistrationProgressKind.CodeRequested,
                    "验证码不正确或尚未生效，请重试");
                await EmitAsync(waiting, cancellationToken);
                throw new InvalidOperationException(waiting.Message);
            }

            await EmitAsync(
                ToStatus(verified, RegistrationProgressKind.Verified, "手机号已验证，正在创建账户…"),
                cancellationToken);

            var status = await FinishPrimaryRegistrationAsync(sessionId, number, deviceName, cancellationToken);
            ClearState();
            return status;
        }
        catch (SignalApiException ex)
        {
            var mapped = MapApiError(ex);
            await EmitAsync(mapped, cancellationToken);
            throw new InvalidOperationException(mapped.Message, ex);
        }
    }

    public void Cancel() => ClearState();

    private async Task<RegistrationSessionStatus> AdvanceSessionAsync(
        VerificationSessionResponse session,
        bool requestCode,
        CancellationToken ct)
    {
        _registrationSessionId = session.Id;

        if (session.Verified)
        {
            var verified = ToStatus(
                session,
                RegistrationProgressKind.Verified,
                "手机号已验证，请提交以完成注册");
            await EmitAsync(verified, ct);
            return verified;
        }

        var requested = session.RequestedInformation ?? [];
        var needsCaptcha = requested.Any(i =>
            string.Equals(i, "captcha", StringComparison.OrdinalIgnoreCase));
        var needsPush = requested.Any(i =>
            string.Equals(i, "pushChallenge", StringComparison.OrdinalIgnoreCase));

        if (needsPush && !needsCaptcha && !session.AllowedToRequestCode)
        {
            var pushBlocked = ToStatus(
                session,
                RegistrationProgressKind.Failed,
                "服务器要求 push challenge，桌面端无法完成。请改用自建服务器，或提供 captcha。");
            await EmitAsync(pushBlocked, ct);
            return pushBlocked;
        }

        if (needsCaptcha && !session.AllowedToRequestCode)
        {
            var captchaNeeded = ToStatus(
                session,
                RegistrationProgressKind.CaptchaRequired,
                "需要完成 Captcha 后才能获取验证码");
            await EmitAsync(captchaNeeded, ct);
            return captchaNeeded;
        }

        var codeSent = false;
        if (requestCode && session.AllowedToRequestCode)
        {
            session = await _rest.RequestVerificationCodeAsync(
                session.Id,
                new VerificationCodeRequest
                {
                    Transport = _registrationTransport,
                    // Desktop maps to UNKNOWN; ios selects SMS templates used by primary clients.
                    Client = "ios",
                },
                ct);
            _registrationSessionId = session.Id;
            codeSent = true;

            requested = session.RequestedInformation ?? [];
            needsCaptcha = requested.Any(i =>
                string.Equals(i, "captcha", StringComparison.OrdinalIgnoreCase));
            if (needsCaptcha && !session.AllowedToRequestCode && !session.Verified)
            {
                var captchaNeeded = ToStatus(
                    session,
                    RegistrationProgressKind.CaptchaRequired,
                    "需要完成 Captcha 后才能获取验证码");
                await EmitAsync(captchaNeeded, ct);
                return captchaNeeded;
            }
        }

        RegistrationProgressKind kind;
        string message;
        if (codeSent)
        {
            kind = RegistrationProgressKind.CodeRequested;
            message = _registrationTransport == "voice"
                ? $"语音验证码已拨打至 {_registrationNumber}，请接听"
                : $"验证码已请求发送至 {_registrationNumber}。若未收到，请完成 Captcha 后重试，或改用 Call 语音验证";
        }
        else if (!session.AllowedToRequestCode && needsCaptcha)
        {
            kind = RegistrationProgressKind.CaptchaRequired;
            message = "需要先完成 Captcha：打开下方链接 → 完成后复制 signalcaptcha:// 令牌 → 粘贴并提交，才会发送短信";
        }
        else
        {
            kind = RegistrationProgressKind.SessionCreated;
            message = "验证会话已创建";
        }

        var status = ToStatus(session, kind, message);
        await EmitAsync(status, ct);
        return status;
    }

    private async Task<AccountStatus> FinishPrimaryRegistrationAsync(
        string sessionId,
        string number,
        string deviceName,
        CancellationToken ct)
    {
        var password = GeneratePassword();
        var registrationId = RandomNumberGenerator.GetInt32(1, 0x3FFF);
        var pniRegistrationId = RandomNumberGenerator.GetInt32(1, 0x3FFF);
        var normalizedName = LinkDeviceUrl.NormalizeDeviceName(
            string.IsNullOrWhiteSpace(deviceName) ? _options.DeviceName : deviceName);
        if (normalizedName.Length > 50)
            normalizedName = normalizedName[..50];

        var aciKeyPair = Curve25519.GenerateKeyPair();
        var pniKeyPair = Curve25519.GenerateKeyPair();
        var profileKey = ProvisioningCrypto.GetRandomBytes(32);
        var uak = DeriveUnidentifiedAccessKey(profileKey);

        var pending = new AccountCredentials
        {
            Aci = "",
            Pni = "",
            Number = number,
            DeviceId = 1,
            DeviceName = normalizedName,
            Password = password,
            RegistrationId = registrationId,
            PniRegistrationId = pniRegistrationId,
            AciIdentityPrivateKey = aciKeyPair.PrivateKey,
            AciIdentityPublicKey = aciKeyPair.SerializePublicKey(),
            PniIdentityPrivateKey = pniKeyPair.PrivateKey,
            PniIdentityPublicKey = pniKeyPair.SerializePublicKey(),
            ProfileKey = profileKey,
            ReadReceipts = true,
        };

        var protocol = SignalProtocolFactory.Create(_messages, pending);
        var keys = await protocol.GenerateDeviceKeysAsync(pending, enablePq: true, ct);
        if (keys.AciPqLastResortPreKey is null || keys.PniPqLastResortPreKey is null)
        {
            throw new InvalidOperationException(
                "注册需要 PQ（Kyber）last-resort 预密钥。官方服务器请启用 libsignal FFI。");
        }

        _rest.SetNumberAuth(number, password);
        AccountCreationResponse created;
        try
        {
            created = await _rest.RegisterAccountAsync(new RegistrationRequest
            {
                SessionId = sessionId,
                SkipDeviceTransfer = true,
                AciIdentityKey = Convert.ToBase64String(aciKeyPair.SerializePublicKey()),
                PniIdentityKey = Convert.ToBase64String(pniKeyPair.SerializePublicKey()),
                AccountAttributes = new RegistrationAccountAttributes
                {
                    FetchesMessages = true,
                    RegistrationId = registrationId,
                    PniRegistrationId = pniRegistrationId,
                    Name = null,
                    Capabilities = new Dictionary<string, bool>
                    {
                        ["spqr"] = true,
                        ["storage"] = true,
                    },
                    UnidentifiedAccessKey = Convert.ToBase64String(uak),
                    UnrestrictedUnidentifiedAccess = false,
                    DiscoverableByPhoneNumber = true,
                },
                AciSignedPreKey = PreKeyManager.ToSignedEntity(keys.AciSignedPreKey),
                PniSignedPreKey = PreKeyManager.ToSignedEntity(keys.PniSignedPreKey),
                AciPqLastResortPreKey = PreKeyManager.ToKyberEntity(keys.AciPqLastResortPreKey),
                PniPqLastResortPreKey = PreKeyManager.ToKyberEntity(keys.PniPqLastResortPreKey),
            }, ct);
        }
        catch (SignalApiException ex) when (ex.StatusCode == 423)
        {
            throw new InvalidOperationException(
                "该号码已启用 Registration Lock（PIN）。本客户端暂不支持 PIN 解锁注册。", ex);
        }
        catch (SignalApiException ex) when (ex.StatusCode == 409)
        {
            throw new InvalidOperationException(
                "该账户可从其他设备迁移数据。本客户端以 skipDeviceTransfer 重新注册失败，请确认服务器响应。",
                ex);
        }

        var aci = created.Uuid
            ?? throw new InvalidOperationException("注册响应缺少 uuid");
        var pni = created.Pni ?? "";
        pending.Aci = SignalAuth.NormalizeAci(aci);
        pending.Pni = string.IsNullOrWhiteSpace(pni) ? "" : SignalAuth.NormalizeAci(pni);
        pending.Number = created.Number ?? number;
        pending.DeviceId = 1;
        pending.LinkedAt = DateTimeOffset.UtcNow;

        await _credentials.SaveAsync(pending, ct);
        var liveProtocol = SignalProtocolFactory.Create(_messages, pending);
        _state.SetAccountAndProtocol(pending, liveProtocol);

        _rest.SetDeviceAuth(pending.Aci, pending.DeviceId, pending.Password);

        await _preKeys.TryRegisterAsync(
            "v2/keys?identity=aci",
            keys.AciSignedPreKey,
            keys.OneTimePreKeys,
            keys.AciPqLastResortPreKey,
            keys.OneTimeKyberPreKeys,
            ct);
        await _preKeys.TryRegisterAsync(
            "v2/keys?identity=pni",
            keys.PniSignedPreKey,
            keys.OneTimePreKeys,
            keys.PniPqLastResortPreKey,
            [],
            ct);

        await _preKeys.RefreshSenderCertificateAsync(ct);

        var accountStatus = _credentials.ToAccountStatus(pending);
        await EmitAsync(
            new RegistrationSessionStatus(
                RegistrationProgressKind.Registered,
                $"已注册为主设备：{pending.Number ?? pending.Aci}",
                SessionId: sessionId,
                Number: pending.Number,
                CaptchaRequired: false,
                AllowedToRequestCode: false,
                Verified: true,
                ChallengeUrl: _options.ChallengeUrl),
            ct);
        await _state.EmitAsync(new SidecarEvent.AccountStatusChanged(accountStatus), ct);
        _ = _startMessageSocket(ct);
        return accountStatus;
    }

    private RegistrationSessionStatus ToStatus(
        VerificationSessionResponse session,
        RegistrationProgressKind kind,
        string message)
    {
        var requested = session.RequestedInformation ?? [];
        var captchaRequired = requested.Any(i =>
            string.Equals(i, "captcha", StringComparison.OrdinalIgnoreCase));
        return new RegistrationSessionStatus(
            kind,
            message,
            SessionId: session.Id,
            Number: _registrationNumber,
            CaptchaRequired: captchaRequired || kind == RegistrationProgressKind.CaptchaRequired,
            AllowedToRequestCode: session.AllowedToRequestCode,
            Verified: session.Verified,
            ChallengeUrl: _options.ChallengeUrl);
    }

    private RegistrationSessionStatus MapApiError(SignalApiException ex)
    {
        var message = ex.StatusCode switch
        {
            400 => DescribeBadRequest(ex),
            409 => "尚不能发送验证码：请先完成并提交 Captcha。",
            418 => "短信通道无法投递，请点击 Call 改用语音验证。",
            423 => "该号码已启用 Registration Lock（PIN），本客户端暂不支持。",
            429 => "请求过于频繁，请稍后重试。",
            401 => "验证会话未通过，请重新开始注册。",
            403 => "Captcha 被服务器拒绝。请重新完成验证（令牌只能用一次且很快过期；浏览器与客户端须同一公网 IP，关闭 VPN）。",
            404 => "验证会话不存在或已过期，请重新开始注册。",
            422 => "请求参数无效，请检查手机号格式（选择正确国家码，仅输入国内号码数字）。",
            440 => "运营商拒绝投递验证码，请改用 Call 语音或稍后再试。",
            498 => "服务器要求通过 WebSocket 注册。请重试；若仍失败，检查网络是否可访问 wss 端点。",
            499 => "服务器要求更新的客户端能力（含 PQ）。请确认已构建 libsignal FFI。",
            _ => $"注册失败（HTTP {ex.StatusCode}）：{ex.Message}",
        };

        // Keep captcha step available after a rejected/expired token so the user can retry.
        var keepCaptcha = ex.StatusCode is 403 or 400 or 409
                          && !string.IsNullOrEmpty(_registrationSessionId);

        return new RegistrationSessionStatus(
            RegistrationProgressKind.Failed,
            message,
            SessionId: _registrationSessionId,
            Number: _registrationNumber,
            CaptchaRequired: keepCaptcha,
            AllowedToRequestCode: false,
            Verified: false,
            ChallengeUrl: _options.ChallengeUrl);
    }

    private static string DescribeBadRequest(SignalApiException ex)
    {
        var body = ex.ResponseBody ?? string.Empty;
        // NonNormalizedPhoneNumberExceptionMapper returns original/normalized hints.
        if (body.Contains("normalizedNumber", StringComparison.OrdinalIgnoreCase)
            || body.Contains("originalNumber", StringComparison.OrdinalIgnoreCase))
        {
            return "手机号格式未规范化。请确认国家码正确，国内号码不要加 0 或重复国家码。";
        }

        // ImpossiblePhoneNumberExceptionMapper returns HTTP 400 with an empty body.
        if (string.IsNullOrWhiteSpace(body) || body.Contains("Bad Request", StringComparison.OrdinalIgnoreCase))
        {
            return "手机号无效。请选择正确国家码，并只输入国内号码数字（例如中国：11 位，勿加 0）。";
        }

        return $"注册失败（HTTP 400）：{ex.Message}";
    }

    private void ClearState()
    {
        _registrationSessionId = null;
        _registrationNumber = null;
        _registrationTransport = "sms";
    }

    private ValueTask EmitAsync(RegistrationSessionStatus status, CancellationToken ct) =>
        _state.EmitAsync(new SidecarEvent.RegistrationUpdated(status), ct);

    private static string NormalizeE164(string raw)
    {
        var n = raw.Trim().Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
        if (!n.StartsWith('+'))
            throw new ArgumentException("手机号须为 E.164 格式（以 + 开头，如 +8613812345678）", nameof(raw));
        if (n.Length < 8 || n.Length > 16 || !n[1..].All(char.IsDigit))
            throw new ArgumentException("手机号格式无效：请选择正确国家码，并只输入国内号码数字", nameof(raw));
        return n;
    }

    private static string NormalizeTransport(string? transport) =>
        string.Equals(transport, "voice", StringComparison.OrdinalIgnoreCase) ? "voice" : "sms";

    private static string? NormalizeCaptcha(string? captcha)
    {
        if (string.IsNullOrWhiteSpace(captcha))
            return null;
        var t = captcha.Trim().Trim('"', '\'');

        // Browsers sometimes rewrite the custom scheme as http(s)://signalcaptcha//…
        foreach (var bad in new[]
                 {
                     "https://signalcaptcha//", "http://signalcaptcha//",
                     "https://signalcaptcha/", "http://signalcaptcha/",
                     "signalcaptcha://",
                 })
        {
            if (t.StartsWith(bad, StringComparison.OrdinalIgnoreCase))
            {
                t = t[bad.Length..];
                break;
            }
        }

        // Server expects: scheme.sitekey.action.token  (e.g. signal-hcaptcha-short.…registration.…)
        return string.IsNullOrWhiteSpace(t) ? null : t.Trim();
    }

    private static string NormalizeVerificationCode(string code)
    {
        var digits = new string(code.Where(char.IsDigit).ToArray());
        if (digits.Length < 6)
            throw new ArgumentException("验证码无效", nameof(code));
        return digits;
    }

    private static byte[] DeriveUnidentifiedAccessKey(byte[] profileKey)
    {
        var hash = SHA256.HashData(profileKey);
        return hash.AsSpan(0, 16).ToArray();
    }

    private static string GeneratePassword()
    {
        var bytes = RandomNumberGenerator.GetBytes(16);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', 'A').Replace('/', 'B');
    }
}
