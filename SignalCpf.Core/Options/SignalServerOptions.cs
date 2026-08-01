namespace SignalCpf.Core.Options;

/// <summary>
/// Configurable Signal protocol server endpoints.
/// Use <see cref="SignalServerProfile.Official"/> / <c>SIGNAL_SERVER_PROFILE=official</c>
/// for production hosts. Self-hosted remains the safe default.
/// </summary>
public sealed class SignalServerOptions
{
    public SignalServerProfile Profile { get; set; } = SignalServerProfile.SelfHosted;

    /// <summary>HTTPS API base, e.g. https://chat.signal.org or https://localhost</summary>
    public string ApiBaseUrl { get; set; } = "https://localhost";

    /// <summary>CDN-0 attachment base. Falls back to ApiBaseUrl when empty.</summary>
    public string? CdnUrl { get; set; }

    /// <summary>CDN-2 base (optional).</summary>
    public string? Cdn2Url { get; set; }

    /// <summary>CDN-3 base (optional).</summary>
    public string? Cdn3Url { get; set; }

    /// <summary>Storage service URL.</summary>
    public string? StorageUrl { get; set; }

    /// <summary>Captcha / challenge page URL (registration flows).</summary>
    public string? ChallengeUrl { get; set; }

    /// <summary>HTTP / WebSocket User-Agent.</summary>
    public string UserAgent { get; set; } = "SignalCpf/0.2.0";

    /// <summary>Default linked-device display name.</summary>
    public string DeviceName { get; set; } = "CPF Desktop";

    /// <summary>
    /// Bypass TLS validation (self-signed only). Forced off for Official/Staging.
    /// </summary>
    public bool AllowInsecureTls { get; set; }

    /// <summary>Include PQ (Kyber) last-resort prekeys when true.</summary>
    public bool EnablePqKeys { get; set; } = true;

    /// <summary>Directory for SQLite + credential files.</summary>
    public string DataDirectory { get; set; } =
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SignalCpf");

    public bool IsOfficialLike =>
        Profile is SignalServerProfile.Official or SignalServerProfile.Staging;

    public Uri ApiBaseUri => new(ApiBaseUrl.TrimEnd('/') + "/");

    public string CdnBaseUrl =>
        string.IsNullOrWhiteSpace(CdnUrl) ? ApiBaseUrl.TrimEnd('/') : CdnUrl.TrimEnd('/');

    public void ApplyProfile(SignalServerProfile profile)
    {
        Profile = profile;
        switch (profile)
        {
            case SignalServerProfile.Official:
                ApiBaseUrl = "https://chat.signal.org";
                CdnUrl = "https://cdn.signal.org";
                Cdn2Url = "https://cdn2.signal.org";
                Cdn3Url = "https://cdn3.signal.org";
                StorageUrl = "https://storage.signal.org";
                ChallengeUrl = "https://signalcaptchas.org/challenge/generate.html";
                // Production RemoteDeprecationFilter returns HTTP 499 for outdated Desktop UAs.
                if (UserAgent.StartsWith("SignalCpf/", StringComparison.Ordinal))
                    UserAgent = "Signal-Desktop/8.20.0";
                AllowInsecureTls = false;
                break;

            case SignalServerProfile.Staging:
                ApiBaseUrl = "https://chat.staging.signal.org";
                CdnUrl = "https://cdn-staging.signal.org";
                Cdn2Url = "https://cdn2-staging.signal.org";
                Cdn3Url = "https://cdn3-staging.signal.org";
                StorageUrl = "https://storage-staging.signal.org";
                ChallengeUrl = "https://signalcaptchas.org/staging/challenge/generate.html";
                if (UserAgent.StartsWith("SignalCpf/", StringComparison.Ordinal))
                    UserAgent = "Signal-Desktop/8.20.0";
                AllowInsecureTls = false;
                break;

            default:
                // Self-hosted: keep existing ApiBaseUrl / leave localhost default.
                break;
        }
    }

    public static SignalServerOptions FromEnvironment()
    {
        var opts = new SignalServerOptions();

        var profileRaw = Environment.GetEnvironmentVariable("SIGNAL_SERVER_PROFILE");
        var profile = ParseProfile(profileRaw);
        opts.ApplyProfile(profile);

        // Explicit URL overrides profile defaults (and is required for self-hosted).
        var api = Environment.GetEnvironmentVariable("SIGNAL_SERVER_URL");
        if (!string.IsNullOrWhiteSpace(api))
            opts.ApiBaseUrl = api.Trim();

        var cdn = Environment.GetEnvironmentVariable("SIGNAL_CDN_URL");
        if (!string.IsNullOrWhiteSpace(cdn))
            opts.CdnUrl = cdn.Trim();

        var cdn2 = Environment.GetEnvironmentVariable("SIGNAL_CDN2_URL");
        if (!string.IsNullOrWhiteSpace(cdn2))
            opts.Cdn2Url = cdn2.Trim();

        var cdn3 = Environment.GetEnvironmentVariable("SIGNAL_CDN3_URL");
        if (!string.IsNullOrWhiteSpace(cdn3))
            opts.Cdn3Url = cdn3.Trim();

        var storage = Environment.GetEnvironmentVariable("SIGNAL_STORAGE_URL");
        if (!string.IsNullOrWhiteSpace(storage))
            opts.StorageUrl = storage.Trim();

        var challenge = Environment.GetEnvironmentVariable("SIGNAL_CHALLENGE_URL");
        if (!string.IsNullOrWhiteSpace(challenge))
            opts.ChallengeUrl = challenge.Trim();

        var ua = Environment.GetEnvironmentVariable("SIGNAL_USER_AGENT");
        if (!string.IsNullOrWhiteSpace(ua))
            opts.UserAgent = ua.Trim();

        var device = Environment.GetEnvironmentVariable("SIGNAL_DEVICE_NAME");
        if (!string.IsNullOrWhiteSpace(device))
            opts.DeviceName = device.Trim();

        var data = Environment.GetEnvironmentVariable("SIGNAL_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(data))
            opts.DataDirectory = data.Trim();

        var insecure = Environment.GetEnvironmentVariable("SIGNAL_SERVER_INSECURE_TLS");
        var wantInsecure =
            string.Equals(insecure, "1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(insecure, "true", StringComparison.OrdinalIgnoreCase);
        // Never allow TLS bypass against official/staging hosts.
        opts.AllowInsecureTls = wantInsecure && !opts.IsOfficialLike;

        var pq = Environment.GetEnvironmentVariable("SIGNAL_ENABLE_PQ_KEYS");
        if (!string.IsNullOrWhiteSpace(pq))
        {
            opts.EnablePqKeys =
                string.Equals(pq, "1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(pq, "true", StringComparison.OrdinalIgnoreCase);
        }

        return opts;
    }

    private static SignalServerProfile ParseProfile(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return SignalServerProfile.Official;

        return raw.Trim().ToLowerInvariant() switch
        {
            "official" or "production" or "prod" or "signal" => SignalServerProfile.Official,
            "staging" or "stage" => SignalServerProfile.Staging,
            "selfhosted" or "self-hosted" or "local" or "localhost" => SignalServerProfile.SelfHosted,
            _ => SignalServerProfile.SelfHosted,
        };
    }
}
