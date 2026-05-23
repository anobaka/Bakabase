using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Integration coverage for PropertyValueScopePreferenceService — a previously untested
/// service that stores per-(resource, property) scope-priority overrides.
/// </summary>
[TestClass]
public sealed class PropertyValueScopePreferenceServiceTests
{
    private IServiceProvider _sp = null!;
    private IPropertyValueScopePreferenceService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _service = _sp.GetRequiredService<IPropertyValueScopePreferenceService>();
    }

    private static PropertyValueScopePreference Pref(int resourceId, int propertyId, params PropertyValueScope[] scopes)
        => new()
        {
            ResourceId = resourceId,
            PropertyPool = PropertyPool.Custom,
            PropertyId = propertyId,
            Priorities = scopes.Select(s => new PropertyValueScopePriority { Scope = s, FallbackOnEmpty = true })
                .ToArray()
        };

    [TestMethod]
    public async Task Get_NoPreference_ReturnsNull()
        => Assert.IsNull(await _service.Get(1, PropertyPool.Custom, 1));

    [TestMethod]
    public async Task Upsert_ThenGet_ReturnsPreference()
    {
        await _service.Upsert(Pref(1, 10, PropertyValueScope.Manual, PropertyValueScope.Synchronization));

        var got = await _service.Get(1, PropertyPool.Custom, 10);
        Assert.IsNotNull(got);
        Assert.AreEqual(2, got!.Priorities!.Length);
        Assert.AreEqual(PropertyValueScope.Manual, got.Priorities[0].Scope);
    }

    [TestMethod]
    public async Task Upsert_Existing_ReplacesPriorities()
    {
        await _service.Upsert(Pref(1, 10, PropertyValueScope.Manual));
        await _service.Upsert(Pref(1, 10, PropertyValueScope.Synchronization, PropertyValueScope.Manual));

        var got = await _service.Get(1, PropertyPool.Custom, 10);
        Assert.AreEqual(2, got!.Priorities!.Length);
        Assert.AreEqual(PropertyValueScope.Synchronization, got.Priorities[0].Scope);
    }

    [TestMethod]
    public async Task GetByResourceIds_ReturnsPreferencesForEachResource()
    {
        await _service.Upsert(Pref(1, 10, PropertyValueScope.Manual));
        await _service.Upsert(Pref(2, 10, PropertyValueScope.Manual));

        var list = await _service.GetByResourceIds([1, 2]);
        Assert.AreEqual(2, list.Count);
    }

    [TestMethod]
    public async Task Delete_RemovesPreference()
    {
        await _service.Upsert(Pref(1, 10, PropertyValueScope.Manual));
        await _service.Delete(1, PropertyPool.Custom, 10);
        Assert.IsNull(await _service.Get(1, PropertyPool.Custom, 10));
    }

    [TestMethod]
    public async Task RemoveByResourceIds_RemovesEveryPreferenceForThoseResources()
    {
        await _service.Upsert(Pref(1, 10, PropertyValueScope.Manual));
        await _service.Upsert(Pref(1, 11, PropertyValueScope.Manual));
        await _service.RemoveByResourceIds([1]);
        Assert.AreEqual(0, (await _service.GetByResourceIds([1])).Count);
    }
}
