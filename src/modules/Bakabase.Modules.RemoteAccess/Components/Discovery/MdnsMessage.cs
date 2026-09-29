using System.Net;
using System.Text;

namespace Bakabase.Modules.RemoteAccess.Components.Discovery;

/// <summary>
/// The subset of DNS wire format that both halves of "who serves
/// <c>_bakabase._tcp.local</c>?" need — building and parsing a query, and the
/// same for a response. A full DNS library would be overkill for one service
/// type, and this way the packet bytes are unit-testable.
/// </summary>
public static class MdnsMessage
{
    public const ushort TypeA = 1;
    public const ushort TypePtr = 12;
    public const ushort TypeTxt = 16;
    public const ushort TypeAaaa = 28;
    public const ushort TypeSrv = 33;
    public const ushort TypeAny = 255;

    private const ushort ClassIn = 1;

    /// <summary>
    /// mDNS "QU" bit, on a question's class: the querier takes a unicast answer (RFC 6762 §5.4).
    /// </summary>
    private const ushort UnicastResponse = 0x8000;

    /// <summary>
    /// mDNS "cache-flush" bit: set on records only this host can answer for
    /// (SRV/TXT/A), clear on shared ones (PTR).
    /// </summary>
    private const ushort CacheFlush = 0x8000;

    /// <summary>
    /// The longest TTL an answer sent straight back to a one-shot querier may carry
    /// (RFC 6762 §6.7): such a querier is not listening for the updates and goodbyes that keep a
    /// longer one right.
    /// </summary>
    public const uint LegacyUnicastMaxTtl = 10;

    public record Record(string Name, ushort Type, bool CacheFlush, uint Ttl, byte[] Rdata);

    /// <summary>One question as a query asked it, its class whole — the "QU" bit included.</summary>
    public readonly record struct Question(string Name, ushort Type, ushort Class);

    /// <summary>Builds an authoritative mDNS response carrying the given answers.</summary>
    public static byte[] BuildResponse(IReadOnlyList<Record> answers) => Build(0, [], answers, [], r => r);

    /// <summary>
    /// The answer to a one-shot query — one sent from a port other than 5353 — which goes
    /// straight back to its sender as a conventional unicast DNS response (RFC 6762 §6.7): the
    /// query's id and questions repeated, no cache-flush bits, TTLs at most
    /// <see cref="LegacyUnicastMaxTtl"/>.
    /// </summary>
    /// <param name="id">The query's transaction id.</param>
    /// <param name="questions">The query's questions, as asked.</param>
    /// <param name="answers">The records answering them.</param>
    /// <param name="additional">Records that go with them: those an answer points at.</param>
    public static byte[] BuildLegacyUnicastResponse(ushort id, IReadOnlyList<Question> questions,
        IReadOnlyList<Record> answers, IReadOnlyList<Record> additional) =>
        Build(id, questions, answers, additional, r => r with
        {
            CacheFlush = false,
            Ttl = Math.Min(r.Ttl, LegacyUnicastMaxTtl)
        });

    private static byte[] Build(ushort id, IReadOnlyList<Question> questions, IReadOnlyList<Record> answers,
        IReadOnlyList<Record> additional, Func<Record, Record> adjust)
    {
        var bytes = new List<byte>(256)
        {
            (byte) (id >> 8), (byte) id, // ID: 0 in multicast responses, the query's in a unicast one
            0x84, 0, // QR=1 (response), AA=1
            (byte) (questions.Count >> 8), (byte) questions.Count, // QDCOUNT
            (byte) (answers.Count >> 8), (byte) answers.Count, // ANCOUNT
            0, 0, // NSCOUNT
            (byte) (additional.Count >> 8), (byte) additional.Count, // ARCOUNT
        };

        foreach (var question in questions)
        {
            WriteName(bytes, question.Name);
            WriteUInt16(bytes, question.Type);
            WriteUInt16(bytes, question.Class);
        }

        foreach (var record in answers.Concat(additional).Select(adjust))
        {
            WriteName(bytes, record.Name);
            WriteUInt16(bytes, record.Type);
            WriteUInt16(bytes, (ushort) (ClassIn | (record.CacheFlush ? CacheFlush : 0)));
            WriteUInt32(bytes, record.Ttl);
            WriteUInt16(bytes, (ushort) record.Rdata.Length);
            bytes.AddRange(record.Rdata);
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
        var parsed = TryParseQuery(data, out _, out var asked);

        questions = asked.Select(q => (q.Name, q.Type)).ToList();
        return parsed;
    }

    /// <summary>
    /// A query's transaction id and its questions, as asked. False for anything that is not a
    /// well-formed query — including responses, which arrive on the same socket.
    /// </summary>
    public static bool TryParseQuery(ReadOnlySpan<byte> data, out ushort id, out List<Question> questions)
    {
        questions = [];
        id = 0;

        if (data.Length < 12)
        {
            return false;
        }

        var flags = (data[2] << 8) | data[3];
        if ((flags & 0x8000) != 0) // QR=1: a response, not a query
        {
            return false;
        }

        id = (ushort) ((data[0] << 8) | data[1]);

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
            var @class = (ushort) ((data[offset + 2] << 8) | data[offset + 3]);
            offset += 4; // type + class

            questions.Add(new Question(name, type, @class));
        }

        return questions.Count > 0;
    }

