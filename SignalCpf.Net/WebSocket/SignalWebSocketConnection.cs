using System.Net.WebSockets;
using Google.Protobuf;
using Signalservice;

namespace SignalCpf.Net.WebSocket;

/// <summary>
/// Low-level Signal WebSocket framing (WebSocketMessage request/response).
/// </summary>
public sealed class SignalWebSocketConnection : IAsyncDisposable
{
    private readonly ClientWebSocket _socket = new();
    private readonly Uri _uri;
    private readonly byte[] _buffer = new byte[1024 * 256];

    public SignalWebSocketConnection(Uri uri, Action<ClientWebSocketOptions>? configure = null)
    {
        _uri = uri;
        configure?.Invoke(_socket.Options);
    }

    /// <summary>Set common Signal client headers before <see cref="ConnectAsync"/>.</summary>
    public void SetSignalHeaders(string userAgent)
    {
        try
        {
            _socket.Options.SetRequestHeader("User-Agent", userAgent);
            _socket.Options.SetRequestHeader("X-Signal-Agent", userAgent);
        }
        catch (InvalidOperationException)
        {
            // Headers already committed / platform restriction — ignore.
        }
    }

    public WebSocketState State => _socket.State;

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        await _socket.ConnectAsync(_uri, ct);
    }

    public async Task SendResponseAsync(
        ulong requestId,
        uint status,
        string message = "OK",
        byte[]? body = null,
        CancellationToken ct = default)
    {
        var wsMessage = new WebSocketMessage
        {
            Type = WebSocketMessage.Types.Type.Response,
            Response = new WebSocketResponseMessage
            {
                Id = requestId,
                Status = status,
                Message = message,
            },
        };
        if (body is { Length: > 0 })
            wsMessage.Response.Body = ByteString.CopyFrom(body);

        await SendProtoAsync(wsMessage, ct);
    }

    public async Task SendRequestAsync(
        ulong id,
        string verb,
        string path,
        byte[]? body = null,
        IEnumerable<string>? headers = null,
        CancellationToken ct = default)
    {
        var wsMessage = new WebSocketMessage
        {
            Type = WebSocketMessage.Types.Type.Request,
            Request = new WebSocketRequestMessage
            {
                Id = id,
                Verb = verb,
                Path = path,
            },
        };
        if (body is { Length: > 0 })
            wsMessage.Request.Body = ByteString.CopyFrom(body);
        if (headers is not null)
        {
            foreach (var header in headers)
                wsMessage.Request.Headers.Add(header);
        }

        await SendProtoAsync(wsMessage, ct);
    }

    public async Task<WebSocketMessage?> ReceiveAsync(CancellationToken ct = default)
    {
        using var ms = new MemoryStream();
        ValueWebSocketReceiveResult result;
        do
        {
            result = await _socket.ReceiveAsync(_buffer.AsMemory(), ct);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None);
                return null;
            }

            ms.Write(_buffer, 0, result.Count);
        } while (!result.EndOfMessage);

        if (ms.Length == 0)
            return null;

        return WebSocketMessage.Parser.ParseFrom(ms.ToArray());
    }

    private async Task SendProtoAsync(WebSocketMessage message, CancellationToken ct)
    {
        var bytes = message.ToByteArray();
        await _socket.SendAsync(bytes, WebSocketMessageType.Binary, endOfMessage: true, ct);
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "dispose", CancellationToken.None);
            }
            catch
            {
                // ignored
            }
        }

        _socket.Dispose();
    }
}
