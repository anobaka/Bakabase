using System.Net;
using System.Text;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery;

/// <summary>
/// The subset of DNS wire format that answering "who serves
/// <c>_bakabase._tcp.local</c>?" needs — response building and question
/// parsing, nothing else. A full DNS library would be overkill for one service
/// type, and this way the packet bytes are unit-testable.
/// </summary>
public static class MdnsMessage
{
    public const ushort TypeA = 1;
    public const ushort TypePtr = 12;
    public const ushort TypeTxt = 16;
    public const ushort TypeSrv = 33;
    public const ushort TypeAny = 255;

    private const ushort ClassIn = 1;

    /// <summary>
    /// mDNS "cache-flush" bit: set on records only this host can answer for
    /// (SRV/TXT/A), clear on shared ones (PTR).
    /// </summary>
    private const ushort CacheFlush = 0x8000;

    public record Record(string Name, ushort Type, bool CacheFlush, uint Ttl, byte[] Rdata);

    /// <summary>Builds an authoritative mDNS response carrying the given answers.</summary>
    public static byte[] BuildResponse(IReadOnlyList<Record> answers)
    {
        var bytes = new List<byte>(256)
        {
            0, 0, // ID is always 0 in multicast responses
            0x84, 0, // QR=1 (response), AA=1
            0, 0, // QDCOUNT
            (byte) (answers.Count >> 8), (byte) answers.Count, // ANCOUNT
            0, 0, // NSCOUNT
            0, 0, // ARCOUNT
        };

        foreach (var answer in answers)
        {
            WriteName(bytes, answer.Name);
            WriteUInt16(bytes, answer.Type);
            WriteUInt16(bytes, (ushort) (ClassIn | (answer.CacheFlush ? CacheFlush : 0)));
            WriteUInt32(bytes, answer.Ttl);
            WriteUInt16(bytes, (ushort) answer.Rdata.Length);
            bytes.AddRange(answer.Rdata);
        }

        return bytes.ToArray();
    }

    public static byte[] PtrRdata(string target)
    {
        var bytes = new List<byte>();
        WriteName(bytes, target);
        return bytes.ToArray();
    }

    public static byte[] SrvRdata(ushort port, string target)
    {
        var bytes = new List<byte>();
        WriteUInt16(bytes, 0); // priority
        WriteUInt16(bytes, 0); // weight
        WriteUInt16(bytes, port);
        WriteName(bytes, target);
        return bytes.ToArray();
    }

    public static byte[] TxtRdata(IEnumerable<string> entries)
    {
        var bytes = new List<byte>();
        foreach (var entry in entries)
        {
            var data = Encoding.UTF8.GetBytes(entry);
            var length = Math.Min(data.Length, 255);
            bytes.Add((byte) length);
            bytes.AddRange(data.Take(length));
        }

        // An empty TXT record still needs one (empty) string to be well-formed.
        if (bytes.Count == 0)
        {
            bytes.Add(0);
        }

        return bytes.ToArray();
    }

    public static byte[] ARdata(IPAddress address) => address.GetAddressBytes();

    /// <summary>
    /// Pulls the questions out of a datagram. False for anything that is not a
    /// well-formed query — including responses, which arrive on the same socket.
    /// </summary>
    public static bool TryParseQuestions(ReadOnlySpan<byte> data, out List<(string Name, ushort Type)> questions)
    {
        questions = [];

        if (data.Length < 12)
        {
            return false;
        }

        var flags = (data[2] << 8) | data[3];
        if ((flags & 0x8000) != 0) // QR=1: a response, not a query
        {
            return false;
        }

        var questionCount = (data[4] << 8) | data[5];
        var offset = 12;

        for (var i = 0; i < questionCount; i++)
        {
            if (!TryReadName(data, ref offset, out var name))
            {
                return false;
            }

            if (offset + 4 > data.Length)
            {
                return false;
            }

            var type = (ushort) ((data[offset] << 8) | data[offset + 1]);
            offset += 4; // type + class

            questions.Add((name, type));
        }

        return questions.Count > 0;
    }

    /// <summary>Case- and trailing-dot-insensitive, as DNS names are.</summary>
    public static bool NamesEqual(string a, string b) =>
        string.Equals(a.TrimEnd('.'), b.TrimEnd('.'), StringComparison.OrdinalIgnoreCase);

    private static void WriteName(List<byte> bytes, string fqdn)
    {
        foreach (var label in fqdn.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var data = Encoding.UTF8.GetBytes(label);
            var length = Math.Min(data.Length, 63);
            bytes.Add((byte) length);
            bytes.AddRange(data.Take(length));
        }

        bytes.Add(0);
    }

    private static void WriteUInt16(List<byte> bytes, ushort value)
    {
        bytes.Add((byte) (value >> 8));
        bytes.Add((byte) value);
    }

    private static void WriteUInt32(List<byte> bytes, uint value)
    {
        bytes.Add((byte) (value >> 24));
        bytes.Add((byte) (value >> 16));
        bytes.Add((byte) (value >> 8));
        bytes.Add((byte) value);
    }

    private static bool TryReadName(ReadOnlySpan<byte> data, ref int offset, out string name)
    {
        name = string.Empty;
        var labels = new List<string>();
        var pos = offset;
        var jumped = false;
        var jumps = 0;
        var afterPointer = 0;

        while (true)
        {
            if (pos >= data.Length)
            {
                return false;
            }

            int length = data[pos];

            if (length == 0)
            {
                pos++;
                break;
            }

            if ((length & 0xC0) == 0xC0) // compression pointer
            {
                if (pos + 1 >= data.Length || ++jumps > 16)
                {
                    return false;
                }

                if (!jumped)
                {
                    afterPointer = pos + 2;
                    jumped = true;
                }

                pos = ((length & 0x3F) << 8) | data[pos + 1];
                continue;
            }

            if ((length & 0xC0) != 0) // reserved label types
            {
                return false;
            }

            if (pos + 1 + length > data.Length)
            {
                return false;
            }

            labels.Add(Encoding.UTF8.GetString(data.Slice(pos + 1, length)));
            pos += 1 + length;
        }

        offset = jumped ? afterPointer : pos;
        name = string.Join('.', labels) + ".";
        return true;
    }
}
