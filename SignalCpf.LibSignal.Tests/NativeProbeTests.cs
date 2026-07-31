using SignalCpf.LibSignal.Native;
using Xunit;

namespace SignalCpf.LibSignal.Tests;

public class NativeProbeTests
{
    [Fact]
    public void Probe_DoesNotThrow()
    {
        // Availability depends on whether scripts/build-libsignal-ffi.ps1 has been run.
        _ = LibSignalNative.IsAvailable;
    }

    [Fact]
    public void EnvelopeType_Constants_MatchSignalWire()
    {
        Assert.Equal(1, SignalEnvelopeType.Ciphertext);
        Assert.Equal(3, SignalEnvelopeType.PreKeyBundle);
        Assert.Equal(6, SignalEnvelopeType.UnidentifiedSender);
    }

    [Fact]
    public void CiphertextTypeMapping_RoundTripsCommonCases()
    {
        Assert.Equal(
            SignalEnvelopeType.Ciphertext,
            LibSignalInterop.CiphertextTypeToEnvelopeType(SignalCiphertextMessageType.Whisper));
        Assert.Equal(
            SignalEnvelopeType.PreKeyBundle,
            LibSignalInterop.CiphertextTypeToEnvelopeType(SignalCiphertextMessageType.PreKey));
        Assert.Equal(
            SignalCiphertextMessageType.Whisper,
            LibSignalInterop.EnvelopeTypeToCiphertextType(SignalEnvelopeType.Ciphertext));
    }
}
