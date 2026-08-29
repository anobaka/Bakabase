using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public class DiscoveryProtocolTests
{
    private static readonly RemoteAccessServerDescriptor Descriptor =
        new("abc123", "My-PC", 34567, "2.4.0-beta", 1);

    [TestMethod]
    public void ProbeRequest_MatchesExactly_IgnoringWhitespace()
    {
        Assert.IsTrue(DiscoveryProtocol.IsProbeRequest(Encoding.UTF8.GetBytes("BAKABASE_DISCOVER_V1")));
        Assert.IsTrue(DiscoveryProtocol.IsProbeRequest(Encoding.UTF8.GetBytes("BAKABASE_DISCOVER_V1\n")));

        Assert.IsFalse(DiscoveryProtocol.IsProbeRequest(Encoding.UTF8.GetBytes("BAKABASE_DISCOVER_V2")));
        Assert.IsFalse(DiscoveryProtocol.IsProbeRequest(Encoding.UTF8.GetBytes("")));
        Assert.IsFalse(DiscoveryProtocol.IsProbeRequest(Encoding.UTF8.GetBytes("GET / HTTP/1.1")));
    }

    [TestMethod]
    public void ProbeResponse_IsPrefixedJson_WithTheAgreedKeys()
    {
        var response = Encoding.UTF8.GetString(DiscoveryProtocol.BuildProbeResponse(Descriptor));

        Assert.IsTrue(response.StartsWith(RemoteAccessProtocol.ProbeResponsePrefix), response);

        var json = JsonDocument.Parse(response[RemoteAccessProtocol.ProbeResponsePrefix.Length..]);
        Assert.AreEqual("abc123", json.RootElement.GetProperty("id").GetString());
        Assert.AreEqual("My-PC", json.RootElement.GetProperty("name").GetString());
        Assert.AreEqual(34567, json.RootElement.GetProperty("port").GetInt32());
        Assert.AreEqual("2.4.0-beta", json.RootElement.GetProperty("ver").GetString());
        Assert.AreEqual(1, json.RootElement.GetProperty("proto").GetInt32());
    }

    [TestMethod]
    public void TxtEntries_MirrorTheProbeJsonKeys()
    {
        var entries = DiscoveryProtocol.BuildTxtEntries(Descriptor);

        CollectionAssert.Contains(entries.ToList(), "id=abc123");
        CollectionAssert.Contains(entries.ToList(), "name=My-PC");
        CollectionAssert.Contains(entries.ToList(), "port=34567");
        CollectionAssert.Contains(entries.ToList(), "ver=2.4.0-beta");
        CollectionAssert.Contains(entries.ToList(), "proto=1");
    }
}

[TestClass]
public class MdnsMessageTests
{
    [TestMethod]
    public void Questions_AreParsed_FromAPlainQuery()
    {
        var query = BuildQuery(("_bakabase._tcp.local.", MdnsMessage.TypePtr));

        Assert.IsTrue(MdnsMessage.TryParseQuestions(query, out var questions));
        Assert.AreEqual(1, questions.Count);
        Assert.AreEqual("_bakabase._tcp.local.", questions[0].Name);
        Assert.AreEqual(MdnsMessage.TypePtr, questions[0].Type);
    }

    [TestMethod]
    public void Questions_FollowCompressionPointers()
    {
        // Two questions where the second's name is a pointer back to the first's.
        var bytes = new List<byte> {0, 0, 0, 0, 0, 2, 0, 0, 0, 0, 0, 0};
        var nameOffset = bytes.Count;
        WriteName(bytes, "_bakabase._tcp.local.");
        bytes.AddRange([0, (byte) MdnsMessage.TypePtr, 0, 1]);
        bytes.AddRange([(byte) (0xC0 | (nameOffset >> 8)), (byte) nameOffset]); // pointer
        bytes.AddRange([0, (byte) MdnsMessage.TypeTxt, 0, 1]);

        Assert.IsTrue(MdnsMessage.TryParseQuestions(bytes.ToArray(), out var questions));
        Assert.AreEqual(2, questions.Count);
        Assert.AreEqual("_bakabase._tcp.local.", questions[1].Name);
        Assert.AreEqual(MdnsMessage.TypeTxt, questions[1].Type);
    }

    [TestMethod]
    public void Responses_AreNotParsedAsQueries()
    {
        var response = MdnsMessage.BuildResponse([
            new MdnsMessage.Record("x.local.", MdnsMessage.TypeA, true, 120, MdnsMessage.ARdata(IPAddress.Parse("192.168.1.5")))
        ]);

        Assert.IsFalse(MdnsMessage.TryParseQuestions(response, out _));
    }

    [TestMethod]
    public void Garbage_DoesNotParse()
    {
        Assert.IsFalse(MdnsMessage.TryParseQuestions(Array.Empty<byte>(), out _));
        Assert.IsFalse(MdnsMessage.TryParseQuestions(new byte[] {1, 2, 3}, out _));
        // A pointer loop must not hang or throw.
        var loop = new List<byte> {0, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0xC0, 12, 0, 12, 0, 1};
        Assert.IsFalse(MdnsMessage.TryParseQuestions(loop.ToArray(), out _));
    }

    [TestMethod]
    public void Response_CarriesHeaderAndAnswerCount()
    {
        var response = MdnsMessage.BuildResponse([
            new MdnsMessage.Record("a.local.", MdnsMessage.TypeA, true, 120, new byte[] {1, 2, 3, 4}),
            new MdnsMessage.Record("b.local.", MdnsMessage.TypeA, false, 0, new byte[] {5, 6, 7, 8}),
        ]);

        Assert.AreEqual(0x84, response[2]); // QR=1, AA=1
        Assert.AreEqual(0, response[4]); // QDCOUNT
        Assert.AreEqual(0, response[5]);
        Assert.AreEqual(0, response[6]); // ANCOUNT
        Assert.AreEqual(2, response[7]);
    }

