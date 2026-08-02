using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using SignalCpf.Core.Options;
using SignalCpf.Net.Tls;
using Signalservice;

namespace SignalCpf.Net.WebSocket;

/// <summary>
/// Unauthenticated chat WebSocket used for REST-deprecated endpoints
/// (verification / registration). Official servers return HTTP 498
/// ("use websockets") for modern Desktop User-Agents on plain HTTPS.
/// </summary>
public sealed class UnauthenticatedApiSocket : IAsyncDisposable
{
    private readonly SignalServerOptions _options;
    private readonly ConcurrentDictionary<ulong, TaskCompletionSource<WebSocketResponseMessage>> _pending = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);

    private SignalWebSocketConnection? _connection;
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private long _nextId = 1;

    public UnauthenticatedApiSocket(SignalServerOptions options)
    {
        _options = options;
    }

    public async Task EnsureConnectedAsync(CancellationToken ct = default)
    {
        if (_connection?.State == WebSocketState.Open)
            return;

        await _connectGate.WaitAsync(ct);
        try
        {
            if (_connection?.State == WebSocketState.Open)
                return;

            await DisposeConnectionAsync();

            var uri = BuildUri(_options);
            _connection = new SignalWebSocketConnection(uri, opts =>
            {
                if (_options.AllowInsecureTls && !_options.IsOfficialLike)
                    opts.RemoteCertificateValidationCallback = static (_, _, _, _) => true;
                else if (SignalCertificateAuthority.ShouldUseSignalCa(_options))
                    opts.RemoteCertificateValidationCallback = SignalCertificateAuthority.CreateCallback();
            });
            _connection.SetSignalHeaders(_options.UserAgent);
            await _connection.ConnectAsync(ct);

            // Loop lifetime is owned by this socket (not the caller's short-lived CT).
            _loopCts = new CancellationTokenSource();
            _loopTask = Task.Run(() => ReceiveLoopAsync(_loopCts.Token), CancellationToken.None);
        }
        finally
        {
            _connectGate.Release();
        }
    }

    public async Task<(int StatusCode, string Body)> SendAsync(
        string verb,
        string path,
        byte[]? body = null,
        string? authorizationHeader = null,
        CancellationToken ct = default)
    {
        await EnsureConnectedAsync(ct);
        var connection = _connection
            ?? throw new InvalidOperationException("Unauthenticated WebSocket not connected");

        var id = (ulong)Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<WebSocketResponseMessage>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, tcs))
            throw new InvalidOperationException("Failed to register WebSocket request");

        var headers = new List<string>
        {
            "accept:application/json",
            $"user-agent:{_options.UserAgent}",
            $"x-signal-agent:{_options.UserAgent}",
        };
        if (body is { Length: > 0 })
            headers.Add("content-type:application/json");
        if (!string.IsNullOrEmpty(authorizationHeader))
            headers.Add("authorization:" + authorizationHeader);

        try
        {
            await connection.SendRequestAsync(id, verb, NormalizePath(path), body, headers, ct);

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));
            var response = await tcs.Task.WaitAsync(timeoutCts.Token);

            var text = response.Body is null || response.Body.IsEmpty
                ? string.Empty
                : Encoding.UTF8.GetString(response.Body.ToByteArray());
            if (string.IsNullOrWhiteSpace(text) && !string.IsNullOrWhiteSpace(response.Message))
                text = response.Message;
            return ((int)response.Status, text);
        }
        catch
        {
            _pending.TryRemove(id, out _);
            throw;
        }
    }

    public async Task<(int StatusCode, string Body)> SendJsonAsync<T>(
        string verb,
        string path,
        T? body,
        JsonSerializerOptions jsonOpts,
        string? authorizationHeader = null,
        CancellationToken ct = default)
    {
        byte[]? bytes = null;
        if (body is not null)
            bytes = JsonSerializer.SerializeToUtf8Bytes(body, jsonOpts);
        return await SendAsync(verb, path, bytes, authorizationHeader, ct);
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

                if (msg.Type == WebSocketMessage.Types.Type.Response && msg.Response is not null)
                {
                    if (_pending.TryRemove(msg.Response.Id, out var tcs))
                        tcs.TrySetResult(msg.Response);
                    continue;
                }

                if (msg.Type == WebSocketMessage.Types.Type.Request && msg.Request is not null)
                {
                    // Keepalive / unexpected server pushes — ACK so the socket stays open.
                    await _connection.SendResponseAsync(msg.Request.Id, 200, "OK", ct: ct);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        catch (Exception ex)
        {
            FailAllPending(ex);
            return;
        }

        FailAllPending(new InvalidOperationException("Unauthenticated WebSocket closed"));
    }

    private void FailAllPending(Exception ex)
    {
        foreach (var key in _pending.Keys.ToArray())
        {
            if (_pending.TryRemove(key, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    private static string NormalizePath(string path) =>
        path.StartsWith('/') ? path : "/" + path;

    public static Uri BuildUri(SignalServerOptions options)
    {
        var api = options.ApiBaseUri;
        return new UriBuilder(api)
        {
            Scheme = string.Equals(api.Scheme, "http", StringComparison.OrdinalIgnoreCase) ? "ws" : "wss",
            Path = "/v1/websocket/",
            Query = string.Empty,
        }.Uri;
    }

    private async Task DisposeConnectionAsync()
    {
        try
        {
            _loopCts?.Cancel();
        }
        catch
        {
            // ignore
        }

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
    }

    public async ValueTask DisposeAsync()
    {
        FailAllPending(new ObjectDisposedException(nameof(UnauthenticatedApiSocket)));
        await DisposeConnectionAsync();
        _connectGate.Dispose();
    }
}
