using CPF.Drawing;
using Net.Codecrete.QrCodeGenerator;

namespace SignalCpf.UI.Helpers;

/// <summary>
/// Builds CPF images from provisioning URLs (Signal-compatible sgnl://linkdevice…).
/// </summary>
public static class QrCodeBitmapFactory
{
    public static Image? TryCreate(string? url, int scale = 8, int border = 2)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        // High ECC so a center logo cutout remains scannable.
        var qr = QrCode.EncodeText(url, QrCode.Ecc.High);
        var bmpBytes = qr.ToBmpBitmap(scale, border);
        return Image.FromBuffer(bmpBytes);
    }
}