    /// <summary>
    /// One answer, with its payload already decoded.
    /// </summary>
    /// <remarks>
    /// Decoded during the parse rather than handed back as bytes, because a name
    /// inside SRV or PTR data may be a compression pointer into the rest of the
    /// datagram — so it can only be read while the whole packet is still in hand.
    /// </remarks>
    /// <param name="Ttl">Zero means the record is being withdrawn: a server saying goodbye.</param>
    /// <param name="Target">The name a PTR or SRV points at. Null for other types.</param>
    /// <param name="Port">SRV's port. Zero for other types.</param>
    /// <param name="Txt">TXT's strings, in order. Empty for other types.</param>
    /// <param name="Address">An A or AAAA record's address. Null for other types.</param>
    public record ParsedRecord(string Name, ushort Type, uint Ttl, string? Target = null, ushort Port = 0,
        IReadOnlyList<string>? Txt = null, IPAddress? Address = null)
    {
        public IReadOnlyList<string> Txt { get; init; } = Txt ?? [];
    }

    /// <summary>A query for one name and type, with the transaction id mDNS always leaves at zero.</summary>
    public static byte[] BuildQuery(string name, ushort type) => BuildQuery(name, type, 0, false);

    /// <param name="name">The name asked about.</param>
    /// <param name="type">The record type asked for.</param>
    /// <param name="id">
    /// The transaction id. Zero for a full mDNS querier; a one-shot query from a port other than
    /// 5353 carries its own, which the answer sent back to it repeats (RFC 6762 §6.7).
    /// </param>
    /// <param name="unicastResponse">
    /// Sets the question's "QU" bit, asking for the answer to come straight back rather than
    /// multicast. Left clear by browsing: the responders multicast their answers, and every
    /// other listener on the network benefits from seeing them.
    /// </param>
    public static byte[] BuildQuery(string name, ushort type, ushort id, bool unicastResponse)
    {
        var bytes = new List<byte>(64)
        {
            (byte) (id >> 8), (byte) id, // ID
            0, 0, // flags: a plain query
            0, 1, // QDCOUNT
            0, 0, // ANCOUNT
            0, 0, // NSCOUNT
            0, 0, // ARCOUNT
        };

        WriteName(bytes, name);
        WriteUInt16(bytes, type);
        WriteUInt16(bytes, (ushort) (ClassIn | (unicastResponse ? UnicastResponse : 0)));

        return bytes.ToArray();
    }

    /// <summary>
    /// Reads the answers out of a response. False for anything that is not one —
    /// including queries, which arrive on the same socket.
    /// </summary>
    /// <remarks>
    /// Everything on this socket came from whatever else is on the network, so
    /// nothing here throws: a packet that does not parse is one more thing that is
    /// not us. Additional and authority sections are read too, because responders
    /// routinely put the SRV/TXT/A a browser needs there rather than in answers.
    /// </remarks>
    public static bool TryParseResponse(ReadOnlySpan<byte> data, out List<ParsedRecord> records)
    {
        records = [];

        if (data.Length < 12)
        {
            return false;
        }

        var flags = (data[2] << 8) | data[3];

        if ((flags & 0x8000) == 0) // QR=0: a query, not a response
        {
            return false;
        }

        var questionCount = (data[4] << 8) | data[5];
        var recordCount = ((data[6] << 8) | data[7]) + ((data[8] << 8) | data[9]) +
                          ((data[10] << 8) | data[11]);
        var offset = 12;

        for (var i = 0; i < questionCount; i++)
        {
            if (!TryReadName(data, ref offset, out _) || offset + 4 > data.Length)
            {
                return false;
            }

            offset += 4; // type + class
        }

        for (var i = 0; i < recordCount; i++)
        {
            if (!TryReadName(data, ref offset, out var name) || offset + 10 > data.Length)
            {
                // A truncated tail still leaves whatever was read before it usable.
                break;
            }

            var type = (ushort) ((data[offset] << 8) | data[offset + 1]);
            var ttl = (uint) ((data[offset + 4] << 24) | (data[offset + 5] << 16) |
                              (data[offset + 6] << 8) | data[offset + 7]);
            var length = (data[offset + 8] << 8) | data[offset + 9];

            offset += 10;

            if (offset + length > data.Length)
            {
                break;
            }

            records.Add(ReadRecord(data, name, type, ttl, offset, length));
            offset += length;
        }

        return records.Count > 0;
    }

    private static ParsedRecord ReadRecord(ReadOnlySpan<byte> data, string name, ushort type, uint ttl, int offset,
        int length)
    {
        switch (type)
        {
            case TypePtr:
            {
                var position = offset;

                return new ParsedRecord(name, type, ttl,
                    TryReadName(data, ref position, out var target) ? target : null);
            }
            case TypeSrv when length >= 7:
            {
                var position = offset + 6;
                var port = (ushort) ((data[offset + 4] << 8) | data[offset + 5]);

                return new ParsedRecord(name, type, ttl,
                    TryReadName(data, ref position, out var target) ? target : null, port);
            }
            case TypeTxt:
                return new ParsedRecord(name, type, ttl, Txt: ReadTxt(data.Slice(offset, length)));
            case TypeA when length == 4:
                return new ParsedRecord(name, type, ttl, Address: new IPAddress(data.Slice(offset, 4).ToArray()));
            case TypeAaaa when length == 16:
                return new ParsedRecord(name, type, ttl, Address: new IPAddress(data.Slice(offset, 16).ToArray()));
            default:
                return new ParsedRecord(name, type, ttl);
        }
    }

    private static List<string> ReadTxt(ReadOnlySpan<byte> rdata)
    {
        var entries = new List<string>();
        var position = 0;

        while (position < rdata.Length)
        {
            int length = rdata[position];

            if (length == 0)
            {
                position++;
                continue;
            }

            if (position + 1 + length > rdata.Length)
            {
                break;
            }

            entries.Add(Encoding.UTF8.GetString(rdata.Slice(position + 1, length)));
            position += 1 + length;
        }

        return entries;
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
