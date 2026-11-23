using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactWorkspaceMaintenanceTests
{
    [TestMethod]
    public async Task An_inflight_verification_can_exceed_the_idle_budget_without_starting_the_next_candidate()
    {
        var fixtures = new[] {await Fixture(), await Fixture()};
        var pause = new VerificationPause();
        var clock = new CleanupClock();
        await using var services = LifecycleServices(store => new PausedVerificationStore(store, pause));
        using var cleanup = new ArtifactWorkspaceLifecycleCleanup(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ArtifactWorkspaceLifecycleCleanup>.Instance, clock);
        var pass = cleanup.RunIdlePassAsync(default);
        var started = await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromMinutes(6));
        Assert.IsFalse(started.Token.IsCancellationRequested, "Elapsed idle budget must not abandon the current manifest verification.");
        Assert.IsFalse(pass.IsCompleted);
        pause.Continue.TrySetResult();
        await pass.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.AreEqual(1, fixtures.Count(f => !File.Exists(f.Intermediate)), "A second candidate must wait for another idle pass.");
        Assert.IsFalse(File.Exists(fixtures.Single(f => f.Artifact.Id == started.ArtifactId).Intermediate));
        Assert.IsTrue(fixtures.All(f => File.Exists(f.Original) && File.Exists(f.State) && File.Exists(f.Artifact.Files.Single())));

        await cleanup.RunIdlePassAsync(default);
        Assert.IsTrue(fixtures.All(f => !File.Exists(f.Intermediate)), "The budget must not skip the unstarted candidate's cursor.");
    }

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task An_over_budget_verification_only_yields_to_service_shutdown(bool shutdown)
    {
        var fixture = await Fixture();
        var pause = new VerificationPause();
        var clock = new CleanupClock();
        await using var services = LifecycleServices(store => new PausedVerificationStore(store, pause));
        using var cleanup = new ArtifactWorkspaceLifecycleCleanup(services.GetRequiredService<IServiceScopeFactory>(),
            NullLogger<ArtifactWorkspaceLifecycleCleanup>.Instance, clock);
        using var stopping = new CancellationTokenSource();
        var pass = cleanup.RunIdlePassAsync(stopping.Token);
        var started = await pause.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        clock.Advance(TimeSpan.FromMinutes(6));
        if (shutdown)
        {
            stopping.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => pass.WaitAsync(TimeSpan.FromSeconds(10)));
        }
        else
        {
            _db.Add(new DownloadTaskDbModel {Id = 15, Key = "new-download", DownloadPath = Path.Combine(_root, "unrelated-download"), Status = DownloadTaskDbModelStatus.InProgress});
            await _db.SaveChangesAsync();
            Assert.IsFalse(started.Token.IsCancellationRequested);
            pause.Continue.TrySetResult();
            await pass.WaitAsync(TimeSpan.FromSeconds(10));
            await _db.Set<DownloadTaskDbModel>().ExecuteUpdateAsync(s => s.SetProperty(d => d.Status, DownloadTaskDbModelStatus.Complete));
        }
        Assert.AreEqual(shutdown, started.Token.IsCancellationRequested);
        Assert.AreEqual(shutdown, File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
        // Cancellation must release the maintenance fence so a later idle pass can succeed.
        Assert.IsTrue((await _maintenance.ReclaimAsync([fixture.Artifact.Id], default).WaitAsync(TimeSpan.FromSeconds(10))).Single().Completed);
    }


    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Automatic_cleanup_only_opens_candidate_payloads_after_acquiring_maintenance(bool changed)
    {
        var fixture = await Fixture();
        using var held = new FileStream(fixture.Intermediate, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var pause = new VerificationPause();
        pause.Continue.SetResult();
        var entered = false;
        await using var services = LifecycleServices(store => new PausedVerificationStore(store, pause, () =>
        {
            entered = true;
            if (changed)
            {
                var first = held.ReadByte();
                Assert.IsTrue(first >= 0);
                held.Position = 0;
                held.WriteByte((byte)(first ^ 1));
            }
            held.Dispose();
        }));
        using var scope = services.CreateScope();
        var cleanup = scope.ServiceProvider.GetRequiredService<ArtifactWorkspaceMaintenance>();
        var outcome = (await cleanup.ReclaimAsync([fixture.Artifact.Id], default)).Single();
        Assert.IsTrue(entered, outcome.Reason);
        Assert.IsTrue(outcome.Completed, outcome.Reason);
        Assert.AreEqual(changed ? 0 : 1, outcome.RemovedFiles);
        Assert.AreEqual(changed, File.Exists(fixture.Intermediate));
        Assert.IsTrue(File.Exists(fixture.Original));
        Assert.IsTrue(File.Exists(fixture.Artifact.Files.Single()));
    }

    private sealed class CleanupClock : TimeProvider
    {
        private long timestamp;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => Interlocked.Read(ref timestamp);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref timestamp, elapsed.Ticks);
    }

    private sealed class VerificationPause
    {
        public readonly TaskCompletionSource<(string ArtifactId, CancellationToken Token)> Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public readonly TaskCompletionSource Continue = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Calls;
    }

    // Keep the real registration, ownership checks, hashing and deletion. Pause only the
    // first content verification to model a slow storage read without generating huge files.
    private sealed class PausedVerificationStore(IArtifactOperationStore inner, VerificationPause pause, Action? enteringMaintenance = null) : IArtifactOperationStore
    {
        public async Task<ArtifactRecord?> ResolveByIdAsync(string id, CancellationToken ct = default)
        {
            if (Interlocked.Increment(ref pause.Calls) == 1)
            {
                pause.Started.TrySetResult((id, ct));
                await pause.Continue.Task.WaitAsync(ct);
            }
            return await inner.ResolveByIdAsync(id, ct);
        }
        public Task<IAsyncDisposable> AcquireMaintenanceAsync(CancellationToken ct = default)
        {
            enteringMaintenance?.Invoke();
            return inner.AcquireMaintenanceAsync(ct);
        }
        public Task<bool> HasRegisteredFilesAsync(IReadOnlyList<string> files, CancellationToken ct = default) => inner.HasRegisteredFilesAsync(files, ct);
        public Task<ArtifactRecord?> ResolveAsync(string intentKey, CancellationToken ct = default) => inner.ResolveAsync(intentKey, ct);
        public Task<ArtifactRecord?> GetArtifactAsync(string id, CancellationToken ct = default) => inner.GetArtifactAsync(id, ct);
        public Task<ArtifactOperationDbModel?> GetOperationAsync(string id, CancellationToken ct = default) => inner.GetOperationAsync(id, ct);
        public Task<ArtifactOperationClaim> ClaimAsync(ArtifactOperationRequest request, string owner, TimeSpan lease, CancellationToken ct = default) => inner.ClaimAsync(request, owner, lease, ct);
        public Task<bool> RenewAsync(ArtifactOperationLease lease, TimeSpan duration, CancellationToken ct = default) => inner.RenewAsync(lease, duration, ct);
        public Task<ArtifactRecord> CompleteAsync(ArtifactOperationLease lease, ArtifactRegistration registration, CancellationToken ct = default) => inner.CompleteAsync(lease, registration, ct);
        public Task<bool> FailAsync(ArtifactOperationLease lease, string error, bool outcomeUnknown = false, CancellationToken ct = default) => inner.FailAsync(lease, error, outcomeUnknown, ct);
        public Task<ArtifactRecord> AdoptAsync(ArtifactOperationRequest request, ArtifactRegistration registration, string owner, CancellationToken ct = default) => inner.AdoptAsync(request, registration, owner, ct);
        public Task<bool> AttachConsumerAsync(string id, string key, TimeSpan duration, CancellationToken ct = default) => inner.AttachConsumerAsync(id, key, duration, ct);
        public Task<bool> RenewExistingConsumerAsync(string id, string key, TimeSpan duration, CancellationToken ct = default) => inner.RenewExistingConsumerAsync(id, key, duration, ct);
        public Task<bool> PinConsumerAsync(string id, string key, string kind, string owner, CancellationToken ct = default) => inner.PinConsumerAsync(id, key, kind, owner, ct);
        public Task ReleaseConsumerAsync(string id, string key, CancellationToken ct = default) => inner.ReleaseConsumerAsync(id, key, ct);
        public Task<bool> HasActiveConsumersAsync(string id, CancellationToken ct = default) => inner.HasActiveConsumersAsync(id, ct);
        public Task<bool> TryQuarantineAsync(string id, CancellationToken ct = default) => inner.TryQuarantineAsync(id, ct);
        public Task SetLocationAsync(string id, string root, ArtifactLocationState state, CancellationToken ct = default) => inner.SetLocationAsync(id, root, state, ct);
    }
}
