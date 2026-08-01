using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using SignalCpf.Core.Options;

namespace SignalCpf.Net.Http;

public sealed class SignalRestClient : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true,
    };

    private readonly HttpClient _http;
    private AuthenticationHeaderValue? _auth;

    public SignalRestClient(SignalServerOptions options, HttpClient? httpClient = null)
    {
        Options = options;
        _http = httpClient ?? SignalHttpClientFactory.Create(options);
    }

    public SignalServerOptions Options { get; }

    public void SetDeviceAuth(string aci, int deviceId, string password) =>
        _auth = SignalAuth.DeviceBasic(aci, deviceId, password);

    public void SetLinkAuth(string aci, string password) =>
        _auth = SignalAuth.LinkBasic(aci, password);

    public void SetNumberAuth(string e164, string password) =>
        _auth = SignalAuth.NumberBasic(e164, password);

    public void ClearAuth() => _auth = null;

    public async Task<bool> HealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "v1/config");
            ApplyAuth(req);
            using var resp = await _http.SendAsync(req, ct);
            return resp.IsSuccessStatusCode || (int)resp.StatusCode is 401 or 404;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LinkDeviceResponse> LinkDeviceAsync(
        LinkDeviceRequest body,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, "v1/devices/link")
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new SignalApiException((int)resp.StatusCode, text);

        var parsed = JsonSerializer.Deserialize<LinkDeviceResponse>(text, JsonOpts)
                     ?? throw new SignalApiException((int)resp.StatusCode, "Empty linkDevice response");
        return parsed;
    }

    public async Task<VerificationSessionResponse> CreateVerificationSessionAsync(
        CreateVerificationSessionRequest body,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/verification/session")
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        using var resp = await _http.SendAsync(req, ct);
        return await ReadVerificationSessionAsync(resp, ct);
    }

    public async Task<VerificationSessionResponse> UpdateVerificationSessionAsync(
        string sessionId,
        UpdateVerificationSessionRequest body,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Patch,
            $"v1/verification/session/{Uri.EscapeDataString(sessionId)}")
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        using var resp = await _http.SendAsync(req, ct);
        return await ReadVerificationSessionAsync(resp, ct);
    }

    public async Task<VerificationSessionResponse> RequestVerificationCodeAsync(
        string sessionId,
        VerificationCodeRequest body,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1/verification/session/{Uri.EscapeDataString(sessionId)}/code")
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        using var resp = await _http.SendAsync(req, ct);
        return await ReadVerificationSessionAsync(resp, ct);
    }

    public async Task<VerificationSessionResponse> SubmitVerificationCodeAsync(
        string sessionId,
        SubmitVerificationCodeRequest body,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(
            HttpMethod.Put,
            $"v1/verification/session/{Uri.EscapeDataString(sessionId)}/code")
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        using var resp = await _http.SendAsync(req, ct);
        return await ReadVerificationSessionAsync(resp, ct);
    }

    public async Task<AccountCreationResponse> RegisterAccountAsync(
        RegistrationRequest body,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "v1/registration")
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new SignalApiException((int)resp.StatusCode, text);

        var parsed = JsonSerializer.Deserialize<AccountCreationResponse>(text, JsonOpts)
                     ?? throw new SignalApiException((int)resp.StatusCode, "Empty registration response");
        return parsed;
    }

    private static async Task<VerificationSessionResponse> ReadVerificationSessionAsync(
        HttpResponseMessage resp,
        CancellationToken ct)
    {
        var text = await resp.Content.ReadAsStringAsync(ct);
        // 409/429/418 often still return a session body the client should inspect.
        if ((int)resp.StatusCode is 409 or 429 or 418)
        {
            var conflict = JsonSerializer.Deserialize<VerificationSessionResponse>(text, JsonOpts);
            if (conflict is not null && !string.IsNullOrEmpty(conflict.Id))
                return conflict;
        }

        if (!resp.IsSuccessStatusCode)
            throw new SignalApiException((int)resp.StatusCode, text);

        return JsonSerializer.Deserialize<VerificationSessionResponse>(text, JsonOpts)
               ?? throw new SignalApiException((int)resp.StatusCode, "Empty verification session response");
    }

    public async Task RegisterPreKeysAsync(
        string path,
        PreKeyUploadRequest body,
        CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            throw new SignalApiException((int)resp.StatusCode, text);
        }
    }

    public async Task<PreKeyBundleResponse?> GetPreKeyBundleAsync(
        string serviceId,
        int deviceId = 1,
        CancellationToken ct = default)
    {
        // deviceId "*" fetches all devices (official clients).
        var devicePart = deviceId < 0 ? "*" : deviceId.ToString();
        var path = $"v2/keys/{Uri.EscapeDataString(serviceId)}/{devicePart}";
        using var req = new HttpRequestMessage(HttpMethod.Get, path);
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req, ct);
        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
            return null;
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new SignalApiException((int)resp.StatusCode, text);
        return JsonSerializer.Deserialize<PreKeyBundleResponse>(text, JsonOpts);
    }

    /// <summary>Fetch delivery sender certificate for sealed sender (base64 body or JSON).</summary>
    public async Task<byte[]?> GetSenderCertificateAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "v1/certificate/delivery?includeUuid=true");
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
            return null;

        var text = (await resp.Content.ReadAsStringAsync(ct)).Trim();
        if (text.Length == 0)
            return null;

        try
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.TryGetProperty("certificate", out var certProp))
                text = certProp.GetString() ?? text;
        }
        catch (JsonException)
        {
            // raw base64
        }

        try
        {
            return Convert.FromBase64String(text.Trim('"'));
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public async Task SendMessagesAsync(
        string destination,
        OutgoingMessageRequest body,
        bool? story = null,
        CancellationToken ct = default)
    {
        var path = $"v1/messages/{Uri.EscapeDataString(destination)}";
        if (story == true)
            path += "?story=true";

        using var req = new HttpRequestMessage(HttpMethod.Put, path)
        {
            Content = JsonContent.Create(body, options: JsonOpts),
        };
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            var text = await resp.Content.ReadAsStringAsync(ct);
            throw new SignalApiException((int)resp.StatusCode, text);
        }
    }

    public async Task<byte[]> UploadAttachmentAsync(
        byte[] ciphertext,
        string contentType,
        CancellationToken ct = default)
    {
        // Attachment form upload is CDN-specific; return ciphertext for local staging.
        await Task.CompletedTask;
        return ciphertext;
    }

    public async Task<string> GetStringAsync(string relativePath, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, relativePath.TrimStart('/'));
        ApplyAuth(req);
        using var resp = await _http.SendAsync(req, ct);
        var text = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new SignalApiException((int)resp.StatusCode, text);
        return text;
    }

    private void ApplyAuth(HttpRequestMessage req)
    {
        if (_auth is not null)
            req.Headers.Authorization = _auth;
    }

    public void Dispose() => _http.Dispose();
}

