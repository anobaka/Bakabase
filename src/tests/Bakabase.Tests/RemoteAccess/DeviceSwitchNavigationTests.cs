using Bakabase.Abstractions.Components.Gui;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public sealed class DeviceSwitchNavigationTests
{
    [TestMethod]
    [DataRow("http://localhost:34568/#/resource", "/resource")]
    [DataRow("https://localhost/#/resource/42?node=old&epoch=old#details", "/resource/42")]
    [DataRow("http://localhost/#/data-sync?kind=Property", "/data-sync")]
    [DataRow("http://localhost/#/federation/devices", "/federation/devices")]
    [DataRow("http://localhost/#/tools_2", "/tools_2")]
    [DataRow("http://localhost/#/", "/")]
    [DataRow("http://localhost/", "/")]
    [DataRow("http://localhost/#token=private", "/")]
    [DataRow("http://localhost/#//other-host/path", "/")]
    [DataRow("http://localhost/#/resource//42", "/")]
    [DataRow("http://localhost/#/resource/../configuration", "/")]
    [DataRow("http://localhost/#/resource%2F42", "/")]
    [DataRow("http://localhost/#/配置", "/")]
    [DataRow("http://localhost/#/device-switch?path=%2Fresource", "/")]
    [DataRow("http://localhost/#/device-switch/another", "/")]
    [DataRow("file:///page.html#/resource", "/")]
    [DataRow("not a URL", "/")]
    [DataRow(null, "/")]
    public void Handoff_keeps_only_a_bounded_plain_source_path(string? source, string expectedPath)
    {
        const string target = "http://127.0.0.1:4567/?__bakabase_switch=ticket%2Fvalue&other=kept#old";
        Assert.AreEqual("http://127.0.0.1:4567/?__bakabase_switch=ticket%2Fvalue&other=kept" +
                        "#/device-switch?path=" + Uri.EscapeDataString(expectedPath),
            DeviceSwitchNavigation.ForCurrentMenu(target, source));
    }

    [TestMethod]
    public void Overlong_paths_fall_back_without_copying_source_query_or_fragment()
    {
        var source = "http://localhost/#/" + new string('a', 512) + "?token=private";
        Assert.AreEqual("http://localhost:34568/#/device-switch?path=%2F",
            DeviceSwitchNavigation.ForCurrentMenu("http://localhost:34568/", source));
    }

    [TestMethod]
    [DataRow("javascript:alert(1)")]
    [DataRow("file:///tmp/index.html")]
    [DataRow("//another-host/")]
    public void Non_http_destinations_are_rejected(string target) =>
        Assert.ThrowsException<ArgumentException>(() => DeviceSwitchNavigation.ForCurrentMenu(target, "http://localhost/#/resource"));
}
