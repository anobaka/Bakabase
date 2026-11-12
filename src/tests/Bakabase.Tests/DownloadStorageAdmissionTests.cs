using System;
using System.IO;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business.Components.Downloader.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public sealed class DownloadStorageAdmissionTests
{
    private static string Volume(string path) => path.Replace('\\', '/').Contains("/nas/", StringComparison.Ordinal)
        ? "/nas" : "/other";
    private const long Plenty = 1024L * 1024 * 1024;

    [TestMethod]
    public void FullDestinationHoldsOtherSourcesOnSameVolumeButNotAnotherVolume()
    {
        var guard = new DownloadStorageAdmission(Volume, _ => Plenty, _ => { });
        guard.BeforeStart("/nas/first", false);
        guard.ObserveFailure("/nas/first", new LocalStorageException("LOCAL_STORAGE_FULL"));
        Assert.AreEqual("LOCAL_STORAGE_FULL", guard.BlockReason("/nas/different-provider/next"));
        Assert.ThrowsExactly<LocalStorageException>(() => guard.BeforeStart("/nas/next", false));
        Assert.IsNull(guard.BlockReason("/other/download"));
        guard.BeforeStart("/other/download", false);
    }

    [TestMethod]
    public void ExplicitContinuationNeedsBothSpaceAndSuccessfulProbe()
    {
        var free = Plenty; var probes = 0; var allowed = false;
        var guard = new DownloadStorageAdmission(Volume, _ => free, _ =>
        {
            probes++;
            if (!allowed) throw new UnauthorizedAccessException();
        });
        guard.ObserveFailure("/nas/a", new LocalStorageException("LOCAL_STORAGE_FULL"));
        free = 0;
        Assert.ThrowsExactly<LocalStorageException>(() => guard.BeforeStart("/nas/a", true));
        Assert.AreEqual(0, probes);
        free = Plenty;
        Assert.ThrowsExactly<LocalStorageException>(() => guard.BeforeStart("/nas/a", true));
        Assert.AreEqual("LOCAL_STORAGE_FULL", guard.BlockReason("/nas/b"));
        allowed = true;
        guard.BeforeStart("/nas/a", true);
        Assert.AreEqual(2, probes);
        Assert.IsNull(guard.BlockReason("/nas/b"));
        guard.BeforeStart("/nas/b", false);
    }

    [TestMethod]
    public void ANewConcurrentFailureCannotBeClearedByAnOlderProbe()
    {
        DownloadStorageAdmission guard = null!;
        guard = new DownloadStorageAdmission(Volume, _ => Plenty,
            _ => guard.ObserveFailure("/nas/b", new LocalStorageException("LOCAL_STORAGE_FULL")));
        guard.ObserveFailure("/nas/a", new LocalStorageException("LOCAL_STORAGE_FULL"));
        Assert.ThrowsExactly<LocalStorageException>(() => guard.BeforeStart("/nas/a", true));
        Assert.AreEqual("LOCAL_STORAGE_FULL", guard.BlockReason("/nas/c"));
    }

    [TestMethod]
    public void LowFreeSpaceHoldsBeforeStartingAnyPayloadAndDoesNotProbeAutomatically()
    {
        var probes = 0;
        var guard = new DownloadStorageAdmission(Volume, _ => DownloadStorageAdmission.MinimumFreeBytes - 1,
            _ => probes++);
        Assert.ThrowsExactly<LocalStorageException>(() => guard.BeforeStart("/nas/first", false));
        Assert.AreEqual("LOCAL_STORAGE_FULL", guard.BlockReason("/nas/next"));
        Assert.AreEqual(0, probes);
    }

    [TestMethod]
    public void UnknownBaiduAndNetworkErrorsDoNotFalselyHoldAWholeVolume()
    {
        var guard = new DownloadStorageAdmission(Volume, _ => Plenty, _ => { });
        foreach (var error in new Exception[] {new IOException("connection reset"),
                     new InvalidOperationException("BAIDU_OUTPUT_DIRECTORY_FAILED"),
                     new LocalStorageException("LOCAL_STORAGE_ACCESS_DENIED"),
                     new InvalidOperationException("PIKPAK_STORAGE_QUOTA_EXCEEDED")})
            guard.ObserveFailure("/nas/a", error);
        Assert.IsNull(guard.BlockReason("/nas/b"));
        guard.BeforeStart("/nas/b", false);
    }

    [TestMethod]
    public void ExplicitRetryOfALegacyFailureCanVerifyWritableWithoutAnExistingHold()
    {
        var probes = 0;
        var guard = new DownloadStorageAdmission(Volume, _ => Plenty, _ => probes++);
        guard.BeforeStart("/nas/new", true);
        Assert.AreEqual(0, probes);
        guard.BeforeStart("/nas/legacy-failed", true, verifyWritable: true);
        Assert.AreEqual(1, probes);
    }

    [TestMethod]
    public void KnownNativeCapacityErrorsAreRecognizedInsideAggregateWrappers()
    {
        var error = new AggregateException(new IOException("opaque", unchecked((int)0x80070070)));
        Assert.AreEqual("LOCAL_STORAGE_FULL", LocalStorageFailures.Code(error));
        Assert.IsNull(LocalStorageFailures.Code(new IOException("network write failed")));
        Assert.IsNull(LocalStorageFailures.Code(new InvalidOperationException("LOCAL_STORAGE_FULL")));
    }
    [TestMethod]
    public void MissingHeldMountCannotResumeAgainstItsParentVolume()
    {
        var mounted = true; var probes = 0;
        var guard = new DownloadStorageAdmission(_ => mounted ? "/nas" : "/", _ => Plenty, _ => probes++);
        guard.BeforeStart("/nas/a", false);
        guard.ObserveFailure("/nas/a", new LocalStorageException("LOCAL_STORAGE_FULL"));
        mounted = false;
        Assert.AreEqual("LOCAL_STORAGE_FULL", guard.BlockReason("/nas/new"));
        foreach (var path in new[] {"/nas/a", "/nas/new"})
        {
            var error = Assert.ThrowsExactly<LocalStorageException>(() => guard.BeforeStart(path, true));
            Assert.AreEqual("LOCAL_STORAGE_UNAVAILABLE", error.Code);
        }
        Assert.AreEqual(0, probes);
        mounted = true;
        guard.BeforeStart("/nas/a", true);
        Assert.AreEqual(1, probes);
    }

    [TestMethod]
    public void ExistingDirectoryLinksResolveBeforeVolumeIdentification()
    {
        if (OperatingSystem.IsWindows()) Assert.Inconclusive("Symlink privilege is environment dependent on Windows.");
        var scratch = Path.Combine(Path.GetTempPath(), "storage-links-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(scratch);
        try
        {
            var destination = Directory.CreateDirectory(Path.Combine(scratch, "nas")).FullName;
            var alias = Path.Combine(scratch, "alias");
            Directory.CreateSymbolicLink(alias, destination);
            var expected = Path.Combine(destination, "new", "download");
            Assert.AreEqual(expected, DownloadStorageAdmission.ResolveExistingPath(Path.Combine(alias, "new", "download")));
            var guard = new DownloadStorageAdmission(p => p.StartsWith(destination, StringComparison.Ordinal) ? destination : scratch,
                _ => Plenty, _ => { });
            guard.BeforeStart(Path.Combine(alias, "new", "download"), false);
            guard.ObserveFailure(Path.Combine(alias, "new", "download"), new LocalStorageException("LOCAL_STORAGE_FULL"));
            Assert.AreEqual("LOCAL_STORAGE_FULL", guard.BlockReason(Path.Combine(destination, "another")));
            Assert.IsNull(guard.BlockReason(Path.Combine(scratch, "unrelated")));
        }
        finally { Directory.Delete(scratch, recursive: true); }
    }

}
