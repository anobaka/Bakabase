using Bakabase.Service.Services;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class DeviceIdServiceTests
{
    [TestMethod]
    public void DurableIdentitySurvivesNewServiceInstance()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "telemetry-id-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var first = Service(() => directory).GetOrCreate();
            Assert.IsTrue(Guid.TryParse(first, out var id) && id != Guid.Empty);
            Assert.AreEqual(first, File.ReadAllText(Path.Combine(directory, "device-id")));
            Assert.AreEqual(first, Service(() => directory).GetOrCreate());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    [DataRow("corrupted-identity")]
    [DataRow("00000000-0000-0000-0000-000000000000")]
    public void InvalidIdentityIsPreservedAndDisablesTracking(string original)
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "telemetry-id-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var path = Path.Combine(directory, "device-id");
            File.WriteAllText(path, original);
            var service = Service(() => directory);
            Assert.AreEqual(string.Empty, service.GetOrCreate());
            Assert.AreEqual(original, File.ReadAllText(path));
            File.WriteAllText(path, Guid.NewGuid().ToString("D"));
            Assert.AreEqual(string.Empty, service.GetOrCreate(), "Failure stays cached until the next process");
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    [TestMethod]
    public void UnavailableDirectoryNeverFallsBackToEphemeralIdentity()
    {
        var attempts = 0;
        var unavailable = Service(() => { attempts++; throw new UnauthorizedAccessException("private path"); });
        Assert.AreEqual(string.Empty, unavailable.GetOrCreate());
        Assert.AreEqual(string.Empty, unavailable.GetOrCreate());
        Assert.AreEqual(1, attempts);

        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "telemetry-id-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            // A directory in place of the file makes persistence fail on every operating system,
            // including test runners whose privileges bypass filesystem write permissions.
            Directory.CreateDirectory(Path.Combine(directory, "device-id"));
            Assert.AreEqual(string.Empty, Service(() => directory).GetOrCreate());
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    private static DeviceIdService Service(Func<string> directory) => new(directory, NullLogger<DeviceIdService>.Instance);
}
