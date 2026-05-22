using Bakabase.Modules.HealthScore.Components;
using Bakabase.Modules.HealthScore.Models.Db;

namespace Bakabase.Modules.HealthScore.Tests;

/// <summary>
/// Boundary coverage for HealthScoreProfileHasher: the hash must be stable for
/// score-affecting fields and must ignore the fields documented as excluded
/// (Name, Enabled, Priority, timestamps) so an irrelevant edit does not force
/// a score recompute.
/// </summary>
[TestClass]
public sealed class ProfileHasherTests
{
    private static HealthScoreProfileDbModel Profile() => new()
    {
        Name = "Profile",
        BaseScore = 100m,
        MembershipFilterJson = null,
        RulesJson = null
    };

    [TestMethod]
    public void Compute_IsDeterministic()
    {
        Assert.AreEqual(HealthScoreProfileHasher.Compute(Profile()), HealthScoreProfileHasher.Compute(Profile()));
    }

    [TestMethod]
    public void Compute_ReturnsSha256HexString()
    {
        var hash = HealthScoreProfileHasher.Compute(Profile());
        Assert.AreEqual(64, hash.Length);
        Assert.IsTrue(hash.All(c => "0123456789ABCDEF".Contains(c)));
    }

    [TestMethod]
    public void Compute_BaseScoreChange_ChangesHash()
    {
        Assert.AreNotEqual(
            HealthScoreProfileHasher.Compute(Profile()),
            HealthScoreProfileHasher.Compute(Profile() with { BaseScore = 50m }));
    }

    [TestMethod]
    public void Compute_RulesChange_ChangesHash()
    {
        Assert.AreNotEqual(
            HealthScoreProfileHasher.Compute(Profile()),
            HealthScoreProfileHasher.Compute(Profile() with { RulesJson = "[{}]" }));
    }

    [TestMethod]
    public void Compute_MembershipFilterChange_ChangesHash()
    {
        Assert.AreNotEqual(
            HealthScoreProfileHasher.Compute(Profile()),
            HealthScoreProfileHasher.Compute(Profile() with { MembershipFilterJson = "{}" }));
    }

    [TestMethod]
    public void Compute_ScoreIrrelevantFields_DoNotChangeHash()
    {
        var edited = Profile() with
        {
            Id = 999,
            Name = "Completely Different Name",
            Enabled = true,
            Priority = 42,
            CreatedAt = new DateTime(2000, 1, 1),
            UpdatedAt = new DateTime(2001, 2, 3)
        };
        Assert.AreEqual(
            HealthScoreProfileHasher.Compute(Profile()),
            HealthScoreProfileHasher.Compute(edited));
    }
}
