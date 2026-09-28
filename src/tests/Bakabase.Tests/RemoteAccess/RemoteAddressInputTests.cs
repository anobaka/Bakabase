using System;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// How an address typed for another device is read — the one reading server switching and
/// library sharing both use.
/// </summary>
[TestClass]
public class RemoteAddressInputTests
{
    [TestMethod]
    [DataRow("192.168.1.5:34567", "http://192.168.1.5:34567")]
    // A Chinese input method left in full-width mode.
    [DataRow("192.168.1.5：34567", "http://192.168.1.5:34567")]
    [DataRow("１９２．１６８．１．５：３４５６７", "http://192.168.1.5:34567")]
    [DataRow("192。168。1。5:34567", "http://192.168.1.5:34567")]
    [DataRow("\u3000192.168.1.5:34567\u3000", "http://192.168.1.5:34567")]
    // How Windows names another computer.
    [DataRow(@"\\PC1:34567", "http://PC1:34567")]
    [DataRow(@"\\PC1:34567\", "http://PC1:34567")]
    [DataRow("https://bakabase.example.com/", "https://bakabase.example.com")]
    public void What_was_meant_is_what_is_used(string typed, string expected)
    {
        Assert.AreEqual(expected, RemoteAddressInput.Normalize(typed));
        Assert.AreEqual(RemoteAddressProblem.None, RemoteAddressInput.Parse(typed, out var root));
        Assert.AreEqual(new Uri(expected), root);
    }

    [TestMethod]
    [DataRow("192.168.1.5")]
    [DataRow("PC1")]
    [DataRow(@"\\PC1")]
    [DataRow("PC1:")]
    [DataRow("[fe80::1]")]
    [DataRow("１９２．１６８．１．５")]
    public void A_host_without_a_port_is_not_guessed_at(string typed)
    {
        // The desktop app's port is 34567 only when it was free at launch, and a Docker
        // server's is whatever it was given; HTTP's own, 80, is where nothing listens.
        Assert.AreEqual(RemoteAddressProblem.PortMissing, RemoteAddressInput.Parse(typed, out var root));
        Assert.IsNull(root);
    }

    [TestMethod]
    [DataRow("http://nas", 80)]
    [DataRow("https://bakabase.example.com", 443)]
    [DataRow("[fe80::1]:34567", 34567)]
    public void A_scheme_or_a_port_says_where_to_go(string typed, int port)
    {
        // A URL keeps its scheme's port, so a server behind a reverse proxy still works.
        Assert.AreEqual(RemoteAddressProblem.None, RemoteAddressInput.Parse(typed, out var root));
        Assert.AreEqual(port, root!.Port);
    }

    [TestMethod]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("ftp://192.168.1.5:34567")]
    [DataRow("192.168.1.5:34567/library")]
    [DataRow("http://192.168.1.5:34567/#/dashboard")]
    [DataRow("192.168.1.5:34567?x=1")]
    [DataRow("user:secret@192.168.1.5:34567")]
    [DataRow("192.168.1.5:99999")]
    [DataRow("PC 1:34567")]
    [DataRow(@"\\PC1\share")]
    [DataRow("-pc-:34567")]
    // A colon no input method maps back to ':' — .NET takes it for part of an international
    // host name and would only throw once a request is sent.
    [DataRow("192.168.1.5\uFE5534567")]
    public void What_is_not_a_host_and_port_is_refused_as_such(string typed)
    {
        Assert.AreEqual(RemoteAddressProblem.Invalid, RemoteAddressInput.Parse(typed, out var root));
        Assert.IsNull(root);
    }
}