public sealed class SignalApiException(int statusCode, string body)
    : Exception($"Signal API {statusCode}: {Truncate(body)}")
{
    public int StatusCode { get; } = statusCode;
    public string ResponseBody { get; } = body;

    private static string Truncate(string s) =>
        s.Length <= 512 ? s : s[..512] + "…";
}

public sealed class LinkDeviceRequest
{
    public string VerificationCode { get; set; } = "";
    public AccountAttributes AccountAttributes { get; set; } = new();
    public SignedPreKeyEntity? AciSignedPreKey { get; set; }
    public SignedPreKeyEntity? PniSignedPreKey { get; set; }
    public KyberPreKeyEntity? AciPqLastResortPreKey { get; set; }
    public KyberPreKeyEntity? PniPqLastResortPreKey { get; set; }
}

public sealed class AccountAttributes
{
    public bool FetchesMessages { get; set; } = true;
    public int RegistrationId { get; set; }
    public int PniRegistrationId { get; set; }
    public string? Name { get; set; }
    /// <summary>Legacy numeric capabilities (link-device / older forks).</summary>
    public int? Capabilities { get; set; }
}

/// <summary>Account attributes for primary registration (capability map + UAK).</summary>
public sealed class RegistrationAccountAttributes
{
    public bool FetchesMessages { get; set; } = true;
    public int RegistrationId { get; set; }
    public int PniRegistrationId { get; set; }
    public string? Name { get; set; }
    public Dictionary<string, bool>? Capabilities { get; set; }
    public string? UnidentifiedAccessKey { get; set; }
    public bool UnrestrictedUnidentifiedAccess { get; set; }
    public bool DiscoverableByPhoneNumber { get; set; } = true;
}

