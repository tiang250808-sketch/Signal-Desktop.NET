using System.Threading.Channels;
using SignalCpf.Core.Models;
using SignalCpf.LibSignal;
using SignalCpf.Storage;

namespace SignalCpf.Client.Handlers;

/// <summary>
/// Shared mutable client state guarded by a single lock.
/// </summary>
internal sealed class ClientState
{
    private readonly object _gate = new();

    public Channel<SidecarEvent> Events { get; } = Channel.CreateUnbounded<SidecarEvent>();

    public AccountCredentials? Account { get; private set; }
    public ISignalProtocolService? Protocol { get; private set; }
    public bool NotificationsEnabled { get; set; } = true;

    public void SetAccount(AccountCredentials account)
    {
        lock (_gate)
            Account = account;
    }

    public void SetProtocol(ISignalProtocolService protocol)
    {
        lock (_gate)
            Protocol = protocol;
    }

    public void SetAccountAndProtocol(AccountCredentials account, ISignalProtocolService protocol)
    {
        lock (_gate)
        {
            Account = account;
            Protocol = protocol;
        }
    }

    public (AccountCredentials? Account, ISignalProtocolService? Protocol) Snapshot()
    {
        lock (_gate)
            return (Account, Protocol);
    }

    public void Emit(SidecarEvent ev) => Events.Writer.TryWrite(ev);

    public ValueTask EmitAsync(SidecarEvent ev, CancellationToken ct) =>
        Events.Writer.WriteAsync(ev, ct);
}
