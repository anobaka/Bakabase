using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Tests.Helpers;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using EnhancementRecord = Bakabase.Abstractions.Models.Domain.EnhancementRecord;
using BangumiTarget = Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi.BangumiEnhancerTarget;

namespace Bakabase.Modules.Enhancer.Tests;

/// <summary>
/// Coverage for <c>EnhancerService.ApplyEnhancementsToResources</c> — the
/// single write path that turns enhancer-produced raw values into property
/// rows. The early tests pin the defensive contract at the entry (empty /
/// nonexistent input). The happy-path tests pin the actual writes against
/// the two real storage backends: <c>ReservedPropertyValue</c> table for
/// Reserved-pool targets, and <c>CustomPropertyValue</c> table for Custom.
///
/// Targets used are sourced from real <c>EnhancerTarget</c> metadata
/// (<c>Bangumi.Introduction</c> / <c>Bangumi.Name</c>) so the descriptor /
/// converter lookups exercise real code rather than a stubbed registration.
/// </summary>
[TestClass]
public sealed class ApplyEnhancementsToResourcesTests
{
    private IServiceProvider _sp = null!;
    private IEnhancerService _enhancerService = null!;
    private IEnhancementService _enhancementService = null!;
    private IEnhancementRecordService _recordService = null!;
    private IResourceService _resourceService = null!;
    private ICustomPropertyService _propertyService = null!;
    private ICustomPropertyValueService _propertyValueService = null!;
    private IReservedPropertyValueService _reservedValueService = null!;
    private FakeResourceProfileService _fakeProfile = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _fakeProfile = new FakeResourceProfileService();
        _sp = await TestServiceBuilder.BuildServiceProvider(svc =>
        {
            svc.AddScoped<IResourceProfileService>(_ => _fakeProfile);
        });
        _enhancerService = _sp.GetRequiredService<IEnhancerService>();
        _enhancementService = _sp.GetRequiredService<IEnhancementService>();
        _recordService = _sp.GetRequiredService<IEnhancementRecordService>();
        _resourceService = _sp.GetRequiredService<IResourceService>();
        _propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        _propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
        _reservedValueService = _sp.GetRequiredService<IReservedPropertyValueService>();
    }

    #region defensive entry contract

    [TestMethod]
    public async Task ApplyEnhancementsToResources_EmptyInput_NoOp()
    {
        await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>>(),
            new List<Enhancement>(),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task ApplyEnhancementsToResources_NonExistentResource_EmptyEnhancementList_DoesNotThrow()
    {
        // Caller-side filters can leave us with resource IDs that no longer
        // exist (e.g. resource deleted between scrape and apply). When the
        // accompanying enhancement list is empty, nothing should throw.
        await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>> { { 99_999, [1, 2, 3] } },
            new List<Enhancement>(),
            CancellationToken.None);
    }

    [TestMethod]
    public async Task ApplyEnhancementsToResources_InputContainsUnpersistedEnhancement_Throws_KnownGap()
    {
        // KNOWN BEHAVIOR: the method unconditionally calls
        // `_enhancementService.UpdateRange(enhancements)` at the end without
        // filtering rows whose Id is 0 (never persisted). Real callers always
        // go through AddRange first, so this isn't hit in production today —
        // pinned here so a future refactor that loosens the invariant trips
        // this test rather than silently corrupting state.
        var unpersisted = new Enhancement
        {
            ResourceId = 99_999, EnhancerId = 1, Target = 1, Value = "x",
        };

        var act = async () => await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>> { { 99_999, [1] } },
            [unpersisted],
            CancellationToken.None);

        await act.Should().ThrowAsync<Exception>();
    }

    [TestMethod]
    public async Task ApplyEnhancementsToResources_EmptyInput_RespectsCancellationLazily()
    {
        // Cancellation is only checked inside the per-enhancement foreach;
        // with empty input the checkpoint is never reached, so a pre-cancelled
        // token is a no-op. Pinned so we notice if cancellation is tightened
        // to the entry.
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var act = async () => await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>>(),
            new List<Enhancement>(),
            cts.Token);

        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Reserved-pool writes

    [TestMethod]
    public async Task ApplyEnhancementsToResources_ReservedIntroductionTarget_WritesReservedPropertyValue()
    {
        // Bangumi.Introduction is StandardValueType.String, PropertyType.MultilineText,
        // reservedPropertyCandidate=Introduction, no converter — the cleanest
        // converter-free path to test a Reserved-pool write end-to-end.
        var resourceId = await CreateResourceAsync();

        // Pre-persist the enhancement so ApplyEnhancementsToResources's
        // trailing UpdateRange (which assumes Id > 0) is happy.
        var enhancement = new Enhancement
        {
            ResourceId = resourceId,
            EnhancerId = (int)EnhancerId.Bangumi,
            Target = (int)BangumiTarget.Introduction,
            ValueType = StandardValueType.String,
            Value = "Synopsis from Bangumi",
        };
        await _enhancementService.AddRange([enhancement]);

        // Profile says: route Introduction target into the Reserved pool's
        // Introduction property.
        SetReservedTargetProfile(
            resourceId, EnhancerId.Bangumi,
            target: (int)BangumiTarget.Introduction,
            reservedProperty: ReservedProperty.Introduction);

        await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>> { { resourceId, [(int)EnhancerId.Bangumi] } },
            [enhancement],
            CancellationToken.None);

        var rpvs = await _reservedValueService.GetAll(x => x.ResourceId == resourceId);
        rpvs.Should().ContainSingle("one Reserved row per (resource, scope) — Bangumi has a single PropertyValueScope");
        rpvs[0].Introduction.Should().Be("Synopsis from Bangumi");
    }

    [TestMethod]
    public async Task ApplyEnhancementsToResources_ReservedTarget_FlipsRecordToContextApplied()
    {
        // After a successful apply, the EnhancementRecord must transition to
        // ContextApplied so BuildContextCreationTasks won't pick it up again.
        var resourceId = await CreateResourceAsync();

        var enhancement = new Enhancement
        {
            ResourceId = resourceId,
            EnhancerId = (int)EnhancerId.Bangumi,
            Target = (int)BangumiTarget.Introduction,
            ValueType = StandardValueType.String,
            Value = "anything",
        };
        await _enhancementService.AddRange([enhancement]);

        // Pre-seed an EnhancementRecord at the earlier ContextCreated state.
        await _recordService.Add(new EnhancementRecord
        {
            ResourceId = resourceId,
            EnhancerId = (int)EnhancerId.Bangumi,
            Status = EnhancementRecordStatus.ContextCreated,
            ContextCreatedAt = DateTime.Now,
        });

        SetReservedTargetProfile(
            resourceId, EnhancerId.Bangumi,
            target: (int)BangumiTarget.Introduction,
            reservedProperty: ReservedProperty.Introduction);

        await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>> { { resourceId, [(int)EnhancerId.Bangumi] } },
            [enhancement],
            CancellationToken.None);

        var record = (await _recordService.GetAll(x => x.ResourceId == resourceId)).Single();
        record.Status.Should().Be(EnhancementRecordStatus.ContextApplied);
        record.ContextAppliedAt.Should().NotBeNull();
    }

    [TestMethod]
    public async Task ApplyEnhancementsToResources_ReservedTarget_UpdatesExistingRowInPlace()
    {
        // Re-applying with a new value must update the same ReservedPropertyValue
        // row (matched on scope + resourceId), not insert a duplicate.
        var resourceId = await CreateResourceAsync();

        async Task ApplyWith(string text)
        {
            var e = new Enhancement
            {
                ResourceId = resourceId,
                EnhancerId = (int)EnhancerId.Bangumi,
                Target = (int)BangumiTarget.Introduction,
                ValueType = StandardValueType.String,
                Value = text,
            };
            await _enhancementService.AddRange([e]);
            await _enhancerService.ApplyEnhancementsToResources(
                new Dictionary<int, HashSet<int>> { { resourceId, [(int)EnhancerId.Bangumi] } },
                [e],
                CancellationToken.None);
        }

        SetReservedTargetProfile(
            resourceId, EnhancerId.Bangumi,
            target: (int)BangumiTarget.Introduction,
            reservedProperty: ReservedProperty.Introduction);

        await ApplyWith("first synopsis");
        await ApplyWith("revised synopsis");

        var rpvs = await _reservedValueService.GetAll(x => x.ResourceId == resourceId);
        rpvs.Should().ContainSingle("the same scope+resource row must be updated, not duplicated");
        rpvs[0].Introduction.Should().Be("revised synopsis");
    }

    #endregion

    #region Custom-pool writes

    [TestMethod]
    public async Task ApplyEnhancementsToResources_CustomTarget_WritesCustomPropertyValue()
    {
        // Bangumi.Name is StandardValueType.String, PropertyType.SingleLineText,
        // no converter, no reservedPropertyCandidate — the cleanest path to
        // test a Custom-pool write.
        var resourceId = await CreateResourceAsync();
        var propId = await CreateSingleLineTextPropertyAsync("custom_title");

        var enhancement = new Enhancement
        {
            ResourceId = resourceId,
            EnhancerId = (int)EnhancerId.Bangumi,
            Target = (int)BangumiTarget.Name,
            ValueType = StandardValueType.String,
            Value = "Spirited Away",
        };
        await _enhancementService.AddRange([enhancement]);

        _fakeProfile.Set(resourceId,
        [
            new EnhancerFullOptions
            {
                EnhancerId = (int)EnhancerId.Bangumi,
                TargetOptions =
                [
                    new EnhancerTargetFullOptions
                    {
                        Target = (int)BangumiTarget.Name,
                        PropertyId = propId,
                        PropertyPool = PropertyPool.Custom,
                    }
                ],
            }
        ]);

        await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>> { { resourceId, [(int)EnhancerId.Bangumi] } },
            [enhancement],
            CancellationToken.None);

        var pvs = await _propertyValueService.GetAll(
            x => x.ResourceId == resourceId && x.PropertyId == propId,
            CustomPropertyValueAdditionalItem.BizValue,
            true);
        pvs.Should().ContainSingle();
        pvs[0].BizValue.Should().Be("Spirited Away");
    }

    [TestMethod]
    public async Task ApplyEnhancementsToResources_CustomTarget_MissingPropertyId_ThrowsLoudly()
    {
        // The Custom-pool branch looks the property up in propertyMap; if the
        // PropertyId in the profile points at nothing in the CustomProperty
        // table, the descriptor-not-found path throws via PropertyLocalizer.
        // This is the loud-fail we rely on so misconfigured profiles surface.
        var resourceId = await CreateResourceAsync();

        var enhancement = new Enhancement
        {
            ResourceId = resourceId,
            EnhancerId = (int)EnhancerId.Bangumi,
            Target = (int)BangumiTarget.Name,
            ValueType = StandardValueType.String,
            Value = "x",
        };
        await _enhancementService.AddRange([enhancement]);

        _fakeProfile.Set(resourceId,
        [
            new EnhancerFullOptions
            {
                EnhancerId = (int)EnhancerId.Bangumi,
                TargetOptions =
                [
                    new EnhancerTargetFullOptions
                    {
                        Target = (int)BangumiTarget.Name,
                        PropertyId = 999_999,             // nonexistent
                        PropertyPool = PropertyPool.Custom,
                    }
                ],
            }
        ]);

        var act = async () => await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>> { { resourceId, [(int)EnhancerId.Bangumi] } },
            [enhancement],
            CancellationToken.None);

        await act.Should().ThrowAsync<Exception>(
            "missing property must fail loudly, not silently drop the enhancement");
    }

    #endregion

    #region Profile mismatch — silent skip

    [TestMethod]
    public async Task ApplyEnhancementsToResources_NoMatchingTargetOptions_SilentlySkips()
    {
        // PrepareEnhancementOptions hits `continue` when no target options
        // are configured for the (enhancer, target) pair — the enhancement
        // is silently skipped (no property write, no error). Pinning this so
        // we notice if the behavior shifts to throwing.
        var resourceId = await CreateResourceAsync();

        var enhancement = new Enhancement
        {
            ResourceId = resourceId,
            EnhancerId = (int)EnhancerId.Bangumi,
            Target = (int)BangumiTarget.Introduction,
            ValueType = StandardValueType.String,
            Value = "would-be intro",
        };
        await _enhancementService.AddRange([enhancement]);

        // Profile has options for Bangumi but NOT for the Introduction target.
        _fakeProfile.Set(resourceId,
        [
            new EnhancerFullOptions
            {
                EnhancerId = (int)EnhancerId.Bangumi,
                TargetOptions =
                [
                    new EnhancerTargetFullOptions
                    {
                        Target = (int)BangumiTarget.Name,        // different target
                        PropertyId = (int)ReservedProperty.Rating,
                        PropertyPool = PropertyPool.Reserved,
                    }
                ],
            }
        ]);

        await _enhancerService.ApplyEnhancementsToResources(
            new Dictionary<int, HashSet<int>> { { resourceId, [(int)EnhancerId.Bangumi] } },
            [enhancement],
            CancellationToken.None);

        var rpvs = await _reservedValueService.GetAll(x => x.ResourceId == resourceId);
        rpvs.Should().BeEmpty("no target options for Introduction → silent skip, nothing persisted");
    }

    #endregion

    #region helpers

    private async Task<int> CreateResourceAsync()
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var added = await _resourceService.AddAll(
        [
            new ResourceDbModel
            {
                Path = $"/test/apply/{marker}",
                IsFile = true,
            }
        ]);
        return added[0].Id;
    }

    private async Task<int> CreateSingleLineTextPropertyAsync(string name)
    {
        var p = await _propertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = $"{name}_{Guid.NewGuid().ToString("N")[..8]}",
            Type = PropertyType.SingleLineText,
        });
        return p.Id;
    }

    private void SetReservedTargetProfile(int resourceId, EnhancerId enhancerId,
        int target, ReservedProperty reservedProperty)
    {
        _fakeProfile.Set(resourceId,
        [
            new EnhancerFullOptions
            {
                EnhancerId = (int)enhancerId,
                TargetOptions =
                [
                    new EnhancerTargetFullOptions
                    {
                        Target = target,
                        PropertyId = (int)reservedProperty,
                        PropertyPool = PropertyPool.Reserved,
                    }
                ],
            }
        ]);
    }

    #endregion
}