    [TestMethod]
    public void TxtRdata_LengthPrefixesEachEntry()
    {
        var rdata = MdnsMessage.TxtRdata(["id=1", "name=x"]);

        Assert.AreEqual(4, rdata[0]);
        Assert.AreEqual("id=1", Encoding.UTF8.GetString(rdata, 1, 4));
        Assert.AreEqual(6, rdata[5]);
        Assert.AreEqual("name=x", Encoding.UTF8.GetString(rdata, 6, 6));
    }

    [TestMethod]
    public void SrvRdata_EncodesThePort()
    {
        var rdata = MdnsMessage.SrvRdata(34567, "host.local.");

        // priority(2) + weight(2), then the port big-endian.
        Assert.AreEqual(34567, (rdata[4] << 8) | rdata[5]);
    }

    [TestMethod]
    public void NamesEqual_IgnoresCaseAndTrailingDot()
    {
        Assert.IsTrue(MdnsMessage.NamesEqual("_Bakabase._TCP.local", "_bakabase._tcp.local."));
        Assert.IsFalse(MdnsMessage.NamesEqual("_bakabase._udp.local.", "_bakabase._tcp.local."));
    }

    internal static byte[] BuildQuery(params (string Name, ushort Type)[] questions)
    {
        var bytes = new List<byte>
        {
            0, 0, 0, 0,
            (byte) (questions.Length >> 8), (byte) questions.Length,
            0, 0, 0, 0, 0, 0,
        };

        foreach (var (name, type) in questions)
        {
            WriteName(bytes, name);
            bytes.AddRange([(byte) (type >> 8), (byte) type, 0, 1]);
        }

        return bytes.ToArray();
    }

    private static void WriteName(List<byte> bytes, string fqdn)
    {
        foreach (var label in fqdn.Split('.', StringSplitOptions.RemoveEmptyEntries))
        {
            var data = Encoding.UTF8.GetBytes(label);
            bytes.Add((byte) data.Length);
            bytes.AddRange(data);
        }

        bytes.Add(0);
    }
}

[TestClass]
public class MdnsAdvertisementTests
{
    private static readonly RemoteAccessServerDescriptor Descriptor =
        new("abc123", "My.PC", 34567, "2.4.0-beta", 1);

    private static readonly IPAddress[] Addresses =
        [IPAddress.Parse("192.168.1.5"), IPAddress.Parse("10.0.0.2")];

    [TestMethod]
    public void InstanceLabel_HasNoInnerDots()
    {
        var advertisement = new MdnsAdvertisement(Descriptor);

        // "My.PC" would otherwise split into two labels and change the name.
        Assert.IsTrue(advertisement.InstanceName.StartsWith("My-PC._bakabase._tcp.local."),
            advertisement.InstanceName);
    }

    [TestMethod]
    public void HostName_IsDistinctFromTheMachinesOwnLocalName()
    {
        var advertisement = new MdnsAdvertisement(Descriptor);

        Assert.AreEqual("my-pc-bakabase.local.", advertisement.HostName);
    }

    [TestMethod]
    public void Advertising_WithoutAPort_IsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            _ = new MdnsAdvertisement(Descriptor with {Port = null}));
    }

    [TestMethod]
    public void RecordSet_CoversPtrSrvTxtAndEveryAddress()
    {
        var advertisement = new MdnsAdvertisement(Descriptor);
        var records = advertisement.BuildRecords(Addresses);

        Assert.AreEqual(1, records.Count(r => r.Type == MdnsMessage.TypeSrv));
        Assert.AreEqual(1, records.Count(r => r.Type == MdnsMessage.TypeTxt));
        Assert.AreEqual(2, records.Count(r => r.Type == MdnsMessage.TypePtr)); // service + meta enumeration
        Assert.AreEqual(Addresses.Length, records.Count(r => r.Type == MdnsMessage.TypeA));
        Assert.IsTrue(records.All(r => r.Ttl > 0));

        var srv = records.Single(r => r.Type == MdnsMessage.TypeSrv);
        Assert.AreEqual(34567, (srv.Rdata[4] << 8) | srv.Rdata[5]);
    }

    [TestMethod]
    public void Goodbye_SetsEveryTtlToZero()
    {
        var records = new MdnsAdvertisement(Descriptor).BuildRecords(Addresses, goodbye: true);

        Assert.IsTrue(records.All(r => r.Ttl == 0));
    }

    [TestMethod]
    public void Answers_ServiceInstanceHostAndMetaQuestions_ButNothingElse()
    {
        var advertisement = new MdnsAdvertisement(Descriptor);

        Assert.IsTrue(advertisement.Answers([("_bakabase._tcp.local.", MdnsMessage.TypePtr)]));
        Assert.IsTrue(advertisement.Answers([("_BAKABASE._TCP.LOCAL", MdnsMessage.TypeAny)]));
        Assert.IsTrue(advertisement.Answers([("_services._dns-sd._udp.local.", MdnsMessage.TypePtr)]));
        Assert.IsTrue(advertisement.Answers([(advertisement.InstanceName, MdnsMessage.TypeSrv)]));
        Assert.IsTrue(advertisement.Answers([(advertisement.HostName, MdnsMessage.TypeA)]));

        Assert.IsFalse(advertisement.Answers([("_googlecast._tcp.local.", MdnsMessage.TypePtr)]));
        Assert.IsFalse(advertisement.Answers([("_bakabase._tcp.local.", MdnsMessage.TypeA)]));
        Assert.IsFalse(advertisement.Answers([]));
    }
}
