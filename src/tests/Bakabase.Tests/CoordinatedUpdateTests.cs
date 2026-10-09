using Bakabase.Infrastructures.Components.App.Upgrade;
using Velopack;
using Velopack.Locators;

namespace Bakabase.Tests;

[TestClass]
public sealed class CoordinatedUpdateTests
{
    [TestMethod]
    public void UpdateWaitsForTheCoordinatorBeforeReplacingTheSharedRelease()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-update-handoff-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var update = Path.Combine(root, "Update.exe");
            File.WriteAllText(update, "test process abstraction prevents execution");
            var package = new VelopackAsset { FileName = "Bakabase-2.0.0-full.nupkg", Version = SemanticVersion.Parse("2.0.0") };
            File.WriteAllText(Path.Combine(root, package.FileName), "downloaded package fixture");
            var process = new RecordingProcess();
            var locator = new RecordingLocator(root, update, process);
            var parentPid = Environment.ProcessId + 1;
            var launch = AppUpdater.PrepareCoordinatedUpdate(locator, package, parentPid);
            Assert.IsNull(process.Arguments, "Preparing a validated restart must not start the updater early.");
            launch();

            Assert.AreEqual(update, process.Executable);
            var arguments = process.Arguments!;
            Assert.AreEqual(parentPid.ToString(), arguments[Array.IndexOf(arguments, "--waitPid") + 1],
                "The child can exit while its coordinator still holds the same release files.");
            Assert.AreEqual(Path.Combine(root, package.FileName), arguments[Array.IndexOf(arguments, "--package") + 1]);
            Assert.IsFalse(arguments.Contains("--norestart"));
            Assert.IsFalse(arguments.Any(a => a.StartsWith("--bakabase-role=")),
                "The updater must relaunch the public coordinator entry, not an unauthenticated child role.");
            Assert.IsFalse(process.Exited);
        }
        finally { Directory.Delete(root, true); }
    }

    [TestMethod]
    public void CoordinatedUpdatesRejectAbsentOrSelfProcessIdentities()
    {
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AppUpdater.PrepareCoordinatedUpdate(null!, null!, 0));
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => AppUpdater.PrepareCoordinatedUpdate(null!, null!, Environment.ProcessId));
    }

    private sealed class RecordingLocator(string root, string updater, IProcessImpl process)
        : TestVelopackLocator("Bakabase", "1.0.0", root, root, root, updater)
    {
        public override IProcessImpl Process => process;
    }

    private sealed class RecordingProcess : IProcessImpl
    {
        public string? Executable { get; private set; }
        public string[]? Arguments { get; private set; }
        public bool Exited { get; private set; }
        public string GetCurrentProcessPath() => "unused";
        public uint GetCurrentProcessId() => (uint)Environment.ProcessId;
        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow)
        {
            Executable = exePath;
            Arguments = args.ToArray();
        }
        public void Exit(int exitCode) => Exited = true;
    }
}
