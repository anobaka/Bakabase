using Bakabase.InsideWorld.Business.Components.Dependency.Implementations.BaiduPcsGo;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class BaiduPcsDiscoveryTests
{
    [TestMethod]
    public async Task VersionProbeUsesExplicitConfigDirectoryAndRejectsUnrecognizedProtocol()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The fixture uses a Unix shell.");
        var root = Path.Combine(Path.GetTempPath(), "baidu-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "bakabase-baidu-pcs");
            await File.WriteAllTextAsync(executable,
                "#!/bin/sh\n[ -n \"$BAIDUPCS_GO_CONFIG_DIR\" ] || exit 23\n[ \"$1\" = --version ] || exit 24\nprintf '4.0.2+bakabase.1\\n'\n");
            File.SetUnixFileMode(executable, (UnixFileMode)0x1c0);
            var discovery = new BaiduPcsGoDiscoverer(NullLoggerFactory.Instance);
            var found = await discovery.TryDiscoverAt(root, default);
            Assert.AreEqual(BaiduPcsGoService.BridgeVersion, found?.Version);
            Assert.AreEqual(root, found?.Location);
            await File.WriteAllTextAsync(executable, "#!/bin/sh\nprintf '4.0.2\\n'\n");
            Assert.IsNull(await discovery.TryDiscoverAt(root, default), "Upstream CLI is not the JSON bridge protocol.");
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task CancellationStopsVersionProcess()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("The fixture uses a Unix shell.");
        var root = Path.Combine(Path.GetTempPath(), "baidu-discovery-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var executable = Path.Combine(root, "bakabase-baidu-pcs");
            await File.WriteAllTextAsync(executable, "#!/bin/sh\nwhile :; do :; done\n");
            File.SetUnixFileMode(executable, (UnixFileMode)0x1c0);
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            await Assert.ThrowsAsync<OperationCanceledException>(() =>
                new BaiduPcsGoDiscoverer(NullLoggerFactory.Instance).TryDiscoverAt(root, cancel.Token)
                    .WaitAsync(TimeSpan.FromSeconds(3)));
        }
        finally { Directory.Delete(root, true); }
    }
}
