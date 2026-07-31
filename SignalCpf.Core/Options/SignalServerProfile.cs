namespace SignalCpf.Core.Options;

/// <summary>Which Signal server constellation to target.</summary>
public enum SignalServerProfile
{
    /// <summary>Local / self-hosted (default). Requires SIGNAL_SERVER_URL.</summary>
    SelfHosted = 0,

    /// <summary>Official production: chat.signal.org + cdn/storage.</summary>
    Official = 1,

    /// <summary>Official staging (Signal-Desktop default.json).</summary>
    Staging = 2,
}
