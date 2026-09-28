using Bakabase.Modules.Federation.Security;
using Bakabase.Modules.Federation.Transport;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Transport;

/// <summary>
/// The address a person types under "Connect another device": read as server switching reads
/// it, and refused with a code the page can explain — never a 500.
/// </summary>
[TestClass]
public sealed class FederationAddressTests
{
    [TestMethod]
    [DataRow("192.168.1.5:34567", "http://192.168.1.5:34567")]
    [DataRow("192.168.1.5：34567", "http://192.168.1.5:34567")]
    [DataRow("１９２．１６８．１．５：３４５６７/", "http://192.168.1.5:34567")]
    [DataRow(@"\\PC1:34567", "http://pc1:34567")]
    [DataRow("https://bakabase.example.com", "https://bakabase.example.com")]
    public void A_typed_address_becomes_the_origin_it_means(string typed, string expected)
    {
        Assert.AreEqual(expected, FederationHttpClient.NormalizeAddress(typed));
        using var request = FederationHttpClient.CreateRequest(typed, HttpMethod.Get, "/federation/v1/info", null);
        Assert.AreEqual($"{expected}/federation/v1/info", request.RequestUri!.AbsoluteUri);
    }

    [TestMethod]
    [DataRow("192.168.1.5")]
    [DataRow(@"\\PC1")]
    public void A_host_without_its_port_is_asked_for_the_port(string typed)
    {
        var refused = Assert.ThrowsException<FederationAccessException>(() => FederationHttpClient.NormalizeAddress(typed));
        Assert.AreEqual("PortMissing", refused.ErrorCode);
        Assert.AreEqual(400, refused.StatusCode);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("192.168.1.5:34567/library")]
    [DataRow("192.168.1.5﹕34567")]
    public void What_is_not_a_host_and_port_is_an_invalid_address(string typed)
    {
        // The last one is a colon .NET takes for part of an international host name: it used to
        // pass here and throw UriFormatException when the request was sent, a 500.
        var refused = Assert.ThrowsException<FederationAccessException>(() =>
            FederationHttpClient.CreateRequest(typed, HttpMethod.Get, "/federation/v1/info", null));
        Assert.AreEqual("InvalidAddress", refused.ErrorCode);
        Assert.AreEqual(400, refused.StatusCode);
    }
}
