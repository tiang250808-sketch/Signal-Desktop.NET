using System.Threading.Channels;
using SignalCpf.Core.Options;
using SignalCpf.Net.Http;
using SignalCpf.Net.Tls;
using SignalCpf.Net.WebSocket;
using Signalservice;

namespace SignalCpf.Net.Messaging;

/// <summary>
/// Authenticated Signal message WebSocket: receives Envelopes and ACKs them.
/// </summary>
public sealed class AuthenticatedMessageSocket : IAsyncDisposable
{
    private readonly SignalServerOptions _options;
    private readonly string _aci;
    private readonly int _deviceId;
    private readonly string _password;
    private readonly Channel<IncomingEnvelope> _envelopes =
        Channel.CreateUnbounded<IncomingEnvelope>();

    private SignalWebSocketConnection? _connection;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;

    public AuthenticatedMessageSocket(
        SignalServerOptions options,
        string aci,
        int deviceId,
        string password)
    {
        _options = options;
        _aci = aci;
        _deviceId = deviceId;
        _password = password;
    }

    public ChannelReader<IncomingEnvelope> Envelopes => _envelopes.Reader;

    public async Task StartAsync(CancellationToken ct = default)
    {
        var uri = BuildAuthenticatedUri(_options, _aci, _deviceId, _password);
        _connection = new SignalWebSocketConnection(uri, opts =>
        {
            if (_options.AllowInsecureTls && !_options.IsOfficialLike)
                opts.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
            else if (SignalCertificateAuthority.ShouldUseSignalCa(_options))
                opts.RemoteCertificateValidationCallback = SignalCertificateAuthority.CreateCallback();
        });
        _connection.SetSignalHeaders(_options.UserAgent);

        await _connection.ConnectAsync(ct);
        _loopCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        _loopTask = Task.Run(() => ReceiveLoopAsync(_loopCts.Token), CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _connection is not null)
            {
                var msg = await _connection.ReceiveAsync(ct);
                if (msg is null)
                    break;

                if (msg.Type != WebSocketMessage.Types.Type.Request || msg.Request is null)
                    continue;

                var req = msg.Request;
                var path = req.Path ?? string.Empty;

                if (path.Equals("/api/v1/message", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith("/message", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var envelope = Envelope.Parser.ParseFrom(req.Body);
                        await _envelopes.Writer.WriteAsync(
                            new IncomingEnvelope(req.Id, envelope, req.Body.ToByteArray()),
                            ct);
                    }
                    catch (Exception ex)
                    {
                        await _connection.SendResponseAsync(req.Id, 400, ex.Message, ct: ct);
                        continue;
                    }

                    await _connection.SendResponseAsync(req.Id, 200, "OK", ct: ct);
                }
                else if (path.Contains("queue/empty", StringComparison.OrdinalIgnoreCase))
                {
                    await _connection.SendResponseAsync(req.Id, 200, "OK", ct: ct);
                }
                else
                {
                    await _connection.SendResponseAsync(req.Id, 200, "OK", ct: ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            _envelopes.Writer.TryComplete(ex);
            return;
        }

        _envelopes.Writer.TryComplete();
    }

    public static Uri BuildAuthenticatedUri(
        SignalServerOptions options,
        string aci,
        int deviceId,
        string password)
    {
        var login = $"{SignalAuth.NormalizeAci(aci)}.{deviceId}";
        var api = options.ApiBaseUri;
        var builder = new UriBuilder(api)
        {
            Scheme = string.Equals(api.Scheme, "http", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss",
            Path = "/v1/websocket/",
            Query =
                "login=" + Uri.EscapeDataString(login)
                + "&password=" + Uri.EscapeDataString(password),
        };
        return builder.Uri;
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            _loopCts?.Cancel();
        }
        catch
        {
            // ignore
        }

        // Close the socket first so ReceiveAsync unblocks promptly.
        if (_connection is not null)
        {
            try
            {
                await _connection.DisposeAsync();
            }
            catch
            {
                // ignore
            }

            _connection = null;
        }

        if (_loopTask is not null)
        {
            try
            {
                await _loopTask.WaitAsync(TimeSpan.FromSeconds(2));
            }
            catch
            {
                // ignore hang/cancel during shutdown
            }

            _loopTask = null;
        }

        _loopCts?.Dispose();
        _loopCts = null;
        _envelopes.Writer.TryComplete();
    }
}

public sealed record IncomingEnvelope(ulong RequestId, Envelope Envelope, byte[] RawBytes);
