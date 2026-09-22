using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Queries;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Modules.Federation.Tests.Queries;

[TestClass]
public class CoordinatorSchedulingTests
{
    [TestMethod]
    public async Task SynchronousLocalPreparationMustNotPreventTheRemotePreparationFromEntering()
    {
        var watchdog = TimeSpan.FromSeconds(5);
        var limits = new FederationQueryLimits { BlockSize = 2, MaxBlockSize = 4 };
        using var local = new TestPeer("local", limits, null, "a");
        using var remote = new TestPeer("remote", limits, null, "b");
        using var releaseLocal = new ManualResetEventSlim();
        using var cancellation = new CancellationTokenSource();
        var localEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var remoteEntered = false;
        var localObservedRemote = false;
        var localClient = new SynchronousPrefixClient(local.Client, token =>
        {
            localEntered.TrySetResult();
            // Model SQLite's synchronous async-method prefix. Only entry into
            // the other preparation releases this gate during the assertion.
            // The watchdog prevents a broken dispatcher from deadlocking CI.
            localObservedRemote = releaseLocal.Wait(watchdog, token) && Volatile.Read(ref remoteEntered);
        });
        var remoteClient = new SynchronousPrefixClient(remote.Client, _ =>
        {
            Volatile.Write(ref remoteEntered, true);
            releaseLocal.Set();
        });
        using var coordinator = new FederatedQueryCoordinator(new Targets(
            local.Target with { Client = localClient }, remote.Target with { Client = remoteClient }), limits);

        // The invocation itself may block before returning a Task. Give it a
        // dedicated thread so the test can always reach its cleanup watchdog.
        var pending = Task.Factory.StartNew(() => coordinator.CreateAsync("ui", new()
        {
            NodeIds = ["local", "remote"], PageSize = 2
        }, cancellation.Token), CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default).Unwrap();
        FederatedQueryPage? page = null;
        try
        {
            await localEntered.Task.WaitAsync(watchdog);
            page = await pending.WaitAsync(watchdog + TimeSpan.FromSeconds(2));
            Assert.IsTrue(localObservedRemote,
                "Remote preparation could not enter until the local synchronous prefix's watchdog released it.");
            Assert.IsTrue(page.CoverageComplete);
            Assert.AreEqual(2L, page.TotalWithinParticipants);
            Assert.AreEqual(1, localClient.CreateCalls);
            Assert.AreEqual(1, remoteClient.CreateCalls);
        }
        finally
        {
            // Also release the prefix after assertion/cancellation failures;
            // no test failure may leave a blocked worker or retained session.
            releaseLocal.Set();
            cancellation.Cancel();
            try { page ??= await pending.WaitAsync(watchdog); }
            catch (OperationCanceledException) { }
            if (page != null) await coordinator.ReleaseAsync("ui", page.SessionId);
        }
    }

    [TestMethod]
    public async Task PreCancelledCreationDoesNotResolveNodesAndReturnsOwnerQuota()
    {
        var limits = new FederationQueryLimits { MaxSessionsPerOwner = 1, MaxCoordinatorSessions = 1 };
        using var peer = new TestPeer("local", limits, null, "a");
        var targets = new Targets(peer.Target);
        using var coordinator = new FederatedQueryCoordinator(targets, limits);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => coordinator.CreateAsync("ui",
            new() { NodeIds = ["local"], PageSize = 1 }, cancellation.Token));
        Assert.AreEqual(0, targets.ResolveCalls);
        Assert.AreEqual(0, peer.Client.ReleaseCalls);

