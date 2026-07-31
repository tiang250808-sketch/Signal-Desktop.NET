using System.Net.WebSockets;
using SignalCpf.Core.Options;
using SignalCpf.Net.Tls;
using SignalCpf.Net.WebSocket;
using Signalservice;

namespace SignalCpf.Net.Provisioning;

public sealed class ProvisioningSocket : IAsyncDisposable
{
    private readonly SignalServerOptions _options;
    private SignalWebSocketConnection? _connection;

    public ProvisioningSocket(SignalServerOptions options)
    {
        _options = options;
    }

    /// <summary>
    /// Connects to the provisioning WebSocket, waits for address + envelope.
    /// </summary>
    public async Task<ProvisioningSessionResult> RunAsync(
        Func<string, Task>? onAddressReady,
        CancellationToken ct = default)
    {
        var uri = BuildProvisioningUri(_options);
        _connection = new SignalWebSocketConnection(uri, opts =>
        {
            if (_options.AllowInsecureTls && !_options.IsOfficialLike)
                opts.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
            else if (SignalCertificateAuthority.ShouldUseSignalCa(_options))
                opts.RemoteCertificateValidationCallback = SignalCertificateAuthority.CreateCallback();
        });
        _connection.SetSignalHeaders(_options.UserAgent);

        try
        {
            await _connection.ConnectAsync(ct);
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            var hint = _options.IsOfficialLike
                ? "已使用官方端点。HTTP 499 通常表示 User-Agent 被 RemoteDeprecationFilter 拒绝，可设置较新的 SIGNAL_USER_AGENT（如 Signal-Desktop/8.20.0）。亦需确认网络可达且已配置 Signal CA。"
                : "请设置 SIGNAL_SERVER_URL；自签证书加 SIGNAL_SERVER_INSECURE_TLS=1；或 SIGNAL_SERVER_PROFILE=official。";
            throw new InvalidOperationException(
                $"无法连接配钥 WebSocket：{uri}（API={_options.ApiBaseUrl}，profile={_options.Profile}，UA={_options.UserAgent}）。" +
                $"原因：{detail}。{hint}",
                ex);
        }

        string? address = null;
        byte[]? envelope = null;

        while (!ct.IsCancellationRequested && (address is null || envelope is null))
        {
            var msg = await _connection.ReceiveAsync(ct);
            if (msg is null)
                throw new InvalidOperationException("Provisioning WebSocket closed unexpectedly");

            if (msg.Type != WebSocketMessage.Types.Type.Request || msg.Request is null)
                continue;

            var req = msg.Request;
            var path = req.Path ?? string.Empty;

            if (path.Contains("address", StringComparison.OrdinalIgnoreCase))
            {
                var addr = ProvisioningAddress.Parser.ParseFrom(req.Body);
                address = addr.Address;
                await _connection.SendResponseAsync(req.Id, 200, "OK", ct: ct);
                if (onAddressReady is not null && !string.IsNullOrEmpty(address))
                    await onAddressReady(address);
            }
            else if (path.Contains("message", StringComparison.OrdinalIgnoreCase))
            {
                envelope = req.Body.ToByteArray();
                await _connection.SendResponseAsync(req.Id, 200, "OK", ct: ct);
            }
            else
            {
                await _connection.SendResponseAsync(req.Id, 400, "Unknown path", ct: ct);
            }
        }

        if (string.IsNullOrEmpty(address) || envelope is null)
            throw new InvalidOperationException("Provisioning incomplete: missing address or envelope");

        return new ProvisioningSessionResult(address, envelope);
    }

    public static Uri BuildProvisioningUri(SignalServerOptions options)
    {
        var api = options.ApiBaseUri;
        var builder = new UriBuilder(api)
        {
            Scheme = string.Equals(api.Scheme, "http", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss",
            Path = "/v1/websocket/provisioning/",
            Query = string.Empty,
        };
        return builder.Uri;
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
            await _connection.DisposeAsync();
    }
}

public sealed record ProvisioningSessionResult(string Address, byte[] EnvelopeBytes);
