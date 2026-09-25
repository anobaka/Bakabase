using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Modules.DataSync.Abstractions;

namespace Bakabase.TestKit.DataSync;

/// <summary>
/// A per-provider device identity (spec §2.11): a random node id and library epoch, named
/// <c>test-device-&lt;8 hex&gt;</c>. A test that needs other values registers its own through
/// <c>TestServiceBuilder.BuildServiceProvider(configure)</c>.
/// </summary>
public sealed class TestDataSyncDeviceIdentity : IDataSyncDeviceIdentity
{
    public TestDataSyncDeviceIdentity() : this(NewDevice())
    {
    }

    public TestDataSyncDeviceIdentity(DataSyncDevice device) => Device = device;

    public DataSyncDevice Device { get; set; }

    public Task<DataSyncDevice> GetAsync(CancellationToken ct) => Task.FromResult(Device);

    public static DataSyncDevice NewDevice()
    {
        var id = Guid.NewGuid().ToString("N");
        return new DataSyncDevice(id, Guid.NewGuid().ToString("N"), $"test-device-{id[..8]}");
    }
}

/// <summary>The TestKit's host kind: a desktop by default (spec §2.11).</summary>
public sealed class TestDataSyncHostKind : IDataSyncHostKind
{
    public bool IsHeadless { get; set; }
}
