using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Enhancer.Tests;

/// <summary>
/// Pins the dedup contract of <c>EnhancementService.AddRange</c>. The method
/// is the single write path for enhancer-produced raw values; misunderstanding
/// what collapses (and what doesn't) silently loses scraper output.
///
/// **Contract:** dedup key is the DB <c>Key</c> field, populated by
/// <c>FillKey</c> as <c>"{ResourceId}:{EnhancerId}:{Target}:{DynamicTarget}"</c>.
/// ValueType and Value are intentionally NOT part of the key — a single
/// (R, E, T, DT) is canonical and downstream property writes need exactly
/// one value per target. Callers wanting multiple values per target must
/// disambiguate via DynamicTarget.
/// </summary>
[TestClass]
public sealed class AddRangeDedupTests
{
    private IServiceProvider _sp = null!;
    private IEnhancementService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IEnhancementService>();
    }

    private static Enhancement Make(int resourceId, int enhancerId, int target,
        string? dynamicTarget = null, object? value = null,
        StandardValueType valueType = StandardValueType.String) =>
        new()
        {
            ResourceId = resourceId,
            EnhancerId = enhancerId,
            Target = target,
            DynamicTarget = dynamicTarget,
            Value = value ?? "v",
            ValueType = valueType,
        };

    [TestMethod]
    public async Task AddRange_SameKey_CollapsesToFirst()
    {
        // Two entries identical down to the Value — they share the same
        // composite Key (R:E:T:DT). One row, taken from the first input.
        var a = Make(1, 100, 7, value: "first");
        var b = Make(1, 100, 7, value: "second");

        await _service.AddRange([a, b]);

        var stored = await _service.GetAll(x => x.ResourceId == 1);
        stored.Should().HaveCount(1);
        stored[0].Value.Should().Be("first",
            "dedup keeps the first entry by composite key; later duplicates are dropped");
    }

    [TestMethod]
    public async Task AddRange_SameKeyDifferentValues_KeepsFirstByDesign()
    {
        // Two scrapers producing different cover URLs for the same target
        // collapse to one row, keeping the first by input order. The second
        // is dropped as redundant signal — a single (R, E, T, DT) is
        // canonical and downstream property writes need exactly one value
        // per target. Callers needing multiple per-target values must use
        // DynamicTarget to disambiguate.
        var poster1 = Make(1, 100, 5, value: "https://a.example/cover.jpg");
        var poster2 = Make(1, 100, 5, value: "https://b.example/cover.jpg");

        await _service.AddRange([poster1, poster2]);

        var stored = await _service.GetAll(x => x.ResourceId == 1);
        stored.Should().HaveCount(1, "single composite-Key row per (R, E, T, DT)");
        stored[0].Value.Should().Be("https://a.example/cover.jpg",
            "input order decides which value wins — the rest are dropped as redundant");
    }

    [TestMethod]
    public async Task AddRange_DifferentResourceIds_NotDeduped()
    {
        var r1 = Make(1, 100, 7, value: "v1");
        var r2 = Make(2, 100, 7, value: "v2");

        await _service.AddRange([r1, r2]);

        var stored = await _service.GetAll(null);
        stored.Should().HaveCount(2);
        stored.Select(x => x.ResourceId).Should().BeEquivalentTo(new[] { 1, 2 });
    }

    [TestMethod]
    public async Task AddRange_DifferentEnhancerIds_NotDeduped()
    {
        var e1 = Make(1, 100, 7, value: "v1");
        var e2 = Make(1, 200, 7, value: "v2");

        await _service.AddRange([e1, e2]);

        var stored = await _service.GetAll(x => x.ResourceId == 1);
        stored.Should().HaveCount(2);
        stored.Select(x => x.EnhancerId).Should().BeEquivalentTo(new[] { 100, 200 });
    }

    [TestMethod]
    public async Task AddRange_DifferentTargets_NotDeduped()
    {
        var t1 = Make(1, 100, 5, value: "v1");
        var t2 = Make(1, 100, 6, value: "v2");

        await _service.AddRange([t1, t2]);

        var stored = await _service.GetAll(x => x.ResourceId == 1);
        stored.Should().HaveCount(2);
        stored.Select(x => x.Target).Should().BeEquivalentTo(new[] { 5, 6 });
    }

    [TestMethod]
    public async Task AddRange_DifferentDynamicTargets_NotDeduped()
    {
        // Dynamic targets (e.g. JSON-path-style sub-fields) carry distinct
        // semantics under the same Target — they must NOT collapse.
        var dt1 = Make(1, 100, 7, dynamicTarget: "title", value: "Hello");
        var dt2 = Make(1, 100, 7, dynamicTarget: "subtitle", value: "World");

        await _service.AddRange([dt1, dt2]);

        var stored = await _service.GetAll(x => x.ResourceId == 1);
        stored.Should().HaveCount(2);
        stored.Select(x => x.DynamicTarget).Should().BeEquivalentTo(new[] { "title", "subtitle" });
    }

    [TestMethod]
    public async Task AddRange_PopulatesIdsBackOntoDomainObjects()
    {
        // AddRange writes Ids back to the input Enhancement objects so callers
        // (e.g. EnhancerService.ApplyEnhancementsToResources) can reference
        // them in subsequent updates without re-querying.
        var e = Make(1, 100, 7);
        await _service.AddRange([e]);

        e.Id.Should().BeGreaterThan(0, "Id must be assigned after persistence");
    }

    [TestMethod]
    public async Task AddRange_ReplacesPreviousRowsWithSameKey()
    {
        // The method does `RemoveAll(x => keys.Contains(x.Key))` before insert,
        // so re-adding with the same composite key OVERWRITES rather than
        // duplicates. A new row Id is issued each time.
        var first = Make(1, 100, 7, value: "v1");
        await _service.AddRange([first]);
        var oldId = first.Id;

        var second = Make(1, 100, 7, value: "v2");
        await _service.AddRange([second]);

        var stored = await _service.GetAll(x => x.ResourceId == 1);
        stored.Should().HaveCount(1, "previous row with the same Key was removed before insert");
        stored[0].Value.Should().Be("v2");
        second.Id.Should().NotBe(oldId, "row is a new insert, so its Id differs from the prior generation");
    }

    [TestMethod]
    public async Task AddRange_EmptyInput_NoOp()
    {
        // Defensive: callers can pass empty lists (e.g. when an enhancer
        // returns no results). Must not throw or touch the DB.
        await _service.AddRange([]);

        var stored = await _service.GetAll(null);
        stored.Should().BeEmpty();
    }
}
