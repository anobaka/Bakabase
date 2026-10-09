using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Services;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ResourceUsageTests
{
    [TestMethod]
    public void Cpu_is_normalized_to_available_capacity_and_clamped()
    {
        Assert.AreEqual(25d, ResourceUsageService.CalculateCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1), 4));
        Assert.AreEqual(100d, ResourceUsageService.CalculateCpuPercent(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(1), 4));
        Assert.AreEqual(0d, ResourceUsageService.CalculateCpuPercent(TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(1), 4));
        Assert.AreEqual(0d, ResourceUsageService.CalculateCpuPercent(TimeSpan.FromSeconds(1), TimeSpan.Zero, 4));
    }

    [TestMethod]
    public void Directory_measurement_includes_hidden_files_but_does_not_follow_links()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-usage-" + Guid.NewGuid().ToString("N"));
        try
        {
            var data = Directory.CreateDirectory(Path.Combine(root, "data"));
            var external = Directory.CreateDirectory(Path.Combine(root, "media"));
            File.WriteAllBytes(Path.Combine(external.FullName, "large.dat"), new byte[10000]);
            File.WriteAllBytes(Path.Combine(data.FullName, ".hidden"), new byte[7]);
            var nested = Directory.CreateDirectory(Path.Combine(data.FullName, "covers"));
            File.WriteAllBytes(Path.Combine(nested.FullName, "cover.jpg"), new byte[31]);
            // Creating symlinks on Windows may require an elevated account; Unix CI exercises links.
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(data.FullName, "external"), external.FullName);
                Directory.CreateSymbolicLink(Path.Combine(nested.FullName, "cycle"), data.FullName);
                File.CreateSymbolicLink(Path.Combine(data.FullName, "linked.dat"), Path.Combine(external.FullName, "large.dat"));
            }
            var result = ResourceUsageService.MeasureDirectory(data.FullName, CancellationToken.None);
            Assert.AreEqual(38L, result.Bytes);
            Assert.IsFalse(result.Partial);
            Assert.ThrowsException<OperationCanceledException>(() => ResourceUsageService.MeasureDirectory(data.FullName, new CancellationToken(true)));
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Reads_share_a_background_measurement_and_cached_result()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-usage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var file = Path.Combine(root, "file");
            File.WriteAllBytes(file, new byte[17]);
            var service = new ResourceUsageService(root, CancellationToken.None);
            var first = service.GetSnapshot();
            Assert.IsTrue(first.MemoryBytes > 0);
            Assert.IsTrue(first.DataDirectoryScanning);
            var snapshot = first;
            for (var n = 0; snapshot.DataDirectoryScanning && n < 100; n++)
            {
                await Task.Delay(10);
                snapshot = service.GetSnapshot();
            }
            Assert.AreEqual(17L, snapshot.DataDirectoryBytes);
            Assert.IsNotNull(snapshot.DataDirectoryUpdatedAt);
            File.WriteAllBytes(file, new byte[99]);
            var cached = service.GetSnapshot();
            Assert.AreEqual(17L, cached.DataDirectoryBytes);
            Assert.IsFalse(cached.DataDirectoryScanning);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public async Task Missing_directory_is_unavailable_instead_of_zero()
    {
        var service = new ResourceUsageService(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N")), CancellationToken.None);
        var snapshot = service.GetSnapshot();
        for (var n = 0; snapshot.DataDirectoryScanning && n < 100; n++)
        {
            await Task.Delay(10);
            snapshot = service.GetSnapshot();
        }
        Assert.IsNull(snapshot.DataDirectoryBytes);
        Assert.IsTrue(snapshot.DataDirectoryUnavailable);
    }
}
