using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.TestKit.Implementations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class PropertyValueScopeResolverTests
{
    private const int PropId = 7;

    private static PropertyValueScopeResolver MakeResolver(params PropertyValueScope[] globalPriority) =>
        new(new TestBOptions<ResourceOptions>(new ResourceOptions { PropertyValueScopePriority = globalPriority }));

    private static Resource MakeResource(
        IEnumerable<(PropertyValueScope Scope, object? Value)> values,
        PropertyValueScopePreference? preference = null)
    {
        var propertyValues = values
            .Select(v => new Resource.Property.PropertyValue((int)v.Scope, v.Value, v.Value, v.Value))
            .ToList();
        return new Resource
        {
            Id = 1,
            Properties = new Dictionary<int, Dictionary<int, Resource.Property>>
            {
                [(int)PropertyPool.Reserved] = new()
                {
                    [PropId] = new Resource.Property("p", PropertyType.SingleLineText, propertyValues)
                }
            },
            ScopePreferences = preference == null ? null : [preference]
        };
    }

    private static PropertyValueScopePreference Preference(
        params (PropertyValueScope Scope, bool FallbackOnEmpty)[] priorities) =>
        new()
        {
            ResourceId = 1,
            PropertyPool = PropertyPool.Reserved,
            PropertyId = PropId,
            Priorities = priorities
                .Select(p => new PropertyValueScopePriority { Scope = p.Scope, FallbackOnEmpty = p.FallbackOnEmpty })
                .ToArray()
        };

    private static object? Resolve(PropertyValueScopeResolver resolver, Resource resource) =>
        resolver.Resolve(resource, PropertyPool.Reserved, PropId)?.BizValue;

    [TestMethod]
    public void SkipsEmptyHigherPriorityScope_GlobalPriorityUnconfigured()
    {
        // The reported bug: a Manual row (e.g. from editing Rating) holds an empty Name, while an
        // enhancer populated the Av scope. Manual sorts first by default but must be skipped.
        var resolver = MakeResolver();
        var resource = MakeResource([
            (PropertyValueScope.Manual, null),
            (PropertyValueScope.Av, "Real Name")
        ]);
        Assert.AreEqual("Real Name", Resolve(resolver, resource));
    }

    [TestMethod]
    public void ConfiguredGlobalPriorityIsRespected()
    {
        var resolver = MakeResolver(PropertyValueScope.Av, PropertyValueScope.Manual);
        var resource = MakeResource([
            (PropertyValueScope.Manual, "Manual Name"),
            (PropertyValueScope.Av, "Av Name")
        ]);
        Assert.AreEqual("Av Name", Resolve(resolver, resource));
    }

    [TestMethod]
    public void PreferenceOverridesGlobalPriority()
    {
        var resolver = MakeResolver(); // default: Manual first
        var resource = MakeResource(
            [(PropertyValueScope.Manual, "Manual Name"), (PropertyValueScope.Av, "Av Name")],
            Preference((PropertyValueScope.Av, true)));
        Assert.AreEqual("Av Name", Resolve(resolver, resource));
    }

    [TestMethod]
    public void FallbackOnEmptyFalse_StopsAtEmptyScope()
    {
        var resolver = MakeResolver();
        var resource = MakeResource(
            [(PropertyValueScope.Manual, null), (PropertyValueScope.Av, "Av Name")],
            Preference((PropertyValueScope.Manual, false)));
        // The chain stops at the empty Manual scope rather than falling through to Av.
        Assert.IsNull(Resolve(resolver, resource));
    }

    [TestMethod]
    public void EmptyStringAndEmptyListAreSkipped_ZeroIsKept()
    {
        var resolver = MakeResolver();
        Assert.IsNull(Resolve(resolver, MakeResource([(PropertyValueScope.Manual, "  ")])));
        Assert.IsNull(Resolve(resolver, MakeResource([(PropertyValueScope.Manual, new List<string>())])));
        Assert.AreEqual(0m, Resolve(resolver, MakeResource([(PropertyValueScope.Manual, 0m)])));
    }

    [TestMethod]
    public void AllScopesEmpty_ReturnsNull()
    {
        var resolver = MakeResolver();
        var resource = MakeResource([
            (PropertyValueScope.Manual, null),
            (PropertyValueScope.Av, "")
        ]);
        Assert.IsNull(Resolve(resolver, resource));
    }

    [TestMethod]
    public void PropertyAbsent_ReturnsNull()
    {
        var resolver = MakeResolver();
        Assert.IsNull(resolver.Resolve(new Resource { Id = 1 }, PropertyPool.Reserved, PropId)?.BizValue);
    }
}