public sealed class CreateVerificationSessionRequest
{
    public string Number { get; set; } = "";
    public string? Captcha { get; set; }
}

public sealed class UpdateVerificationSessionRequest
{
    public string? Captcha { get; set; }
    public string? PushToken { get; set; }
    public string? PushTokenType { get; set; }
    public string? PushChallenge { get; set; }
}

public sealed class VerificationCodeRequest
{
    public string Transport { get; set; } = "sms";
    /// <summary>Server maps "ios" → IOS client type (used by Desktop).</summary>
    public string Client { get; set; } = "ios";
}

public sealed class SubmitVerificationCodeRequest
{
    public string Code { get; set; } = "";
}

public sealed class VerificationSessionResponse
{
    public string Id { get; set; } = "";
    public long? NextSms { get; set; }
    public long? NextCall { get; set; }
    public long? NextVerificationAttempt { get; set; }
    public bool AllowedToRequestCode { get; set; }
    public List<string>? RequestedInformation { get; set; }
    public bool Verified { get; set; }
}

public sealed class RegistrationRequest
{
    public string? SessionId { get; set; }
    public RegistrationAccountAttributes AccountAttributes { get; set; } = new();
    public bool SkipDeviceTransfer { get; set; } = true;
    public string AciIdentityKey { get; set; } = "";
    public string PniIdentityKey { get; set; } = "";
    public SignedPreKeyEntity? AciSignedPreKey { get; set; }
    public SignedPreKeyEntity? PniSignedPreKey { get; set; }
    public KyberPreKeyEntity? AciPqLastResortPreKey { get; set; }
    public KyberPreKeyEntity? PniPqLastResortPreKey { get; set; }
}

public sealed class AccountCreationResponse
{
    public string? Uuid { get; set; }
    public string? Number { get; set; }
    public string? Pni { get; set; }
    public bool StorageCapable { get; set; }
    public bool Reregistration { get; set; }
}

public sealed class SignedPreKeyEntity
{
    public uint KeyId { get; set; }
    public string PublicKey { get; set; } = "";
    public string Signature { get; set; } = "";
}

public sealed class KyberPreKeyEntity
{
    public uint KeyId { get; set; }
    public string PublicKey { get; set; } = "";
    public string Signature { get; set; } = "";
}

public sealed class LinkDeviceResponse
{
    public int DeviceId { get; set; }
}

public sealed class PreKeyUploadRequest
{
    public SignedPreKeyEntity? SignedPreKey { get; set; }
    public List<PreKeyEntity>? PreKeys { get; set; }
    public KyberPreKeyEntity? PqLastResortPreKey { get; set; }
    public List<KyberPreKeyEntity>? PqPreKeys { get; set; }
}

public sealed class PreKeyEntity
{
    public uint KeyId { get; set; }
    public string PublicKey { get; set; } = "";
}

public sealed class PreKeyBundleResponse
{
    public string? IdentityKey { get; set; }
    public List<DevicePreKeys>? Devices { get; set; }
}

public sealed class DevicePreKeys
{
    public int DeviceId { get; set; }
    public int RegistrationId { get; set; }
    public PreKeyEntity? PreKey { get; set; }
    public SignedPreKeyEntity? SignedPreKey { get; set; }
    public KyberPreKeyEntity? PqPreKey { get; set; }
}

public sealed class OutgoingMessageRequest
{
    public List<OutgoingMessage> Messages { get; set; } = [];
    public long Timestamp { get; set; }
    public bool Online { get; set; }
    public bool Urgent { get; set; } = true;
}

public sealed class OutgoingMessage
{
    /// <summary>Envelope type: 1 ciphertext, 3 prekey, 6 unidentified.</summary>
    public int Type { get; set; } = 1;
    public int DestinationDeviceId { get; set; }
    public int DestinationRegistrationId { get; set; }
    public string Content { get; set; } = "";
}
