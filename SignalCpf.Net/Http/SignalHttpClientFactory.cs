using SignalCpf.Core.Options;
using SignalCpf.Net.Tls;

namespace SignalCpf.Net.Http;

public static class SignalHttpClientFactory
{
    public static HttpClient Create(SignalServerOptions options)
    {
        var handler = new HttpClientHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
        };

        if (options.AllowInsecureTls && !options.IsOfficialLike)
        {
            handler.ServerCertificateCustomValidationCallback =
                static (_, _, _, _) => true;
        }
        else if (SignalCertificateAuthority.ShouldUseSignalCa(options))
        {
            handler.ServerCertificateCustomValidationCallback =
                SignalCertificateAuthority.Validate;
        }

        var client = new HttpClient(handler)
        {
            BaseAddress = options.ApiBaseUri,
            Timeout = TimeSpan.FromMinutes(2),
        };
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", options.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("X-Signal-Agent", options.UserAgent);
        client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "*/*");
        return client;
    }
}
