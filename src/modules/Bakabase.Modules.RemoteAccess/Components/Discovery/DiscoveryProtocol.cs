using System.Text;
using System.Text.Json;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery;

/// <summary>
/// The wire format of the UDP probe channel and the TXT payload of the mDNS
/// channel, kept as pure functions so the contract is testable without sockets.
/// Both channels carry the same <see cref="RemoteAccessServerDescriptor"/> facts.
/// </summary>
public static class DiscoveryProtocol
{
    /// <summary>
    /// Stable JSON keys — short because the mDNS TXT record mirrors them and TXT
    /// space is tight. These are protocol, not style; renaming breaks clients.
    /// </summary>
    private static class Keys
    {
        public const string Id = "id";
        public const string Name = "name";
        public const string Port = "port";
        public const string AppVersion = "ver";
        public const string ProtocolVersion = "proto";
    }

    public static bool IsProbeRequest(ReadOnlySpan<byte> datagram)
    {
        var text = Encoding.UTF8.GetString(datagram).Trim();
        return text == RemoteAccessProtocol.ProbeRequest;
    }

    public static byte[] BuildProbeResponse(RemoteAccessServerDescriptor descriptor)
    {
        var json = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            [Keys.Id] = descriptor.Id,
            [Keys.Name] = descriptor.Name,
            [Keys.Port] = descriptor.Port,
            [Keys.AppVersion] = descriptor.AppVersion,
            [Keys.ProtocolVersion] = descriptor.ProtocolVersion,
        });

        return Encoding.UTF8.GetBytes(RemoteAccessProtocol.ProbeResponsePrefix + json);
    }

    /// <summary>
    /// TXT entries for the mDNS advertisement, one <c>key=value</c> per record
    /// string, same keys as the probe JSON.
    /// </summary>
    public static IReadOnlyList<string> BuildTxtEntries(RemoteAccessServerDescriptor descriptor)
    {
        return
        [
            $"{Keys.Id}={descriptor.Id}",
            $"{Keys.Name}={descriptor.Name}",
            $"{Keys.Port}={descriptor.Port?.ToString() ?? string.Empty}",
            $"{Keys.AppVersion}={descriptor.AppVersion}",
            $"{Keys.ProtocolVersion}={descriptor.ProtocolVersion}",
        ];
    }
}
