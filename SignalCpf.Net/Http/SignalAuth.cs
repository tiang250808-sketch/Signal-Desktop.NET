using System.Net.Http.Headers;
using System.Text;

namespace SignalCpf.Net.Http;

public static class SignalAuth
{
    /// <summary>Basic auth for an already-linked device: {aci}.{deviceId}:{password}</summary>
    public static AuthenticationHeaderValue DeviceBasic(string aci, int deviceId, string password)
    {
        var token = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{NormalizeAci(aci)}.{deviceId}:{password}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    /// <summary>Basic auth used during linkDevice before a deviceId is assigned.</summary>
    public static AuthenticationHeaderValue LinkBasic(string aci, string password)
    {
        var token = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{NormalizeAci(aci)}:{password}"));
        return new AuthenticationHeaderValue("Basic", token);
    }

    public static string NormalizeAci(string aci) =>
        Guid.TryParse(aci, out var g)
            ? g.ToString().ToLowerInvariant()
            : aci.Trim().ToLowerInvariant();
}
