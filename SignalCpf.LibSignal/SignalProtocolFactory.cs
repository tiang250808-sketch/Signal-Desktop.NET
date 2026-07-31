using SignalCpf.LibSignal.Native;
using SignalCpf.Storage;

namespace SignalCpf.LibSignal;

public static class SignalProtocolFactory
{
    public static ISignalProtocolService Create(IMessageStore store, AccountCredentials credentials)
    {
        if (LibSignalNative.IsAvailable)
            return new FfiSignalProtocolService(store, credentials);

        return new ManagedSignalProtocol(store, credentials);
    }
}