        var page = await coordinator.CreateAsync("ui", new() { NodeIds = ["local"], PageSize = 1 });
        Assert.IsTrue(page.CoverageComplete);
        await coordinator.ReleaseAsync("ui", page.SessionId);
        Assert.AreEqual(1, targets.ResolveCalls);
        Assert.AreEqual(1, peer.Client.ReleaseCalls);
    }

    [TestMethod]
    public async Task CancellationWhilePreparationCapacityIsOccupiedDoesNotExportTheQueuedNode()
    {
        var watchdog = TimeSpan.FromSeconds(5);
        var limits = new FederationQueryLimits
        {
            MaxConcurrentPreparations = 1, MaxSessionsPerOwner = 1,
            MaxCoordinatorSessions = 2, MaxSnapshotsPerGrant = 1,
            // One preparation fits, but a leaked preparation workspace would
            // prevent the next one from obtaining its parsing reservation.
            MaxCoordinatorBytes = 11 * 1024 * 1024
        };
        using var active = new TestPeer("active", limits, null, "a");
        using var queued = new TestPeer("queued", limits, null, "b");
        var activeEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseActive = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        active.Client.CreationGate = releaseActive.Task;
        var activeClient = new SynchronousPrefixClient(active.Client, _ => activeEntered.TrySetResult());
        var queuedClient = new SynchronousPrefixClient(queued.Client, _ => { });
        using var coordinator = new FederatedQueryCoordinator(new Targets(
            active.Target with { Client = activeClient }, queued.Target with { Client = queuedClient }), limits);
        using var activeCancellation = new CancellationTokenSource();
        using var queuedCancellation = new CancellationTokenSource();
        var activeTask = coordinator.CreateAsync("active-ui", new() { NodeIds = ["active"], PageSize = 1 },
            activeCancellation.Token);
        Task<FederatedQueryPage>? queuedTask = null;
        FederatedQueryPage? activePage = null;
        FederatedQueryPage? queuedPage = null;
        try
        {
            await activeEntered.Task.WaitAsync(watchdog);
            // The single shared preparation permit remains held by active.
            queuedTask = coordinator.CreateAsync("queued-ui", new() { NodeIds = ["queued"], PageSize = 1 },
                queuedCancellation.Token);
            queuedCancellation.Cancel();
            await Assert.ThrowsExceptionAsync<OperationCanceledException>(() => queuedTask.WaitAsync(watchdog));
            Assert.AreEqual(0, queuedClient.CreateCalls);
            Assert.AreEqual(0, queued.Client.ReleaseCalls);

            releaseActive.SetResult();
            activePage = await activeTask.WaitAsync(watchdog);
            await coordinator.ReleaseAsync("active-ui", activePage.SessionId);
            activePage = null;
            // This requires the shared permit, queued owner's creation quota
            // and the active preparation's workspace to have been returned.
            queuedPage = await coordinator.CreateAsync("queued-ui",
                new() { NodeIds = ["queued"], PageSize = 1 }).WaitAsync(watchdog);
            Assert.IsTrue(queuedPage.CoverageComplete);
            Assert.AreEqual(1, queuedClient.CreateCalls);
            Assert.AreEqual(1, active.Client.ReleaseCalls);
        }
        finally
        {
            releaseActive.TrySetResult();
            activeCancellation.Cancel();
            queuedCancellation.Cancel();
            try { activePage ??= await activeTask.WaitAsync(watchdog); }
            catch (OperationCanceledException) { }
            if (queuedTask != null)
                try { await queuedTask.WaitAsync(watchdog); }
                catch (OperationCanceledException) { }
            if (activePage != null) await coordinator.ReleaseAsync("active-ui", activePage.SessionId);
            if (queuedPage != null) await coordinator.ReleaseAsync("queued-ui", queuedPage.SessionId);
        }
        Assert.AreEqual(1, queued.Client.ReleaseCalls);
    }

    private sealed class Targets(params PeerSearchTarget[] targets) : IPeerSearchTargetResolver
    {
        private int _resolveCalls;
        public int ResolveCalls => Volatile.Read(ref _resolveCalls);
        public Task<PeerSearchTarget> ResolveAsync(string nodeId, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _resolveCalls);
            return Task.FromResult(targets.Single(target => target.NodeId == nodeId));
        }
    }

    private sealed class SynchronousPrefixClient(IPeerSearchClient inner, Action<CancellationToken> prefix)
        : IPeerSearchClient
    {
        public int CreateCalls { get; private set; }

        public Task<NodeQueryBlock> CreateAsync(NodeExportQuery query, CancellationToken cancellationToken)
        {
            CreateCalls++;
            prefix(cancellationToken);
            return inner.CreateAsync(query, cancellationToken);
        }

        public Task<NodeQueryBlock> ReadAsync(string snapshotId, string cursor, CancellationToken cancellationToken) =>
            inner.ReadAsync(snapshotId, cursor, cancellationToken);
        public Task ValidateAsync(string snapshotId, CancellationToken cancellationToken) =>
            inner.ValidateAsync(snapshotId, cancellationToken);
        public Task ReleaseAsync(string snapshotId, CancellationToken cancellationToken) =>
            inner.ReleaseAsync(snapshotId, cancellationToken);
    }
}
