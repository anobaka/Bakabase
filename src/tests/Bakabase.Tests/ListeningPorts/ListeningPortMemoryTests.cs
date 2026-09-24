using System;
using System.IO;
using System.Linq;
using Bakabase.Infrastructures.Components.App.Ports;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.ListeningPorts;

[TestClass]
public class ListeningPortMemoryTests
{
    private string _dir = null!;

    [TestInitialize]
    public void Setup() =>
        _dir = Path.Combine(Path.GetTempPath(), "bakabase-ports-" + Guid.NewGuid().ToString("N"));

    [TestCleanup]
    public void Cleanup()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [TestMethod]
    public void Nothing_remembered_reads_as_empty()
    {
        var remembered = new ListeningPortMemory(_dir).Read();
        Assert.AreEqual(0, remembered.Preferred.Count);
        Assert.AreEqual(0, remembered.LastUsed.Count);
        Assert.AreEqual(0, remembered.Candidates.Count);
    }

    [TestMethod]
    public void The_first_ports_recorded_become_the_preference_in_order()
    {
        new ListeningPortMemory(_dir).Record([34569, 34567, 34568]);

        var remembered = new ListeningPortMemory(_dir).Read();
        CollectionAssert.AreEqual(new[] {34569, 34567, 34568}, remembered.Preferred.ToArray());
        CollectionAssert.AreEqual(new[] {34569, 34567, 34568}, remembered.LastUsed.ToArray());
        CollectionAssert.AreEqual(new[] {34569, 34567, 34568}, remembered.Candidates.ToArray());
    }

    [TestMethod]
    public void A_fallback_is_remembered_as_last_used_but_never_replaces_the_preference()
    {
        var memory = new ListeningPortMemory(_dir);
        memory.Record([34567, 34568, 34569]);

        // Something held 34567 for one launch.
        memory.Record([34568, 34569, 34570]);

        var remembered = memory.Read();
        CollectionAssert.AreEqual(new[] {34567, 34568, 34569}, remembered.Preferred.ToArray(),
            "the window's origin belongs to the first preferred port; one conflict must not move it for good");
        CollectionAssert.AreEqual(new[] {34568, 34569, 34570}, remembered.LastUsed.ToArray());
        CollectionAssert.AreEqual(new[] {34567, 34568, 34569, 34570}, remembered.Candidates.ToArray(),
            "preferred first, then whatever of the last used is not among them");
    }

    [TestMethod]
    public void Each_data_directory_has_its_own()
    {
        var other = _dir + "-other";
        try
        {
            new ListeningPortMemory(_dir).Record([34567]);
            new ListeningPortMemory(other).Record([34570]);
            CollectionAssert.AreEqual(new[] {34567}, new ListeningPortMemory(_dir).Read().Candidates.ToArray());
            CollectionAssert.AreEqual(new[] {34570}, new ListeningPortMemory(other).Read().Candidates.ToArray());
        }
        finally
        {
            try { Directory.Delete(other, recursive: true); } catch { /* best effort */ }
        }
    }

    [TestMethod]
    public void A_damaged_file_reads_as_no_memory()
    {
        Directory.CreateDirectory(_dir);
        var memory = new ListeningPortMemory(_dir);

        File.WriteAllText(memory.FilePath, "{ not json");
        Assert.AreEqual(0, memory.Read().Candidates.Count);

        File.WriteAllText(memory.FilePath, """{"preferred":[0,-1,70000,34567,34567],"lastUsed":[34568,99999]}""");
        CollectionAssert.AreEqual(new[] {34567}, memory.Read().Preferred.ToArray(),
            "impossible values and repeats are dropped");
        CollectionAssert.AreEqual(new[] {34568}, memory.Read().LastUsed.ToArray());

        File.WriteAllText(memory.FilePath, "null");
        Assert.AreEqual(0, memory.Read().Candidates.Count);
    }

    [TestMethod]
    public void An_unchanged_memory_is_not_rewritten()
    {
        var memory = new ListeningPortMemory(_dir);
        memory.Record([34567, 34568, 34569]);
        var stamp = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(memory.FilePath, stamp);

        memory.Record([34567, 34568, 34569]);
        Assert.AreEqual(stamp, File.GetLastWriteTimeUtc(memory.FilePath));

        memory.Record([34570]);
        Assert.AreNotEqual(stamp, File.GetLastWriteTimeUtc(memory.FilePath));
    }

    [TestMethod]
    public void An_unwritable_directory_is_not_an_error()
    {
        var blocker = _dir + ".file";
        File.WriteAllText(blocker, "x");
        try
        {
            var memory = new ListeningPortMemory(Path.Combine(blocker, "sub"));
            memory.Record([34567]);
            Assert.AreEqual(0, memory.Read().Candidates.Count);
        }
        finally
        {
            File.Delete(blocker);
        }
    }
}
