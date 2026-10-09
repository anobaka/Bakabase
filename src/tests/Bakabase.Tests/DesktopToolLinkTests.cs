using Bakabase.App;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Shell.Components;
using Bakabase.Service.Components.ServerData;
using Avalonia.Controls.ApplicationLifetimes;
using System.Reflection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed class DesktopToolLinkTests
{
    [TestMethod]
    [DataRow(DesktopToolLink.FileProcessor, "/file-processor")]
    [DataRow(DesktopToolLink.FileNameModifier, "/file-name-modifier")]
    public void OnlyTheTwoPageIntentsAreResolved(string link, string route) =>
        Assert.AreEqual(route, DesktopToolLink.GetRoute(link));

    [TestMethod]
    [DataRow("bakabase://tools/file-processor?file=C:/private")]
    [DataRow("bakabase://tools/file-processor#execute")]
    [DataRow("bakabase://tools/file-processor/")]
    [DataRow("bakabase://tools/../tools/file-processor")]
    [DataRow("bakabase://tools/%66ile-processor")]
    [DataRow("bakabase://tools/file-processor%00")]
    [DataRow("bakabase://user@tools/file-processor")]
    [DataRow("bakabase://tools:123/file-processor")]
    [DataRow("bakabase://evil/file-processor")]
    [DataRow("bakabase://tools/delete")]
    [DataRow("https://tools/file-processor")]
    [DataRow("file:///etc/passwd")]
    [DataRow("javascript:alert(1)")]
    [DataRow(" bakabase://tools/file-processor")]
    [DataRow("bakabase://tools/file-processor\nSHOW")]
    public void UntrustedPayloadsNeverBecomeNavigation(string link) => Assert.IsNull(DesktopToolLink.GetRoute(link));

    [TestMethod]
    public void ProtocolCommandAcceptsExactlyOneAllowlistedUriAndNoInjectedOptions()
    {
        Assert.IsTrue(DesktopToolLink.TryNormalizeArguments([DesktopToolLink.Argument, DesktopToolLink.FileProcessor], out var args));
        CollectionAssert.AreEqual(new[] { DesktopToolLink.FileProcessor }, args);
        Assert.IsFalse(DesktopToolLink.TryNormalizeArguments([DesktopToolLink.Argument, DesktopToolLink.FileProcessor, "--urls", "http://0.0.0.0:1"], out _));
        Assert.IsFalse(DesktopToolLink.TryNormalizeArguments([DesktopToolLink.Argument, "bakabase://tools/file-processor\" --setup-child worker"], out _));
        Assert.IsFalse(DesktopToolLink.TryNormalizeArguments([DesktopToolLink.Argument], out _));
        Assert.IsFalse(DesktopToolLink.TryNormalizeArguments(["--setup-child", DesktopToolLink.Argument], out _));
        var ordinary = new[] { "--veloapp-updated", "2.4.0" };
        Assert.IsTrue(DesktopToolLink.TryNormalizeArguments(ordinary, out args));
        Assert.AreSame(ordinary, args, "ordinary install and internal startup arguments retain their existing behavior");
    }

    [TestMethod]
    public void ColdActivationWaitsForLocalReadinessAndIsConsumedOnce()
    {
        var navigations = new List<string>();
        var navigation = new DesktopToolNavigation(navigations.Add);
        navigation.Request(DesktopToolLink.FileProcessor);
        navigation.Request(DesktopToolLink.FileNameModifier);
        Assert.HasCount(0, navigations, "the Setup window must complete before the business page opens");
        navigation.Ready("http://127.0.0.1:12345/#/resource");
        CollectionAssert.AreEqual(new[] { "http://127.0.0.1:12345/#/file-name-modifier" }, navigations);
        navigation.Ready("http://127.0.0.1:12345/#/resource");
        Assert.HasCount(1, navigations, "readiness must not replay an already applied navigation");
    }

    [TestMethod]
    public void WarmActivationUsesTheOriginalLocalOriginEvenAfterShowingAManagedRelay()
    {
        var navigations = new List<string>();
        var navigation = new DesktopToolNavigation(navigations.Add);
        navigation.Ready("http://localhost:12345/#/resource");
        navigation.Ready("http://127.0.0.1:23456/?ticket=one-use#/remote-page");
        navigation.Request("bakabase://tools/file-processor?execute=true");
        navigation.Request(DesktopToolLink.FileProcessor);
        CollectionAssert.AreEqual(new[] { "http://localhost:12345/#/file-processor" }, navigations);
    }

    [TestMethod]
    [DataRow("https://some-server.example/")]
    [DataRow("file:///tmp/app.html")]
    [DataRow("http://user@127.0.0.1/")]
    public void AnExternalServerCannotBeUsedAsTheLocalToolDestination(string address) =>
        Assert.ThrowsExactly<ArgumentException>(() => DesktopToolLink.LocalPage(address, "/file-processor"));

    [TestMethod]
    public void WindowsRegistrationQuotesTheExecutableAndKeepsAFixedLeadingMarker()
    {
        Assert.AreEqual("\"C:\\Users\\Test User\\Bakabase\\current\\Bakabase.exe\" --desktop-tool \"%1\"",
            DesktopProtocolRegistration.Command(@"C:\Users\Test User\Bakabase\current\Bakabase.exe"));
        Assert.ThrowsExactly<ArgumentException>(() => DesktopProtocolRegistration.Command("bad\"path.exe"));
    }
}

