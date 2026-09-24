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

        /// <summary>
        /// Optional, added later: what kind of install it is and what it runs on, as words
        /// (<see cref="ServerSelfDescriptionWords"/>). Older servers leave them out; older
        /// readers skip keys they do not know.
        /// </summary>
        public const string Kind = "kind";
        public const string Platform = "os";
    }

    public static bool IsProbeRequest(ReadOnlySpan<byte> datagram)
    {
        var text = Encoding.UTF8.GetString(datagram).Trim();
        return text == RemoteAccessProtocol.ProbeRequest;
    }

    public static byte[] BuildProbeResponse(RemoteAccessServerDescriptor descriptor)
    {
        var facts = new Dictionary<string, object?>
        {
            [Keys.Id] = descriptor.Id,
            [Keys.Name] = descriptor.Name,
            [Keys.Port] = descriptor.Port,
            [Keys.AppVersion] = descriptor.AppVersion,
            [Keys.ProtocolVersion] = descriptor.ProtocolVersion,
        };

        foreach (var (key, word) in DescriptionOf(descriptor))
        {
            facts[key] = word;
        }

        var json = JsonSerializer.Serialize(facts);

        return Encoding.UTF8.GetBytes(RemoteAccessProtocol.ProbeResponsePrefix + json);
    }

    /// <summary>
    /// Reads a reply back into the facts it carries.
    /// </summary>
    /// <remarks>
    /// The other half of <see cref="BuildProbeResponse"/>, and the C# counterpart of
    /// the mobile app's <c>DiscoveryCodec.tryParseProbeResponse</c>. Everything on the
    /// wire came from whatever answered a broadcast, so nothing here throws: a
    /// malformed datagram is one more thing on the network that is not us.
    /// </remarks>
    /// <returns>Null when the datagram is not one of ours.</returns>
    public static RemoteAccessServerDescriptor? TryParseProbeResponse(ReadOnlySpan<byte> datagram)
    {
        string text;

        try
        {
            text = Encoding.UTF8.GetString(datagram);
        }
        catch (ArgumentException)
        {
            return null;
        }

        if (!text.StartsWith(RemoteAccessProtocol.ProbeResponsePrefix, StringComparison.Ordinal))
        {
            return null;
        }

        try
        {
            using var document =
                JsonDocument.Parse(text[RemoteAccessProtocol.ProbeResponsePrefix.Length..]);

            return FromFacts(
                key => document.RootElement.ValueKind == JsonValueKind.Object &&
                       document.RootElement.TryGetProperty(key, out var value)
                    ? value.ValueKind switch
                    {
                        JsonValueKind.String => value.GetString(),
                        JsonValueKind.Number => value.GetRawText(),
                        _ => null
                    }
                    : null);
        }
        catch (JsonException)
        {
            return null;
        }
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
            .. DescriptionOf(descriptor).Select(fact => $"{fact.Key}={fact.Word}"),
        ];
    }

    /// <summary>The optional facts that are known, and only those: absent ones take no room.</summary>
    private static IEnumerable<(string Key, string Word)> DescriptionOf(RemoteAccessServerDescriptor descriptor)
    {
        if (ServerSelfDescriptionWords.Of(descriptor.Kind) is { } kind)
        {
            yield return (Keys.Kind, kind);
        }

        if (ServerSelfDescriptionWords.Of(descriptor.Platform) is { } platform)
        {
            yield return (Keys.Platform, platform);
        }
    }

    /// <summary>
    /// Reads TXT entries back, in the <c>key=value</c> shape
    /// <see cref="BuildTxtEntries"/> writes.
    /// </summary>
    public static RemoteAccessServerDescriptor? TryParseTxtEntries(IEnumerable<string> entries)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);

        foreach (var entry in entries)
        {
            var separator = entry.IndexOf('=');

            if (separator > 0)
            {
                facts[entry[..separator]] = entry[(separator + 1)..];
            }
        }

        return FromFacts(key => facts.GetValueOrDefault(key));
    }

    /// <summary>
    /// The one place the two channels turn their raw key/value facts into a
    /// descriptor, so they cannot disagree about which fields are required.
    /// </summary>
    /// <remarks>
    /// An id is required — it is what tells two servers apart and what a paired
    /// device is paired to. A port is required because the address is useless
    /// without one. A name and a version are not: a server that reports neither is
    /// still reachable, and refusing to list it would hide a working server over
    /// cosmetics.
    /// </remarks>
    private static RemoteAccessServerDescriptor? FromFacts(Func<string, string?> fact)
    {
        var id = fact(Keys.Id);

        if (string.IsNullOrWhiteSpace(id) ||
            !int.TryParse(fact(Keys.Port), out var port) || port is <= 0 or > 65535)
        {
            return null;
        }

        _ = int.TryParse(fact(Keys.ProtocolVersion), out var protocolVersion);

        return new RemoteAccessServerDescriptor(id, fact(Keys.Name) ?? id, port,
            fact(Keys.AppVersion) ?? string.Empty, protocolVersion,
            // Whatever answered a broadcast: a word this build does not know is nothing.
            ServerSelfDescriptionWords.KindOf(fact(Keys.Kind)),
            ServerSelfDescriptionWords.PlatformOf(fact(Keys.Platform)));
    }
}
