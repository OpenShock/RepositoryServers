using System.Globalization;

namespace OpenShock.RepositoryServer.Utils;

/// <summary>
/// Parses USB vendor and product identifiers typed by a human.
/// </summary>
/// <remarks>
/// Accepts both <c>0x1A86</c> and <c>6790</c>. Vendor documentation and lsusb print hex, while some
/// tooling reports decimal, and silently reading one as the other would store a device that never
/// matches anything.
/// </remarks>
public static class UsbIdentifier
{
    /// <summary>
    /// Renders an identifier the way vendor documentation and lsusb print it, which is also the
    /// form <see cref="TryParse"/> reads back. Four digits always, because a device is documented as
    /// <c>0x1A86</c> and a truncated <c>0x1A8</c> would not be recognisable as the same number.
    /// </summary>
    public static string Format(int value) => $"0x{value:X4}";

    public static bool TryParse(string? raw, out int value)
    {
        value = 0;
        if (string.IsNullOrWhiteSpace(raw)) return false;

        var trimmed = raw.Trim();

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return int.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value)
                   && value is >= 0 and <= 0xFFFF;
        }

        return int.TryParse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
               && value is >= 0 and <= 0xFFFF;
    }
}
