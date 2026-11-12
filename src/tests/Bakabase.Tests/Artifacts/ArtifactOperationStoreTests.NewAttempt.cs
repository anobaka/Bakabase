using Bakabase.Modules.Workflow.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Tests.Artifacts;

public sealed partial class ArtifactOperationStoreTests
{
    [TestMethod]
    public async Task New_attempt_proof_is_returned_only_by_the_generation_changing_claim()
    {
        var request = Request("new-generation-proof");
        var first = await _store.ClaimAsync(request, "owner", LeaseTime);
        Assert.IsTrue(first.StartedNewAttempt);
        var renewed = await _store.ClaimAsync(request, "owner", LeaseTime);
        Assert.AreEqual(first.Lease, renewed.Lease);
        Assert.IsFalse(renewed.StartedNewAttempt, "Renewing a live operation must not authorize allocating a second workspace.");

        await _db.Set<ArtifactOperationDbModel>().Where(o => o.Id == request.OperationKey)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.LeaseUntil, DateTime.UtcNow.AddMinutes(-1)));
        var resumed = await _store.ClaimAsync(request, "takeover", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.Acquired, resumed.Status);
        Assert.IsFalse(resumed.StartedNewAttempt, "An expired running lease keeps the same attempt and its pinned files.");
        Assert.AreEqual(1, (await _store.GetOperationAsync(request.OperationKey))!.Attempt);

        Assert.IsTrue(await _store.FailAsync(resumed.Lease!, "failed attempt"));
        var denied = await _store.ClaimAsync(request with {AllowRetry = false}, "owner", LeaseTime);
        Assert.AreEqual(ArtifactClaimStatus.NeedsReconciliation, denied.Status);
        Assert.IsFalse(denied.StartedNewAttempt);
        var retry = await _store.ClaimAsync(request, "owner", LeaseTime);
        Assert.IsTrue(retry.StartedNewAttempt);
        Assert.AreEqual(2, (await _store.GetOperationAsync(request.OperationKey))!.Attempt);
        var sameAttempt = await _store.ClaimSameAttemptAsync(request, 2, "owner", LeaseTime);
        Assert.IsFalse(sameAttempt.StartedNewAttempt);
    }

    [TestMethod]
    public async Task Concurrent_failed_attempt_retries_grant_only_one_new_allocation_proof()
    {
        var request = Request("retry-race-proof");
        var initial = await _store.ClaimAsync(request, "previous", LeaseTime);
        Assert.IsTrue(await _store.FailAsync(initial.Lease!, "failed attempt"));
        await using var otherDb = OpenDb(); var other = Store(otherDb);
        var claims = await Task.WhenAll(_store.ClaimAsync(request, "one", LeaseTime),
            other.ClaimAsync(request, "two", LeaseTime));
        Assert.AreEqual(1, claims.Count(c => c.Status == ArtifactClaimStatus.Acquired));
        Assert.AreEqual(1, claims.Count(c => c.StartedNewAttempt));
        Assert.IsTrue(claims.Single(c => c.StartedNewAttempt).Lease != null);
        Assert.IsFalse(claims.Single(c => c.Status != ArtifactClaimStatus.Acquired).StartedNewAttempt);
        Assert.AreEqual(2, (await _store.GetOperationAsync(request.OperationKey))!.Attempt);
    }
}
