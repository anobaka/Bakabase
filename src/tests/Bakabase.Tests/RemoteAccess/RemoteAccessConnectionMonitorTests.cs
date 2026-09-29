using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components.RemoteAccess;
using Bakabase.TestKit.Implementations;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Which open hub connections a change of the remote-access settings hangs up on, whoever
/// makes the change: it is saved through the options manager every writer goes through.
/// </summary>
[TestClass]
public class RemoteAccessConnectionMonitorTests
{
    private const string Paired = "paired";
    private const string Unpaired = "unpaired";

    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), "bakabase-remote-access-monitor", Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void DeleteSettings()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    /// <param name="hungUp">The connections hung up on, comma-separated.</param>
    [TestMethod]
    // Remote access off: everyone.
    [DataRow(RemoteAccessMode.Enabled, false, RemoteAccessMode.Disabled, false, "paired,unpaired")]
    [DataRow(RemoteAccessMode.Unrestricted, false, RemoteAccessMode.Disabled, false, "paired,unpaired")]
    // The mode changed otherwise: everyone who has not paired, to be judged by the new mode.
    [DataRow(RemoteAccessMode.Unrestricted, false, RemoteAccessMode.Enabled, false, "unpaired")]
    [DataRow(RemoteAccessMode.Unrestricted, false, RemoteAccessMode.Enabled, true, "unpaired")]
    [DataRow(RemoteAccessMode.Enabled, false, RemoteAccessMode.Unrestricted, false, "unpaired")]
    // Pairing became a requirement.
    [DataRow(RemoteAccessMode.Enabled, false, RemoteAccessMode.Enabled, true, "unpaired")]
    // Nothing that narrows who is let in.
    [DataRow(RemoteAccessMode.Enabled, true, RemoteAccessMode.Enabled, false, "")]
    [DataRow(RemoteAccessMode.Enabled, false, RemoteAccessMode.Enabled, false, "")]
    [DataRow(RemoteAccessMode.Unrestricted, false, RemoteAccessMode.Unrestricted, false, "")]
    public async Task A_change_hangs_up_on_what_it_no_longer_admits(RemoteAccessMode before, bool requiredBefore,
        RemoteAccessMode after, bool requiredAfter, string hungUp)
    {
        var (options, registry, aborted) = Start(new RemoteAccessOptions {Mode = before, RequirePairing = requiredBefore},
            RemoteAccessMode.Disabled);

        // Every row changes something, so the change is always announced; this one says
        // nothing about who is let in.
        await options.SaveAsync(new RemoteAccessOptions
            {Mode = after, RequirePairing = requiredAfter, AllowLiveTranscode = true});

        CollectionAssert.AreEquivalent(hungUp.Split(',', StringSplitOptions.RemoveEmptyEntries), aborted);
        Assert.AreEqual(2 - aborted.Count, registry.Count);
    }

    /// <summary>A mode left to the runtime default is judged as that default.</summary>
    [TestMethod]
    [DataRow(RemoteAccessMode.Disabled, "paired,unpaired")]
    [DataRow(RemoteAccessMode.Unrestricted, "unpaired")]
    [DataRow(RemoteAccessMode.Enabled, "")]
    public async Task No_mode_is_the_runtime_default(RemoteAccessMode runtimeDefault, string hungUp)
    {
        var (options, _, aborted) = Start(new RemoteAccessOptions {Mode = RemoteAccessMode.Enabled}, runtimeDefault);

        await options.SaveAsync(new RemoteAccessOptions {Mode = null});

        CollectionAssert.AreEquivalent(hungUp.Split(',', StringSplitOptions.RemoveEmptyEntries), aborted);
    }

    private (AspNetCoreOptionsManager<RemoteAccessOptions> Options, RemoteConnectionRegistry Registry,
        List<string> Aborted) Start(RemoteAccessOptions initial, RemoteAccessMode runtimeDefault)
    {
        var options = new AspNetCoreOptionsManager<RemoteAccessOptions>(Path.Combine(_directory, "remote-access.json"),
            "remote-access", new TestOptionsMonitor<RemoteAccessOptions>(initial),
            NullLogger<AspNetCoreOptionsManager<RemoteAccessOptions>>.Instance);

        var registry = new RemoteConnectionRegistry();
        var aborted = new List<string>();
        registry.Track("device-a", Paired, () => aborted.Add(Paired));
        registry.Track(null, Unpaired, () => aborted.Add(Unpaired));

        var monitor = new RemoteAccessConnectionMonitor(options, new RemoteAccessDefaults(runtimeDefault), registry);
        monitor.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
        return (options, registry, aborted);
    }
}
