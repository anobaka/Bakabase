using System.Net;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery;

/// <summary>
/// The DNS-SD record set this server advertises: PTR (service → instance),
/// SRV (instance → host:port), TXT (instance facts) and A (host → addresses).
/// Pure record building; the responder owns the sockets.
/// </summary>
public class MdnsAdvertisement
{
    private const uint RecordTtlSeconds = 120;

    /// <summary>
    /// The meta-service every DNS-SD browser may enumerate ("what service types
    /// exist here at all?").
    /// </summary>
    private const string ServiceEnumerationName = "_services._dns-sd._udp.local.";

    private readonly RemoteAccessServerDescriptor _descriptor;
    private readonly int _port;

    public string InstanceName { get; }

    /// <summary>
    /// Hostname we answer A records for. Deliberately not the machine's own
    /// <c>{name}.local</c> — the OS's resolver already claims that one, and two
    /// responders answering the same name with different addresses would fight.
    /// </summary>
    public string HostName { get; }

    public MdnsAdvertisement(RemoteAccessServerDescriptor descriptor)
    {
        if (descriptor.Port == null)
        {
            throw new ArgumentException("Cannot advertise before a listening port is known", nameof(descriptor));
        }

        _descriptor = descriptor;
        _port = descriptor.Port.Value;

        var label = SanitizeLabel(descriptor.Name);
        InstanceName = $"{label}.{RemoteAccessProtocol.MdnsServiceType}";
        HostName = $"{label.ToLowerInvariant()}-bakabase.local.";
    }

    /// <summary>
    /// True when a question asks about anything in this record set, i.e. a
    /// response is worth sending at all.
    /// </summary>
    public bool Answers(IEnumerable<(string Name, ushort Type)> questions)
    {
        foreach (var (name, type) in questions)
        {
            if (MdnsMessage.NamesEqual(name, RemoteAccessProtocol.MdnsServiceType) &&
                type is MdnsMessage.TypePtr or MdnsMessage.TypeAny)
            {
                return true;
            }

            if (MdnsMessage.NamesEqual(name, ServiceEnumerationName) &&
                type is MdnsMessage.TypePtr or MdnsMessage.TypeAny)
            {
                return true;
            }

            if (MdnsMessage.NamesEqual(name, InstanceName) &&
                type is MdnsMessage.TypeSrv or MdnsMessage.TypeTxt or MdnsMessage.TypeAny)
            {
                return true;
            }

            if (MdnsMessage.NamesEqual(name, HostName) &&
                type is MdnsMessage.TypeA or MdnsMessage.TypeAny)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// The full record set. Always sent whole: a browser that asked the PTR
    /// question needs SRV/TXT/A next anyway, and mDNS encourages bundling them.
    /// </summary>
    /// <param name="addresses">Local IPv4 addresses to publish as A records.</param>
    /// <param name="goodbye">True to build the leaving announcement (TTL 0).</param>
    public IReadOnlyList<MdnsMessage.Record> BuildRecords(IReadOnlyList<IPAddress> addresses, bool goodbye = false)
    {
        var ttl = goodbye ? 0u : RecordTtlSeconds;

        var records = new List<MdnsMessage.Record>
        {
            new(ServiceEnumerationName, MdnsMessage.TypePtr, false, ttl,
                MdnsMessage.PtrRdata(RemoteAccessProtocol.MdnsServiceType)),
            new(RemoteAccessProtocol.MdnsServiceType, MdnsMessage.TypePtr, false, ttl,
                MdnsMessage.PtrRdata(InstanceName)),
            new(InstanceName, MdnsMessage.TypeSrv, true, ttl,
                MdnsMessage.SrvRdata((ushort) _port, HostName)),
            new(InstanceName, MdnsMessage.TypeTxt, true, ttl,
                MdnsMessage.TxtRdata(DiscoveryProtocol.BuildTxtEntries(_descriptor))),
        };

        records.AddRange(addresses.Select(a =>
            new MdnsMessage.Record(HostName, MdnsMessage.TypeA, true, ttl, MdnsMessage.ARdata(a))));

        return records;
    }

    /// <summary>
    /// A DNS label: no inner dots (they would split it), no control characters,
    /// at most 63 bytes. Falls back to a constant rather than an empty label.
    /// </summary>
    private static string SanitizeLabel(string name)
    {
        var chars = name.Trim()
            .Select(c => c == '.' || char.IsControl(c) ? '-' : c)
            .ToArray();

        var label = new string(chars);
        return string.IsNullOrWhiteSpace(label) ? "Bakabase" : label;
    }
}
