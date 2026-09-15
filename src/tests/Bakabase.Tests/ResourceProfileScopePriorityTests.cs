using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

[TestClass]
public sealed class ResourceProfileScopePriorityTests
{
    [TestMethod]
    public async Task LoadedPropertiesCarryOnlyTheirEffectiveProfilesScopeOrderWithoutFakingResourceOverrides()
    {
        var services = await TestServiceBuilder.BuildServiceProvider();
        var resourceId = (await services.GetRequiredService<IPlaceholderResourceService>()
            .CreateByTitle("Manual title")).ResourceId;
        await services.GetRequiredService<IReservedPropertyValueService>().Add(new ReservedPropertyValue
            {ResourceId = resourceId, Scope = (int)PropertyValueScope.Av, Name = "Profile title"});
        var profiles = services.GetRequiredService<IResourceProfileService>();
        var nameTemplate = "{" + services.GetRequiredService<IPropertyLocalizer>().BuiltinPropertyName(ResourceProperty.Name) + "}";
        await profiles.Add("Low priority", "{}", nameTemplate, null, null, null,
            new ResourceProfilePropertyOptions
            {
                Properties =
                [new() {Pool = PropertyPool.Reserved, Id = (int)ReservedProperty.Name, ScopePriority = [PropertyValueScope.Manual]},
                    new() {Pool = PropertyPool.Reserved, Id = (int)ReservedProperty.Introduction, ScopePriority = [PropertyValueScope.Av]}]
            }, 10);
        await profiles.Add("High priority", "{}", null, null, null, null,
            new ResourceProfilePropertyOptions
            {
                Properties =
                [new() {Pool = PropertyPool.Reserved, Id = (int)ReservedProperty.Name, ScopePriority = [PropertyValueScope.Av]},
                    // The same numeric ID in a different pool must not affect the reserved property.
                    new() {Pool = PropertyPool.Custom, Id = (int)ReservedProperty.Rating, ScopePriority = [PropertyValueScope.Av]}]
            }, 20);
        await services.GetRequiredService<IResourceProfileIndexService>().RebuildAsync(null, CancellationToken.None);

        var resources = services.GetRequiredService<IResourceService>();
        var resource = (await resources.Get(resourceId, ResourceAdditionalItem.DisplayName))!;
        var reserved = resource.Properties![(int)PropertyPool.Reserved];
        CollectionAssert.AreEqual(new[] {PropertyValueScope.Av}, reserved[(int)ReservedProperty.Name].ProfileScopePriority);
        Assert.IsNull(reserved[(int)ReservedProperty.Rating].ProfileScopePriority);
        Assert.IsNull(reserved[(int)ReservedProperty.Introduction].ProfileScopePriority,
            "A lower priority profile's property block must not be merged into the selected block.");
        Assert.IsFalse(resource.ScopePreferences?.Any() == true);
        Assert.AreEqual("Profile title", resource.DisplayName,
            "Display-name templates must use the same profile scope priority as the property display.");

        var preferences = services.GetRequiredService<IPropertyValueScopePreferenceService>();
        await preferences.Upsert(new PropertyValueScopePreference
        {
            ResourceId = resourceId, PropertyPool = PropertyPool.Reserved, PropertyId = (int)ReservedProperty.Name,
            Priorities = [new() {Scope = PropertyValueScope.Manual, FallbackOnEmpty = false}]
        });
        var overridden = (await resources.Get(resourceId, ResourceAdditionalItem.DisplayName))!;
        Assert.AreEqual("Manual title", overridden.DisplayName);
        CollectionAssert.AreEqual(new[] {PropertyValueScope.Av},
            overridden.Properties![(int)PropertyPool.Reserved][(int)ReservedProperty.Name].ProfileScopePriority);
        Assert.AreEqual(1, overridden.ScopePreferences!.Count);
        Assert.AreEqual(PropertyValueScope.Manual, overridden.ScopePreferences[0].Priorities!.Single().Scope);
        Assert.IsFalse(overridden.ScopePreferences[0].Priorities!.Single().FallbackOnEmpty);

        await preferences.Delete(resourceId, PropertyPool.Reserved, (int)ReservedProperty.Name);
        var reset = (await resources.Get(resourceId, ResourceAdditionalItem.DisplayName))!;
        Assert.AreEqual("Profile title", reset.DisplayName);
        Assert.IsFalse(reset.ScopePreferences?.Any() == true);
    }
}
