using System.Globalization;
using Bakabase.Service.Components.Acquisition.Planning;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionProcessingWorkspaceAbsenceTests
{
    private readonly DirectoryInfo fixture = Directory.CreateTempSubdirectory("workspace-absence-");
    private string Root => Path.Combine(fixture.FullName, "stage");
    private string Held => Path.Combine(Root, ".bakabase-reclaim-" + new string('a', 32));

    [TestInitialize]
    public void Setup() => Directory.CreateDirectory(Held);

    [TestCleanup]
    public void Clean() => fixture.Delete(true);

    [TestMethod]
    public void ThousandsOfMissingSlotsShareTwoRealDirectoryReadsPerBoundedBatch()
    {
        var count = 2 * AcquisitionProcessingWorkspaceAbsence.BatchSize + 1;
        for (var i = 0; i < 256; i++) File.WriteAllText(Path.Combine(Held, $"user-{i}"), "preserved");
        var enumerations = 0;
        var batch = new AcquisitionProcessingWorkspaceAbsence(Root, Held, default, path =>
        {
            enumerations++;
            return Directory.EnumerateFileSystemEntries(path);
        });
        for (var i = 0; i < count; i++) batch.Add(i);
        batch.Flush();
        Assert.AreEqual(6, enumerations);
        batch.VerifyBeforeRetirement(count);
        Assert.AreEqual(8, enumerations);
        Assert.AreEqual(256, Directory.EnumerateFiles(Held).Count());
    }

    [TestMethod]
    public void ReappearanceDuringSecondDirectoryReadOrAfterEarlierBatchBlocksAndPreservesReplacement()
    {
        var reads = 0;
        var name = Path.Combine(Held, "0000000A");
        var batch = new AcquisitionProcessingWorkspaceAbsence(Root, Held, default, path =>
        {
            if (++reads == 2) File.WriteAllText(name, "new data in a reserved slot");
            return Directory.EnumerateFileSystemEntries(path);
        });
        batch.Add(10);
        Assert.ThrowsExactly<IOException>(batch.Flush);
        Assert.AreEqual("new data in a reserved slot", File.ReadAllText(name));
        File.Delete(name);
        batch.Flush();
        File.WriteAllText(name, "created after batch check");
        Assert.ThrowsExactly<IOException>(() => batch.VerifyBeforeRetirement(11));
        Assert.AreEqual("created after batch check", File.ReadAllText(name));
        File.Delete(name);
        File.WriteAllText(Path.Combine(Held, 11.ToString("X8", CultureInfo.InvariantCulture)), "outside selection");
        batch.VerifyBeforeRetirement(11);
    }

    [TestMethod]
    public void MissingQuarantineOnlyCountsBelowReadableRegisteredRoot()
    {
        Directory.Delete(Held);
        var batch = new AcquisitionProcessingWorkspaceAbsence(Root, Held, default);
        batch.Add(1);
        batch.Flush();
        batch.VerifyBeforeRetirement(2);
        Directory.Delete(Root);
        try { batch.VerifyBeforeRetirement(2); Assert.Fail("Detached storage accepted."); }
        catch (IOException) { }
    }

    [TestMethod]
    public void UnreadableDirectoryAndLinkNeverProveThatSelectedFilesAreGone()
    {
        var batch = new AcquisitionProcessingWorkspaceAbsence(Root, Held, default,
            _ => throw new UnauthorizedAccessException("Directory unavailable"));
        batch.Add(1);
        Assert.ThrowsExactly<UnauthorizedAccessException>(batch.Flush);
        if (OperatingSystem.IsWindows()) return;
        Directory.Delete(Held);
        var other = Directory.CreateDirectory(Path.Combine(fixture.FullName, "other")).FullName;
        Directory.CreateSymbolicLink(Held, other);
        Assert.ThrowsExactly<InvalidDataException>(() => batch.VerifyBeforeRetirement(2));
        Directory.Delete(Held);
    }

    [TestMethod]
    public void DisappearingDirectoryDuringEnumerationAndCancellationCannotApproveRetirement()
    {
        var batch = new AcquisitionProcessingWorkspaceAbsence(Root, Held, default, path =>
        {
            var entries = Directory.GetFileSystemEntries(path);
            Directory.Move(path, Path.Combine(fixture.FullName, "detached"));
            return entries;
        });
        Assert.Throws<IOException>(() => batch.VerifyBeforeRetirement(10));
        Directory.CreateDirectory(Held);
        File.WriteAllText(Path.Combine(Held, "unregistered"), "preserve");
        using var cancel = new CancellationTokenSource();
        batch = new AcquisitionProcessingWorkspaceAbsence(Root, Held, cancel.Token, path =>
        {
            cancel.Cancel();
            return Directory.EnumerateFileSystemEntries(path);
        });
        Assert.ThrowsExactly<OperationCanceledException>(() => batch.VerifyBeforeRetirement(10));
    }

    [TestMethod]
    public void CaseVariationOnlyBlocksWhenItNamesTheSelectedStorageEntry()
    {
        var lower = Path.Combine(Held, "0000000a");
        File.WriteAllText(lower, "new bytes");
        var batch = new AcquisitionProcessingWorkspaceAbsence(Root, Held, default);
        if (File.Exists(Path.Combine(Held, "0000000A")))
            Assert.ThrowsExactly<IOException>(() => batch.VerifyBeforeRetirement(11));
        else
            batch.VerifyBeforeRetirement(11);
        Assert.AreEqual("new bytes", File.ReadAllText(lower));
    }
}
