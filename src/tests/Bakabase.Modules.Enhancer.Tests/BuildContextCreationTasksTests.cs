using Bakabase.Abstractions.Models.Domain;
using EnhancementRecord = Bakabase.Abstractions.Models.Domain.EnhancementRecord;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Services;
using Bakabase.Modules.Enhancer.Tests.Helpers;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Enhancer.Tests;

/// <summary>
/// Integration tests for <c>EnhancerService.BuildContextCreationTasks</c>.
/// The cycle algorithm itself is unit-tested in <see cref="CycleDetectorTests"/>;
/// here we verify the surrounding orchestration:
/// - dependency edges from <c>EnhancerFullOptions.Requirements</c> are wired
///   into <c>ContextCreationTask.Dependencies</c> / <c>Dependents</c>,
/// - self-edges are skipped before the cycle check (so a real "self" entry
///   in Requirements becomes a no-op edge, not a "X-&gt;X" drop),
/// - when a cycle exists for one resource, only that resource's tasks are
///   dropped and the rest of the batch survives,
/// - tasks for enhancer records already in <c>ContextApplied</c> state are
///   skipped so re-runs don't redo applied work.
///
/// We swap in a fake <see cref="IResourceProfileService"/> so the test drives
/// EnhancerOptions directly without exercising the search-index / profile-match
/// machinery (that's covered elsewhere).
/// </summary>
[TestClass]
public sealed class BuildContextCreationTasksTests
{
    private IServiceProvider _sp = null!;
    private EnhancerService _enhancerService = null!;
    private FakeResourceProfileService _fakeProfile = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _fakeProfile = new FakeResourceProfileService();
        _sp = await TestServiceBuilder.BuildServiceProvider(svc =>
        {
            // Replace the default profile service with our fake so we can
            // drive enhancer options directly per resource.
            svc.AddScoped<IResourceProfileService>(_ => _fakeProfile);
        });
        // EnhancerService is registered as IEnhancerService; the concrete
        // class exposes the internal BuildContextCreationTasks we want.
        _enhancerService = (EnhancerService)_sp.GetRequiredService<IEnhancerService>();
    }

    private static Resource Res(int id) => new() { Id = id, Path = $"/test/{id}", IsFile = true };

    private static EnhancerFullOptions Opts(EnhancerId enhancerId, params EnhancerId[] requirements) =>
        new()
        {
            EnhancerId = (int)enhancerId,
            Requirements = requirements.Length == 0 ? null : requirements.Select(r => (int)r).ToList(),
            // At least one TargetOptions entry with pool/id > 0 is needed,
            // otherwise BuildContextCreationTasks filters TargetOptions out.
            TargetOptions =
            [
                new EnhancerTargetFullOptions
                {
                    PropertyId = 1,
                    PropertyPool = PropertyPool.Reserved,
                    Target = 0,
                }
            ],
        };

    [TestMethod]
    public async Task BuildContextCreationTasks_NoResources_ReturnsEmptyList()
    {
        var tasks = await _enhancerService.BuildContextCreationTasks([], null);
        tasks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task BuildContextCreationTasks_NoEnhancerOptionsForResource_ReturnsEmptyList()
    {
        // The fake returns nothing for this resource, so no tasks emerge.
        var r = Res(10);
        var tasks = await _enhancerService.BuildContextCreationTasks([r], null);
        tasks.Should().BeEmpty();
    }

    [TestMethod]
    public async Task BuildContextCreationTasks_SingleEnhancer_OneTaskPerResource()
    {
        var r = Res(11);
        _fakeProfile.Set(r.Id, [Opts(EnhancerId.Bangumi)]);

        var tasks = await _enhancerService.BuildContextCreationTasks([r], null);

        tasks.Should().HaveCount(1);
        tasks[0].Resource.Id.Should().Be(11);
        tasks[0].Enhancer.Id.Should().Be((int)EnhancerId.Bangumi);
        tasks[0].Dependencies.Should().BeEmpty();
        tasks[0].Dependents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task BuildContextCreationTasks_DependencyWiredBothWays()
    {
        // Tmdb requires Bakabase first. The resulting Tmdb task should have
        // Bakabase as a dependency, and Bakabase should list Tmdb as dependent.
        var r = Res(12);
        _fakeProfile.Set(r.Id,
        [
            Opts(EnhancerId.Bakabase),
            Opts(EnhancerId.Tmdb, EnhancerId.Bakabase),
        ]);

        var tasks = await _enhancerService.BuildContextCreationTasks([r], null);

        tasks.Should().HaveCount(2);
        var tmdb = tasks.Single(t => t.Enhancer.Id == (int)EnhancerId.Tmdb);
        var bakabase = tasks.Single(t => t.Enhancer.Id == (int)EnhancerId.Bakabase);

        tmdb.Dependencies.Should().ContainSingle().Which.Should().Be(bakabase);
        bakabase.Dependents.Should().ContainSingle().Which.Should().Be(tmdb);
        bakabase.Dependencies.Should().BeEmpty();
        tmdb.Dependents.Should().BeEmpty();
    }

    [TestMethod]
    public async Task BuildContextCreationTasks_SelfEdge_IsSkippedNotTreatedAsCycle()
    {
        // An enhancer declaring itself as a requirement used to crash the DFS
        // with "X->X". The pre-DFS filter must drop self-edges so this resource
        // produces a clean task (no Dependencies, no cycle).
        var r = Res(13);
        _fakeProfile.Set(r.Id, [Opts(EnhancerId.Av, EnhancerId.Av)]);

        var tasks = await _enhancerService.BuildContextCreationTasks([r], null);

        tasks.Should().HaveCount(1, "self-edge must not drop the task as a cycle");
        tasks[0].Dependencies.Should().BeEmpty("self-edge must not be recorded as a dependency");
    }

    [TestMethod]
    public async Task BuildContextCreationTasks_RealCycle_DropsAllTasksForThatResourceOnly()
    {
        // Resource 20 has a mutual cycle Bangumi<->Tmdb; resource 21 is clean.
        // The cycle resource must have ALL its tasks dropped, while the
        // unrelated resource's task survives.
        var cyclic = Res(20);
        var clean = Res(21);
        _fakeProfile.Set(cyclic.Id,
        [
            Opts(EnhancerId.Bangumi, EnhancerId.Tmdb),
            Opts(EnhancerId.Tmdb, EnhancerId.Bangumi),
        ]);
        _fakeProfile.Set(clean.Id, [Opts(EnhancerId.Bakabase)]);

        var tasks = await _enhancerService.BuildContextCreationTasks([cyclic, clean], null);

        tasks.Should().HaveCount(1, "only the clean resource's task should survive");
        tasks[0].Resource.Id.Should().Be(clean.Id);
        tasks[0].Enhancer.Id.Should().Be((int)EnhancerId.Bakabase);
    }

    [TestMethod]
    public async Task BuildContextCreationTasks_RestrictedEnhancerIds_FiltersOthersOut()
    {
        // The restrictedEnhancerIds parameter narrows the build to a subset.
        // Anything outside the set is skipped, so dependency edges that would
        // have been recorded against an excluded enhancer simply don't exist.
        var r = Res(30);
        _fakeProfile.Set(r.Id,
        [
            Opts(EnhancerId.Bakabase),
            Opts(EnhancerId.Tmdb, EnhancerId.Bakabase),
        ]);

        var tasks = await _enhancerService.BuildContextCreationTasks(
            [r], new HashSet<int> { (int)EnhancerId.Tmdb });

        tasks.Should().HaveCount(1, "the Bakabase task is filtered out by the restriction");
        var tmdb = tasks.Single();
        tmdb.Enhancer.Id.Should().Be((int)EnhancerId.Tmdb);
        tmdb.Dependencies.Should().BeEmpty(
            "the requirement on Bakabase resolves to no task, because the dependency target was filtered out");
    }

    [TestMethod]
    public async Task BuildContextCreationTasks_RecordInContextApplied_SkipsTaskCreation()
    {
        // EnhancementRecord with Status=ContextApplied means a previous run
        // already wrote out the values; re-building tasks must skip it so the
        // EnhanceAll loop doesn't redo applied work.
        var r = Res(40);
        _fakeProfile.Set(r.Id,
        [
            Opts(EnhancerId.Bangumi),
            Opts(EnhancerId.Bakabase),
        ]);

        // Pre-seed an EnhancementRecord for Bangumi at ContextApplied.
        var recordService = _sp.GetRequiredService<IEnhancementRecordService>();
        await recordService.Add(new EnhancementRecord
        {
            ResourceId = r.Id,
            EnhancerId = (int)EnhancerId.Bangumi,
            Status = EnhancementRecordStatus.ContextApplied,
            ContextCreatedAt = DateTime.Now,
            ContextAppliedAt = DateTime.Now,
        });

        var tasks = await _enhancerService.BuildContextCreationTasks([r], null);

        tasks.Should().HaveCount(1, "only Bakabase task should remain — Bangumi is already applied");
        tasks[0].Enhancer.Id.Should().Be((int)EnhancerId.Bakabase);
    }

}
