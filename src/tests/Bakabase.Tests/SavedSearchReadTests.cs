using System.Reflection;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Search.Models.Db;
using Bakabase.Service.Controllers;
using Bakabase.Tests.Notices;

namespace Bakabase.Tests;

[TestClass]
public sealed class SavedSearchReadTests
{
    [TestMethod]
    public async Task Empty_saved_tabs_are_read_without_services_or_writes()
    {
        var options = new StubOptions<ResourceOptions>(new());
        var properties = new RecordingProperties();
        var response = await Controller(options, properties).GetSavedSearches();

        Assert.AreEqual(0, response.Code);
        Assert.AreEqual(0, response.Data!.Count);
        Assert.AreEqual(0, properties.Reads.Count);
        Assert.AreEqual(0, options.SaveCount);
    }

    [TestMethod]
    public async Task A_batch_resolves_properties_once_and_preserves_order_names_and_display_modes()
    {
        var options = new StubOptions<ResourceOptions>(new()
        {
            SavedSearches =
            [
                new() {Id = "second", Name = "My tab", DisplayMode = FilterDisplayMode.Advanced,
                    Search = Search(PropertyPool.Custom, 42)},
                new() {Id = "first", Search = Search(PropertyPool.Reserved, 7)},
                new() {Id = "empty", Search = new() {Group = new()}},
                new() {Id = "null-group", Search = new()}
            ]
        });
        var properties = new RecordingProperties
        {
            Read = _ => Task.FromResult(new List<Property>
            {
                new(PropertyPool.Custom, 42, PropertyType.Number, "Custom number"),
                new(PropertyPool.Reserved, 7, PropertyType.Number, "Reserved number")
            })
        };

        var response = await Controller(options, properties).GetSavedSearches();
        var tabs = response.Data!;

        CollectionAssert.AreEqual(new[] {"second", "first", "empty", "null-group"}, tabs.Select(tab => tab.Id).ToArray());
        Assert.AreEqual("My tab", tabs[0].Name);
        Assert.AreEqual(FilterDisplayMode.Advanced, tabs[0].DisplayMode);
        Assert.AreEqual("", tabs[1].Name);
        Assert.AreEqual(FilterDisplayMode.Simple, tabs[1].DisplayMode);
        Assert.AreEqual("Custom number", tabs[0].Search.Group!.Filters![0].Property!.Name);
        Assert.AreEqual("Reserved number", tabs[1].Search.Group!.Filters![0].Property!.Name);
        Assert.IsNotNull(tabs[2].Search.Group);
        Assert.IsNull(tabs[3].Search.Group);
        CollectionAssert.AreEqual(new[] {PropertyPool.Custom | PropertyPool.Reserved}, properties.Reads);
        Assert.AreEqual(0, options.SaveCount);
    }

    [TestMethod]
    public async Task Removing_or_renaming_a_tab_during_resolution_does_not_mix_its_snapshot()
    {
        var saved = new ResourceOptions.SavedSearch
        {
            Id = "original", Name = "Original name", DisplayMode = FilterDisplayMode.Advanced,
            Search = Search(PropertyPool.Custom, 42)
        };
        var options = new StubOptions<ResourceOptions>(new() {SavedSearches = [saved]});
        var resolve = new TaskCompletionSource<List<Property>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var properties = new RecordingProperties {Read = _ => resolve.Task};
        var pending = Controller(options, properties).GetSavedSearches();
        Assert.AreEqual(1, properties.Reads.Count);

        saved.Id = "changed";
        saved.Name = "Changed name";
        saved.DisplayMode = FilterDisplayMode.Simple;
        saved.Search = new();
        options.Value.SavedSearches.Clear();
        resolve.SetResult([]);

        var tab = (await pending).Data!.Single();
        Assert.AreEqual("original", tab.Id);
        Assert.AreEqual("Original name", tab.Name);
        Assert.AreEqual(FilterDisplayMode.Advanced, tab.DisplayMode);
        Assert.AreEqual(42, tab.Search.Group!.Filters![0].PropertyId);
        Assert.AreEqual(0, options.SaveCount);
    }

    [TestMethod]
    public async Task Foreign_saved_tab_criteria_stay_opaque_to_local_property_resolution()
    {
        var search = Search(PropertyPool.Custom, 42);
        search.Owner = new() {ServerId = "another-server", LibraryEpoch = "another-library"};
        var options = new StubOptions<ResourceOptions>(new()
        {
            SavedSearches = [new() {Id = "foreign", Search = search}]
        });
        var properties = new RecordingProperties();

        var tab = (await Controller(options, properties).GetSavedSearches()).Data!.Single();

        Assert.AreEqual(search.Owner, tab.Search.Owner);
        Assert.AreEqual("5", tab.Search.Group!.Filters![0].DbValue);
        Assert.IsNull(tab.Search.Group.Filters[0].Property);
        Assert.AreEqual(0, properties.Reads.Count);
        Assert.AreEqual(0, options.SaveCount);
    }

    private static ResourceSearchDbModel Search(PropertyPool pool, int propertyId) => new()
    {
        Group = new() {Filters = [new() {PropertyPool = pool, PropertyId = propertyId,
            Operation = SearchOperation.Equals, Value = "5"}]}
    };

    private static ResourceController Controller(StubOptions<ResourceOptions> options, IPropertyService properties) => new(
        service: DispatchProxy.Create<IResourceService, NeverCalled>(), ffMpegService: null!, resourceOptionsManager: options,
        ffMpegInstaller: null!, logger: null!, propertyService: properties, customPropertyService: null!,
        customPropertyValueService: null!, propertyLocalizer: DispatchProxy.Create<IPropertyLocalizer, Localizer>(),
        mediaLibraryV2Service: null!, mappingService: null!, taskManager: null!, fsOptionsManager: null!,
        scopePreferenceService: null!, resourceMoveGuard: null!, bakabaseLocalizer: null!, resourceProfileService: null!,
        placeholderResourceService: null!, materializationService: null!);

    private sealed class RecordingProperties : IPropertyService
    {
        public List<PropertyPool> Reads { get; } = [];
        public Func<PropertyPool, Task<List<Property>>> Read { get; init; } = _ => throw new InvalidOperationException("Unexpected property read");
        public Task<Property> GetProperty(PropertyPool pool, int id) => throw new InvalidOperationException("Unexpected property read");
        public Task<List<Property>> GetProperties(PropertyPool pool, bool includeDeprecated = true)
        {
            Reads.Add(pool);
            return Read(pool);
        }
    }

    public class Localizer : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => args![0]!.ToString();
    }

    public class NeverCalled : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new InvalidOperationException("Unexpected resource read: " + targetMethod?.Name);
    }
}
