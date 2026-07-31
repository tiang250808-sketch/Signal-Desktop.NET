namespace SignalCpf.Protocol.Provisioning;

/// <summary>
/// QR link URL builder adapted from Signal-Desktop linkDeviceRoute.toAppUrl.
/// </summary>
public static class LinkDeviceUrl
{
    public static string Build(string uuid, string pubKeyBase64, IReadOnlyList<string>? capabilities = null)
    {
        var caps = capabilities is { Count: > 0 }
            ? string.Join(",", capabilities)
            : string.Empty;

        return "sgnl://linkdevice?"
               + "uuid=" + Uri.EscapeDataString(uuid)
               + "&pub_key=" + Uri.EscapeDataString(pubKeyBase64)
               + "&capabilities=" + Uri.EscapeDataString(caps);
    }

    public static string NormalizeDeviceName(string? rawDeviceName) =>
        (rawDeviceName ?? string.Empty).Trim().Replace("\0", string.Empty);
}
