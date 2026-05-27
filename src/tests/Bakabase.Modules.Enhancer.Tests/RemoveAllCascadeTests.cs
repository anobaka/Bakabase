using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Tests.Helpers;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using BangumiTarget = Bakabase.Modules.Enhancer.Components.Enhancers.Bangumi.BangumiEnhancerTarget;

namespace Bakabase.Modules.Enhancer.Tests;

/// <summary>
/// Coverage for <c>EnhancementService.RemoveAll(selector, removeGeneratedCustomPropertyValues)</c>.
/// Two things matter:
///
/// 1. The <c>removeGeneratedCustomPropertyValues</c> flag actually cascades —
///    when on, the matching CustomPropertyValue rows are removed alongside
///    the Enhancement rows in the same transaction.
/// 2. The cascade is scoped to exactly the (scope, resourceId) tuples of the
///    deleted enhancements. The pre-filter
///    <c>scopes.Contains(x.Scope) && resourceIds.Contains(x.ResourceId)</c>
///    is broader than the tuple intersection, so the secondary tuple-aware
///    filter must prevent over-deletion when an unrelated resource happens
///    to share a scope.
/// </summary>
[TestClass]
public sealed class RemoveAllCascadeTests
{
    private IServiceProvider _sp = null!;
    private IEnhancerService _enhancerService = null!;
    private IEnhancementService _enhancementService = null!;
    private IResourceService _resourceService = null!;
    private ICustomPropertyService _propertyService = null!;
    private ICustomPropertyValueService _propertyValueService = null!;
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
        _resourceService = _sp.GetRequiredService<IResourceService>();
        _propertyService = _sp.GetRequiredService<ICustomPropertyService>();
        _propertyValueService = _sp.GetRequiredService<ICustomPropertyValueService>();
    }

    [TestMethod]
    public async Task RemoveAll_FlagOff_LeavesGeneratedCustomPropertyValuesAlone()
    {
        // Setup: one resource, one custom property, one enhancement-applied CPV.
        var (resourceId, propId) = await SeedResourceWithAppliedEnhancementAsync("only");

        // Confirm precondition: CPV exists.
        var before = await _propertyValueService.GetAll(
            x => x.ResourceId == resourceId && x.PropertyId == propId,
            CustomPropertyValueAdditionalItem.None, true);
        before.Should().ContainSingle("Apply should have written the CPV before we test RemoveAll");

        await _enhancementService.RemoveAll(
            x => x.ResourceId == resourceId,
            removeGeneratedCustomPropertyValues: false);

        var enhancements = await _enhancementService.GetAll(x => x.ResourceId == resourceId);
        enhancements.Should().BeEmpty("the selector matched the enhancement row, so it must be gone");

        var after = await _propertyValueService.GetAll(
            x => x.ResourceId == resourceId && x.PropertyId == propId,
            CustomPropertyValueAdditionalItem.None, true);
        after.Should().ContainSingle("flag=false must not touch CPV rows");
    }

    [TestMethod]
    public async Task RemoveAll_FlagOn_AlsoDeletesGeneratedCustomPropertyValues()
    {
        var (resourceId, propId) = await SeedResourceWithAppliedEnhancementAsync("with-cpv");

        await _enhancementService.RemoveAll(
            x => x.ResourceId == resourceId,
            removeGeneratedCustomPropertyValues: true);

        var enhancements = await _enhancementService.GetAll(x => x.ResourceId == resourceId);
        enhancements.Should().BeEmpty();

        var pvs = await _propertyValueService.GetAll(
            x => x.ResourceId == resourceId && x.PropertyId == propId,
            CustomPropertyValueAdditionalItem.None, true);
        pvs.Should().BeEmpty("flag=true must cascade-delete the matching CPV");
    }

    [TestMethod]
    public async Task RemoveAll_FlagOn_DoesNotTouchUnrelatedResourceCpvSharingTheSameScope()
    {
        // Two resources, same enhancer (same PropertyValueScope), same custom
        // property. Deleting enhancement A must only nuke A's CPV — B's CPV
        // sits at the same scope but a different resourceId, and must survive.
        var (resourceA, propId) = await SeedResourceWithAppliedEnhancementAsync("rA");
        var resourceB = await SeedAnotherResourceForSamePropertyAsync(propId, "rB");

        await _enhancementService.RemoveAll(
            x => x.ResourceId == resourceA,
            removeGeneratedCustomPropertyValues: true);

        var aPvs = await _propertyValueService.GetAll(
            x => x.ResourceId == resourceA && x.PropertyId == propId,
            CustomPropertyValueAdditionalItem.None, true);
        aPvs.Should().BeEmpty("A's CPV must be cascade-deleted");

        var bPvs = await _propertyValueService.GetAll(
            x => x.ResourceId == resourceB && x.PropertyId == propId,
            CustomPropertyValueAdditionalItem.None, true);
        bPvs.Should().ContainSingle(
            "B's CPV shares scope with A's deleted enhancement but is at a different resource — the tuple-aware filter must spare it");
    }

    [TestMethod]
    public async Task RemoveAll_EmptyMatch_NoOpAndSucceeds()
    {
        var resp = await _enhancementService.RemoveAll(
            x => x.ResourceId == 99_999,
            removeGeneratedCustomPropertyValues: true);
        resp.Code.Should().Be(0, "RemoveAll over an empty match must succeed (Code 0 = Ok)");
    }

    #region helpers

    /// <summary>
    /// Creates a resource + custom SingleLineText property, then runs the
    /// full Apply pipeline so a CustomPropertyValue row exists for the
    /// enhancement we're about to delete. Returns the resource and property
    /// ids so the test can assert on them.
    /// </summary>
    private async Task<(int ResourceId, int PropertyId)> SeedResourceWithAppliedEnhancementAsync(string nameTag)
    {
        var resourceId = await CreateResourceAsync(nameTag);
        var propId = await CreatePropertyAsync(nameTag);
        await ApplyBangumiNameAsync(resourceId, propId, $"value-{nameTag}");
        return (resourceId, propId);
    }

    private async Task<int> SeedAnotherResourceForSamePropertyAsync(int propId, string nameTag)
    {
        var resourceId = await CreateResourceAsync(nameTag);
        await ApplyBangumiNameAsync(resourceId, propId, $"value-{nameTag}");
        return resourceId;
    }

    private async Task<int> CreateResourceAsync(string nameTag)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var added = await _resourceService.AddAll(
        [
            new ResourceDbModel { Path = $"/test/cascade/{nameTag}/{marker}", IsFile = true }
        ]);
        return added[0].Id;
    }

    private async Task<int> CreatePropertyAsync(string nameTag)
    {
        var p = await _propertyService.Add(new CustomPropertyAddOrPutDto
        {
            Name = $"prop_{nameTag}_{Guid.NewGuid().ToString("N")[..8]}",
            Type = PropertyType.SingleLineText,
        });
        return p.Id;
    }

    private async Task ApplyBangumiNameAsync(int resourceId, int propId, string value)
    {
        var e = new Enhancement
        {
            ResourceId = resourceId,
            EnhancerId = (int)EnhancerId.Bangumi,
            Target = (int)BangumiTarget.Name,
            ValueType = StandardValueType.String,
            Value = value,
        };
        await _enhancementService.AddRange([e]);

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
            [e],
            CancellationToken.None);
    }

    #endregion
}
