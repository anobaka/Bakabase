using System.Security.Cryptography;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactOperationStoreTests
{
    // Advance the monotonic interval between reads while retaining the real database clock
    // for lease ownership and compare-and-swap. No wall-clock wait or large fixture is required.
    private sealed class AdoptionClock(Action<int> onSample) : TimeProvider
    {
        private int samples;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp()
        {
            onSample(++samples);
            return TimeSpan.FromMinutes(11).Ticks * samples;
        }
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Empty_file_does_not_report_progress_before_stable_metadata_or_after_cancellation(bool cancel)
    {
        var input = await Files("empty-metadata");
        var file = input.Files.Single();
        await File.WriteAllBytesAsync(file, []);
        using var cancellation = new CancellationTokenSource();
        var reading = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = 0;
        var capture = ArtifactManifestFiles.CaptureAsync(input.RootDirectory, [file],
            path => new FileInfo(cancel ? path : path + ".missing"), async (stream, token) =>
            {
                reading.SetResult();
                await release.Task.WaitAsync(token);
                return await ArtifactContentHash.ComputeAsync(stream, token);
            }, cancellation.Token, onEmptyFileVerified: _ => { completed++; return ValueTask.CompletedTask; });
        await reading.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(0, completed);
        if (cancel) cancellation.Cancel();
        release.SetResult();
        if (cancel) await Assert.ThrowsAsync<OperationCanceledException>(async () => await capture);
        else await Assert.ThrowsExactlyAsync<IOException>(async () => await capture);
        Assert.AreEqual(0, completed);
    }

    [DataTestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Adoption_renews_within_a_large_file_or_across_verified_empty_files_and_recovers_an_expired_lease(bool emptyFiles)
    {
        var request = Request("long-download");
        var input = await Files("long-download");
        byte[] content = emptyFiles ? [] : RandomNumberGenerator.GetBytes(2 * ArtifactContentHash.BufferSize + 17);
        if (emptyFiles)
            input = input with {Files = new[] {"a", "b", "c"}.Select(name => Path.Combine(input.RootDirectory, name)).ToArray()};
        foreach (var file in input.Files) await File.WriteAllBytesAsync(file, content);
        var former = await _store.ClaimAsync(request, "workflow:previous", LeaseTime);
        await using var observer = OpenDb();
        var live = observer.Set<ArtifactOperationDbModel>().Where(o => o.Id == request.OperationKey);
        await live.ExecuteUpdateAsync(set => set.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));

        var extensions = 0;
        var clock = new AdoptionClock(sample =>
        {
            if (sample == 1) return;
            if (sample % 2 == 0)
                live.ExecuteUpdate(set => set.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddSeconds(10)));
            else
            {
                Assert.IsTrue(live.AsNoTracking().Single().LeaseUntil > DateTime.UtcNow.AddMinutes(25));
                extensions++;
            }
        });
        var adopter = new ArtifactOperationStore(_db, () => _appData, clock: clock);
        var artifact = await adopter.AdoptAsync(request, input, "workflow:previous");
        if (emptyFiles) Assert.AreEqual(3, extensions, "Only distinct, fully verified empty files can make progress.");
        else Assert.IsTrue(extensions >= 3, "Renewals must occur within one file, including a partial final read.");
        var digest = Convert.ToHexStringLower(SHA256.HashData(content));
        Assert.AreEqual(input.Files.Count, artifact.Manifest.Entries.Count);
        Assert.IsTrue(artifact.Manifest.Entries.All(entry => entry.Length == content.Length && entry.Sha256 == digest));
        var completed = await adopter.GetOperationAsync(request.OperationKey);
        Assert.AreEqual(1, completed!.Attempt);
        Assert.AreNotEqual(former.Lease!.Token, completed.LeaseToken);
        Assert.AreEqual(ArtifactOperationState.Completed, completed.State);
        Assert.AreEqual(1, await observer.Set<ArtifactDbModel>().CountAsync());
        Assert.IsFalse(await adopter.RenewAsync(former.Lease, LeaseTime));
        await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => adopter.CompleteAsync(former.Lease, input));
    }

    [DataTestMethod]
    [DataRow("expired")]
    [DataRow("replaced")]
    [DataRow("replacedAndCompleted")]
    [DataRow("cancelled")]
    public async Task Adoption_aborts_on_lost_ownership_or_cancellation_without_publishing(string scenario)
    {
        var input = await Files("read-checkpoint");
        var request = Request("read-checkpoint");
        await using var observer = OpenDb();
        var competing = Store(observer);
        var live = observer.Set<ArtifactOperationDbModel>().Where(o => o.Id == request.OperationKey);
        using var cancellation = new CancellationTokenSource();
        ArtifactOperationLease? replacement = null;
        var clock = new AdoptionClock(sample =>
        {
            if (sample != 2) return;
            if (scenario == "cancelled") { cancellation.Cancel(); return; }
            live.ExecuteUpdate(set => set.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
            if (scenario is "replaced" or "replacedAndCompleted")
                replacement = competing.ClaimAsync(request, "other-reader", LeaseTime).GetAwaiter().GetResult().Lease;
            if (scenario == "replacedAndCompleted")
                competing.CompleteAsync(replacement!, input).GetAwaiter().GetResult();
        });
        var adopter = new ArtifactOperationStore(_db, () => _appData, clock: clock);
        async Task Attempt() => await adopter.AdoptAsync(request, input, "reader", cancellation.Token);
        if (scenario == "cancelled") await Assert.ThrowsAsync<OperationCanceledException>(Attempt);
        else await Assert.ThrowsExactlyAsync<InvalidOperationException>(Attempt);
        Assert.IsTrue(File.Exists(input.Files.Single()));
        var pending = await competing.GetOperationAsync(request.OperationKey);
        if (scenario == "replacedAndCompleted")
        {
            Assert.AreEqual(1, await observer.Set<ArtifactDbModel>().CountAsync());
            Assert.AreEqual(replacement!.Token, pending!.LeaseToken);
            Assert.AreEqual(ArtifactOperationState.Completed, pending.State);
            Assert.IsNotNull(pending.OutputArtifactId);
            return;
        }
        Assert.AreEqual(0, await observer.Set<ArtifactDbModel>().CountAsync());
        Assert.AreEqual(ArtifactOperationState.Running, pending!.State);
        Assert.IsNull(pending.OutputArtifactId);
        if (replacement != null)
        {
            Assert.AreEqual(replacement.Token, pending.LeaseToken);
            Assert.IsTrue(await competing.RenewAsync(replacement, LeaseTime));
        }
        var restored = await competing.AdoptAsync(request, input, replacement == null ? "reader" : "other-reader");
        Assert.AreEqual(restored.Id, (await competing.GetOperationAsync(request.OperationKey))!.OutputArtifactId);
        Assert.AreEqual(1, await observer.Set<ArtifactDbModel>().CountAsync());
    }
}
