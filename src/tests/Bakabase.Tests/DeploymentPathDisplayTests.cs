using System.Text.Json;
using Bakabase.Service.Services;

namespace Bakabase.Tests;

[TestClass]
public sealed class DeploymentPathDisplayTests
{
    private static string Manifest(params object[] mounts) => JsonSerializer.Serialize(new
    {
        schemaVersion = 1,
        readOnlyRoot = true,
        mounts
    });

    [TestMethod]
    public void BindDisplay_UsesLongestMountAndPreservesServerPath()
    {
        var display = new DeploymentPathDisplay(true, Manifest(
            new { type = "bind", target = "/data", source = "/Users/me/app data", readOnly = false },
            new { type = "bind", target = "/data/backups", source = "/Volumes/archive", readOnly = true }));
        var data = display.Describe("/data/logs");
        Assert.AreEqual("/data/logs", data.ServerPath);
        Assert.AreEqual("/Users/me/app data/logs", data.HostPath);
        Assert.AreEqual("bind", data.StorageKind);
        Assert.AreEqual(false, data.ReadOnly);
        var backup = display.Describe("/data/backups/2026");
        Assert.AreEqual("/Volumes/archive/2026", backup.HostPath);
        Assert.AreEqual(true, backup.ReadOnly);
        Assert.AreEqual("/Volumes/archive", display.Describe("/data/backups").HostPath);
    }

    [TestMethod]
    public void NamedVolumeAndTmpfs_MaskParentBindWithoutInventingAHostPath()
    {
        var display = new DeploymentPathDisplay(true, Manifest(
            new { type = "bind", target = "/data", source = "/host", readOnly = true },
            new { type = "volume", target = "/data/cache", readOnly = false },
            new { type = "tmpfs", target = "/data/temp", readOnly = false }));
        var volume = display.Describe("/data/cache/items");
        Assert.IsNull(volume.HostPath);
        Assert.AreEqual("volume", volume.StorageKind);
        Assert.AreEqual(false, volume.ReadOnly);
        var tmpfs = display.Describe("/data/temp/work");
        Assert.IsNull(tmpfs.HostPath);
        Assert.AreEqual("container", tmpfs.StorageKind);
        Assert.AreEqual(false, tmpfs.ReadOnly);
    }

    [TestMethod]
    public void Prefixes_RespectDirectoryBoundariesAndNormalizedPaths()
    {
        var display = new DeploymentPathDisplay(true, Manifest(
            new { type = "bind", target = "/data/", source = "/host", readOnly = false }));
        foreach (var outside in new[] { "/database/logs", "/data2", "/data/../private", "/Data/logs" })
        {
            var result = display.Describe(outside);
            Assert.IsNull(result.HostPath, outside);
            Assert.AreEqual("container", result.StorageKind);
            Assert.AreEqual(true, result.ReadOnly);
        }
        Assert.AreEqual("/host/logs", display.Describe("/data/./temp/../logs").HostPath);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("{bad json")]
    [DataRow("null")]
    [DataRow("{\"schemaVersion\":2,\"mounts\":[]}")]
    [DataRow("{\"schemaVersion\":1,\"mounts\":[null]}")]
    [DataRow("{\"schemaVersion\":1,\"mounts\":[{\"type\":\"bind\",\"target\":\"/data\",\"source\":\"relative\"}]}")]
    [DataRow("{\"schemaVersion\":1,\"mounts\":[{\"type\":\"future\",\"target\":\"/data\"}]}")]
    public void MissingOrInvalidMetadata_FallsBackToUnknownWithoutChangingPaths(string? metadata)
    {
        var result = new DeploymentPathDisplay(true, metadata).Describe("/data/logs");
        Assert.AreEqual("/data/logs", result.ServerPath);
        Assert.AreEqual("unknown", result.StorageKind);
        Assert.IsNull(result.HostPath);
        Assert.IsNull(result.ReadOnly);
    }

    [TestMethod]
    public void DuplicateTargetsAndOversizedMetadata_AreNotPartiallyTrusted()
    {
        var duplicate = Manifest(new { type = "bind", target = "/data", source = "/host" },
            new { type = "volume", target = "/data/" });
        foreach (var metadata in new[] { duplicate, new string(' ', 65537) })
            Assert.AreEqual("unknown", new DeploymentPathDisplay(true, metadata).Describe("/data").StorageKind);
    }

    [DataTestMethod]
    [DataRow("C:\\Bakabase", "C:\\Bakabase\\logs")]
    [DataRow("C:/Bakabase", "C:\\Bakabase\\logs")]
    [DataRow("\\\\nas\\share\\Bakabase", "\\\\nas\\share\\Bakabase\\logs")]
    [DataRow("/host/$data", "/host/$data/logs")]
    [DataRow("/", "/logs")]
    public void HostPathSyntax_IsIndependentOfTheContainerPlatform(string host, string expected)
    {
        var display = new DeploymentPathDisplay(true, Manifest(new { type = "bind", target = "/data", source = host }));
        Assert.AreEqual(expected, display.Describe("/data/logs").HostPath);
    }

    [TestMethod]
    public void RootMountAndDuplicateDisplayPaths_AreHandled()
    {
        var display = new DeploymentPathDisplay(true, Manifest(new { type = "bind", target = "/", source = "/host/root" }));
        var result = display.Describe(new[] { "/data", "/data", "", null, "/data/logs" });
        Assert.IsTrue(result.IsContainer);
        Assert.HasCount(2, result.Paths);
        Assert.AreEqual("/host/root/data", result.Paths[0].HostPath);
        Assert.AreEqual("/host/root/data/logs", result.Paths[1].HostPath);
    }

    [TestMethod]
    public void Desktop_IgnoresContainerMetadata()
    {
        var result = new DeploymentPathDisplay(false, Manifest(new { type = "bind", target = "/", source = "/other" }))
            .Describe(new[] { "/Users/me/Bakabase" });
        Assert.IsFalse(result.IsContainer);
        Assert.AreEqual("local", result.Paths[0].StorageKind);
        Assert.IsNull(result.Paths[0].HostPath);
    }
}
