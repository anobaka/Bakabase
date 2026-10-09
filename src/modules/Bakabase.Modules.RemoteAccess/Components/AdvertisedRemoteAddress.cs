using System.Net;
using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Bakabase.Modules.RemoteAccess.Components;

/// <summary>A shareable HTTP(S) origin; validation never performs DNS or network requests.</summary>
public static class AdvertisedRemoteAddress
{
    private static readonly Regex Origin = new(@"\Ahttps?://[^/?#\\\s]+/?\z",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    public static bool TryNormalize(string? input, out string? normalized)
    {
        normalized = null;
        if (input is not {Length: > 0 and <= 2048} || input.Any(char.IsControl)) return false;
        var text = input.Trim();
        if (!Origin.IsMatch(text) || RemoteAddressInput.Parse(text, out var root) != RemoteAddressProblem.None ||
            root == null || root.Port <= 0 || root.IsLoopback) return false;
        var host = root.IdnHost.TrimEnd('.');
        if (host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("host.docker.internal", StringComparison.OrdinalIgnoreCase) ||
            host.Equals("gateway.docker.internal", StringComparison.OrdinalIgnoreCase) || host.Contains('%')) return false;
        if (IPAddress.TryParse(host.Trim('[', ']'), out var address))
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any) ||
                address.IsIPv6LinkLocal || address.IsIPv6Multicast) return false;
            if (address.AddressFamily == AddressFamily.InterNetwork)
            {
                var bytes = address.GetAddressBytes();
                if (bytes[0] == 0 || bytes[0] >= 224 || bytes[0] == 169 && bytes[1] == 254 ||
                    bytes[0] == 198 && bytes[1] is 18 or 19) return false;
            }
        }
        normalized = root.GetLeftPart(UriPartial.Authority);
        return true;
    }
}
