using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App.Relocation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Relocation;

/// <summary>
/// Integration tests that drive the runner end-to-end against a real temp filesystem and
/// capture every <see cref="RelocationProgress"/> event the splash window will see. The point
/// is not the UI — it's the contract between the runner and any progress consumer (currently
/// only <c>RelocationSplashWindow</c>). If these phase / counter assertions hold, the splash
/// rendering of those phases is purely a function of the strings we ship for them.
/// </summary>
[TestClass]
public class RelocationProgressIntegrationTests
{
    /// <summary>
    /// Synchronous progress collector — <see cref="System.Progress{T}"/> marshals callbacks
    /// through the captured <see cref="System.Threading.SynchronizationContext"/>, which in
    /// MSTest means events arrive on the thread pool and may land after RunAsync returns. We
    /// need ordered, fully-flushed capture for the phase-sequence assertions.
    /// </summary>
    private sealed class SyncProgress : IProgress<RelocationProgress>
    {
        public List<RelocationProgress> Events { get; } = new();
        public void Report(RelocationProgress value)
        {
            lock (Events) Events.Add(value);
        }
    }

    private static SyncProgress CaptureProgress() => new();

    [TestMethod]
    public async Task UseTarget_EmitsFinalizingThenDone()
    {
        using var h = new RelocationTestHarness();
        Directory.CreateDirectory(h.TargetDir);
        File.WriteAllBytes(Path.Combine(h.TargetDir, "data.bin"), new byte[] { 9 });

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.UseTarget,
            Target = h.TargetDir,
        });

        var sink = CaptureProgress();
        var outcome = await h.RunAsync(sink);
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind);

        var phases = sink.Events.Select(e => e.Phase).ToList();
        CollectionAssert.Contains(phases, RelocationPhase.Finalizing);
        Assert.AreEqual(RelocationPhase.Done, phases[^1],
            "last reported phase must be Done");
    }

    [TestMethod]
    public async Task MergeOverwrite_EmitsFullPhaseSequence_WithMonotonicCopyingCounters()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1, 2, 3 })
         .WithFile("data/file2.bin", new byte[] { 4, 5, 6, 7, 8 })
         .WithFile("configs/foo.json", "{\"x\":1}");

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var sink = CaptureProgress();
        var outcome = await h.RunAsync(sink);
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);


        var phases = sink.Events.Select(e => e.Phase).Distinct().ToList();
        CollectionAssert.Contains(phases, RelocationPhase.Starting);
        CollectionAssert.Contains(phases, RelocationPhase.Copying);
        CollectionAssert.Contains(phases, RelocationPhase.Validating);
        CollectionAssert.Contains(phases, RelocationPhase.Replacing);
        CollectionAssert.Contains(phases, RelocationPhase.Finalizing);
        CollectionAssert.Contains(phases, RelocationPhase.Done);
        Assert.AreEqual(RelocationPhase.Done, sink.Events[^1].Phase);

        // During Copying, ProcessedFiles + ProcessedBytes must be monotonically non-decreasing
        // (the splash UI computes FilesFraction from these and would jitter visually otherwise).
        var copying = sink.Events.Where(e => e.Phase == RelocationPhase.Copying).ToList();
        Assert.IsTrue(copying.Count > 0, "expected at least one Copying event");
        for (var i = 1; i < copying.Count; i++)
        {
            Assert.IsTrue(copying[i].ProcessedFiles >= copying[i - 1].ProcessedFiles,
                $"ProcessedFiles regressed at index {i}: " +
                $"{copying[i - 1].ProcessedFiles} → {copying[i].ProcessedFiles}");
            Assert.IsTrue(copying[i].ProcessedBytes >= copying[i - 1].ProcessedBytes,
                $"ProcessedBytes regressed at index {i}");
        }

        // Final Copying event reports all files copied (TotalFiles is fixed up-front).
        var lastCopy = copying[^1];
        Assert.AreEqual(3, lastCopy.TotalFiles);
        Assert.AreEqual(3, lastCopy.ProcessedFiles);
        Assert.AreEqual(3 + 5 + 7 /* "{\"x\":1}" */, lastCopy.ProcessedBytes);
    }

    [TestMethod]
    public async Task MergeOverwrite_CopyingEventsReportEachFile()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/a.bin", new byte[] { 1 })
         .WithFile("data/sub/b.bin", new byte[] { 2 })
         .WithFile("c.bin", new byte[] { 3 });

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var sink = CaptureProgress();
        var outcome = await h.RunAsync(sink);
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);


        // Splash uses RelocationProgress.CurrentFile to render the rolling current-file label.
        // Every file we shipped must show up at least once in that field.
        var seen = sink.Events
            .Where(e => e.Phase == RelocationPhase.Copying && !string.IsNullOrEmpty(e.CurrentFile))
            .Select(e => e.CurrentFile!.Replace('\\', '/'))
            .ToHashSet();
        Assert.IsTrue(seen.Contains("data/a.bin"), $"missing data/a.bin in {string.Join(",", seen)}");
        Assert.IsTrue(seen.Contains("data/sub/b.bin"), $"missing data/sub/b.bin in {string.Join(",", seen)}");
        Assert.IsTrue(seen.Contains("c.bin"), $"missing c.bin in {string.Join(",", seen)}");
    }

    [TestMethod]
    public async Task FilesFraction_ReachesOne_AtEndOfCopying()
    {
        using var h = new RelocationTestHarness();
        h.WithFile("data/file1.bin", new byte[] { 1, 2 })
         .WithFile("data/file2.bin", new byte[] { 3, 4 });

        h.WithMarker(new PendingRelocation
        {
            Mode = RelocationMode.MergeOverwrite,
            Target = h.TargetDir,
        });

        var sink = CaptureProgress();
        var outcome = await h.RunAsync(sink);
        Assert.AreEqual(RelocationOutcomeKind.Success, outcome.Kind, outcome.ErrorMessage);


        var lastCopy = sink.Events.LastOrDefault(e => e.Phase == RelocationPhase.Copying);
        Assert.IsNotNull(lastCopy);
        Assert.AreEqual(1.0d, lastCopy!.FilesFraction, 1e-9,
            "splash progress bar relies on FilesFraction reaching 1.0 at end of copying");
    }
}
