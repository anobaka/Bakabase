using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Modules.Property.Components.Properties.Choice;
using Bakabase.Modules.Collection.Abstractions.Services;
using Bakabase.Modules.Collection.Models.Input;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;
using PropertyPool = Bakabase.Abstractions.Models.Domain.Constants.PropertyPool;

namespace Bakabase.Modules.Collection.Tests;

/// <summary>
/// Membership as an ordinary property. This is what makes a collection worth having rather than
/// just a list: "everything in this series that I do not own yet" is a resource search, not a page
/// somebody had to write.
/// </summary>
[TestClass]
public sealed class CollectionAsAPropertyTests
{
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
    }

    private ICollectionService Collections => _sp.GetRequiredService<ICollectionService>();
    private IResourceService Resources => _sp.GetRequiredService<IResourceService>();

    private async Task<int> NewCollection(string name) =>
        (await Collections.Add(new CollectionInputModel {Name = name})).Id;

    private async Task<int> NewResource(string title) =>
        (await _sp.GetRequiredService<IPlaceholderResourceService>().CreateByTitle(title)).ResourceId;

    [TestMethod]
    public async Task TheCollectionsAResourceIsInAreOfferedAsAProperty()
    {
        var id = await NewCollection("A Series");

        var property = await _sp.GetRequiredService<IPropertyService>()
            .GetProperty(PropertyPool.Internal, (int) ResourceProperty.CollectionMulti);

        Assert.AreEqual(PropertyType.MultipleChoice, property.Type);

        var choices = (property.Options as MultipleChoicePropertyOptions)?.Choices ?? [];

        Assert.IsTrue(choices.Any(c => c.Value == id.ToString() && c.Label == "A Series"),
            "the collections themselves are the choices");
    }

    [TestMethod]
    public async Task AResourceCarriesItsCollectionsWhenAskedFor()
    {
        var first = await NewCollection("First");
        var second = await NewCollection("Second");
        var resourceId = await NewResource("In both");

        await Collections.AddMembers(first, [resourceId]);
        await Collections.AddMembers(second, [resourceId]);

        var resource = (await Resources.GetAll(r => r.Id == resourceId,
            ResourceAdditionalItem.CollectionName)).Single();

        CollectionAssert.AreEquivalent(new[] {"First", "Second"},
            resource.Collections!.Select(c => c.Name).ToArray());
    }

    /// <summary>
    /// The point of the whole property: filtering by collection must give the same answer as asking
    /// the collection for its members, or the two would be different features that disagree.
    /// </summary>
    [TestMethod]
    public async Task SearchingByCollectionAgreesWithTheCollectionItself()
    {
        var id = await NewCollection("A Series");
        var inIt = await NewResource("Volume 1");
        var alsoInIt = await NewResource("Volume 2");

        await NewResource("Something else");
        await Collections.AddMembers(id, [inIt, alsoInIt]);

        var property = await _sp.GetRequiredService<IPropertyService>()
            .GetProperty(PropertyPool.Internal, (int) ResourceProperty.CollectionMulti);

        var found = await Resources.Search(new ResourceSearch
        {
            Group = new ResourceSearchFilterGroup
            {
                Combinator = SearchCombinator.And,
                Filters =
                [
                    new ResourceSearchFilter
                    {
                        PropertyPool = PropertyPool.Internal,
                        PropertyId = (int) ResourceProperty.CollectionMulti,
                        Operation = SearchOperation.Contains,
                        // The db value of a multiple-choice property is the list of chosen ids.
                        DbValue = new List<string> {id.ToString()},
                        Property = property,
                    }
                ]
            },
            PageIndex = 1,
            PageSize = 100,
        }, ResourceAdditionalItem.None);

        CollectionAssert.AreEquivalent(new[] {inIt, alsoInIt},
            found.Data!.Select(r => r.Id).ToArray(),
            $"expected {inIt},{alsoInIt}; got {string.Join(',', found.Data!.Select(r => r.Id))}");
    }

    /// <summary>
    /// Setting the value replaces, the way a multi-valued property should: the bulk editor's
    /// "these resources are in these collections" is a statement, not an addition.
    /// </summary>
    [TestMethod]
    public async Task SettingTheValueInBulkReplacesMembership()
    {
        var first = await NewCollection("First");
        var second = await NewCollection("Second");
        var resourceId = await NewResource("Moves");

        await Collections.AddMembers(first, [resourceId]);

        await Resources.BulkPutPropertyValue([resourceId], new ResourcePropertyValuePutInputModel
        {
            PropertyId = (int) ResourceProperty.CollectionMulti,
            IsCustomProperty = false,
            Value = new List<string> {second.ToString()}
                .SerializeAsStandardValue(StandardValueType.ListString),
        });

        var memberships = await _sp.GetRequiredService<ICollectionResourceMappingService>()
            .GetByResourceId(resourceId);

        CollectionAssert.AreEqual(new[] {second}, memberships.Select(m => m.CollectionId).ToArray());
    }
}