[TestClass]
[DoNotParallelize]
public sealed class DesktopToolActivationTests
{
    [TestInitialize] public void Initialize() => SingleInstanceGuard.ResetForTests();
    [TestCleanup] public void Cleanup() => SingleInstanceGuard.ResetForTests();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void MacActivationDuringSetupWaitsForTheNextBusinessReadiness(bool hadBusinessBeforeMaintenance)
    {
        using var bootstrap = new DesktopSetupBootstrap();
        typeof(DesktopSetupBootstrap).GetField("_businessWasReady", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(bootstrap, hadBusinessBeforeMaintenance);
        bootstrap.OnActivated(new ProtocolActivatedEventArgs(new Uri(DesktopToolLink.FileProcessor)));
        bootstrap.OnActivated(new ProtocolActivatedEventArgs(new Uri("bakabase://tools/file-processor?execute=true")));
        Assert.AreEqual(DesktopToolLink.FileProcessor, typeof(DesktopSetupBootstrap)
            .GetField("_pendingToolLink", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(bootstrap),
            "both first Setup and a maintenance Setup must retain navigation until a business child is ready");
        Assert.IsNull(SingleInstanceGuard.PrimaryDirectory, "native activation never initializes a database");
    }

    [TestMethod]
    public void NativeActivationBeforeTheShellExistsIsDeliveredOnceAndResetAtShutdown()
    {
        Assert.IsFalse(SingleInstanceGuard.RequestToolNavigation("bakabase://tools/file-processor?execute=true"));
        Assert.IsTrue(SingleInstanceGuard.RequestToolNavigation(DesktopToolLink.FileProcessor));
        var links = new List<string>();
        SingleInstanceGuard.SetToolNavigationHandler(links.Add);
        SingleInstanceGuard.SetToolNavigationHandler(links.Add);
        CollectionAssert.AreEqual(new[] { DesktopToolLink.FileProcessor }, links);
        SingleInstanceGuard.ReleaseAll();
        SingleInstanceGuard.SetToolNavigationHandler(links.Add);
        Assert.HasCount(1, links);
    }

    [TestMethod]
    public async Task ClosingAnUnsubmittedSetupKeepsTheRunningBusinessImmediatelyReachable()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-cancelled-setup-tool-" + Guid.NewGuid().ToString("N"));
        var previous = Environment.GetEnvironmentVariable(DefaultAppDataPathResolver.EnvVarName);
        Directory.CreateDirectory(root);
        try
        {
            Environment.SetEnvironmentVariable(DefaultAppDataPathResolver.EnvVarName, root);
            using var bootstrap = new DesktopSetupBootstrap();
            // Closing an unsubmitted wizard leaves no window but the same business child.
            // Only OnBusinessUnavailable may withdraw readiness, never showing/closing Setup.
            typeof(DesktopSetupBootstrap).GetField("_businessWasReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bootstrap, true);
            typeof(DesktopSetupBootstrap).GetField("_businessReady", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(bootstrap, true);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var activation = ActivationServer.Start(ActivationChannel.GetName(DataDirectoryIdentity.Normalize(root)), link => received.TrySetResult(link));
            bootstrap.OnActivated(new ProtocolActivatedEventArgs(new Uri(DesktopToolLink.FileNameModifier)));
            Assert.AreEqual(DesktopToolLink.FileNameModifier, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)),
                "cancelling a wizard must not require another child start/readiness event before opening a tool");
        }
        finally
        {
            Environment.SetEnvironmentVariable(DefaultAppDataPathResolver.EnvVarName, previous);
            Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task WarmLaunchPassesTheAllowlistedIntentOverTheActualInstanceChannel()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-tool-activation-" + Guid.NewGuid().ToString("N"));
        try
        {
            Assert.AreEqual(SingleInstanceEntry.Entered, SingleInstanceGuard.Enter(root));
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            SingleInstanceGuard.SetToolNavigationHandler(link => received.TrySetResult(link));
            Assert.IsTrue(await DesktopSetupBootstrap.TryActivateExistingAsync([DesktopToolLink.FileNameModifier], root));
            Assert.AreEqual(DesktopToolLink.FileNameModifier, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            CollectionAssert.AreEqual(new[] { DataDirectoryLock.FileName }, Directory.GetFiles(root).Select(Path.GetFileName).ToArray(),
                "activation must not initialize or touch application data");
        }
        finally
        {
            SingleInstanceGuard.ReleaseAll();
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [TestMethod]
    public async Task ALaunchDuringFirstSetupCanActivateTheCoordinatorWithoutABusinessDatabase()
    {
        var root = Path.Combine(Path.GetTempPath(), "bakabase-setup-tool-activation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            using var control = new FileStream(Path.Combine(root, SetupProcessCoordinator.LockFileName),
                FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var activation = ActivationServer.Start(DesktopSetupBootstrap.SetupActivationChannel(root), link => received.TrySetResult(link));
            Assert.IsTrue(await DesktopSetupBootstrap.TryActivateExistingAsync([DesktopToolLink.FileProcessor], root));
            Assert.AreEqual(DesktopToolLink.FileProcessor, await received.Task.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.IsFalse(File.Exists(Path.Combine(root, DataDirectoryLock.FileName)), "the business database still has no owner or files");
            CollectionAssert.AreEqual(new[] { SetupProcessCoordinator.LockFileName }, Directory.GetFiles(root).Select(Path.GetFileName).ToArray());
        }
        finally { Directory.Delete(root, true); }
    }
}
