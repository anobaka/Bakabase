using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Abstractions.Services;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using Newtonsoft.Json;

namespace Bakabase.Tests.Federation;

[TestClass]
public class ManagedResourceSearchPersistenceTests
{
    [TestMethod]
    public void Saved_query_preserves_device_selection_and_owner_bound_predicates()
    {
        var owner = new ResourceSearchOwner {ServerId = "device-b", LibraryEpoch = "library-b"};
        var input = new ResourceSearchInputModel
        {
            Owner = owner, DeviceIds = ["device-b"], Page = 3, PageSize = 50, Keyword = "album",
            Group = new()
            {
                Filters = [new() {Owner = owner, PropertyPool = PropertyPool.Custom, PropertyId = 12,
                    Operation = SearchOperation.Equals, DbValue = "\"choice-b\""}]
            }
        };
        var saved = JsonConvert.DeserializeObject<Bakabase.Modules.Search.Models.Db.ResourceSearchDbModel>(
            JsonConvert.SerializeObject(input.ToDbModel()))!;
        Assert.AreEqual(owner, saved.Owner);
        CollectionAssert.AreEqual(input.DeviceIds, saved.DeviceIds);
        Assert.AreEqual(owner, saved.Group!.Filters![0].Owner);
        Assert.AreEqual(12, saved.Group.Filters[0].PropertyId);
        Assert.AreEqual("\"choice-b\"", saved.Group.Filters[0].Value);
        Assert.AreEqual(3, saved.Page);
    }

    [TestMethod]
    public async Task Foreign_predicate_cannot_execute_against_coincident_local_property_id()
    {
        var properties = DispatchProxy.Create<IPropertyService, NeverCalled>();
        var input = new ResourceSearchInputModel
        {
            Group = new() {Filters = [new() {Owner = new() {ServerId = "b", LibraryEpoch = "epoch"},
                PropertyPool = PropertyPool.Custom, PropertyId = 12, Operation = SearchOperation.Equals}]}
        };
        await Assert.ThrowsExactlyAsync<ArgumentException>(() => input.ToDomainModel(properties));
        Assert.AreEqual(0, ((NeverCalled) (object) properties).Calls);
    }
    [DataTestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public async Task Reading_foreign_saved_criteria_does_not_look_up_same_numbered_local_properties_or_parents(bool wholeQueryOwner)
    {
        var owner = new ResourceSearchOwner {ServerId = "source-b", LibraryEpoch = "library-b"};
        var properties = DispatchProxy.Create<IPropertyService, NeverCalled>();
        var localizer = DispatchProxy.Create<IPropertyLocalizer, NeverCalled>();
        var resources = DispatchProxy.Create<IResourceService, NeverCalled>();
        var saved = new Bakabase.Modules.Search.Models.Db.ResourceSearchDbModel
        {
            Owner = wholeQueryOwner ? owner : null,
            Group = new()
            {
                Filters = [new() {Owner = wholeQueryOwner ? null : owner,
                    PropertyPool = PropertyPool.Internal, PropertyId = (int) InternalProperty.ParentResource,
                    Operation = SearchOperation.Equals, Value = "\"12\""}]
            }
        };
        var result = await new[] {saved}.ToViewModels(properties, localizer, resources);
        var filter = result.Single().Group!.Filters!.Single();
        Assert.AreEqual(saved.Owner, result.Single().Owner);
        Assert.AreEqual(saved.Group.Filters[0].Owner, filter.Owner);
        Assert.AreEqual("\"12\"", filter.DbValue);
        Assert.IsNull(filter.Property);
        Assert.IsNull(filter.BizValue);
        Assert.AreEqual(0, ((NeverCalled) (object) properties).Calls);
        Assert.AreEqual(0, ((NeverCalled) (object) localizer).Calls);
        Assert.AreEqual(0, ((NeverCalled) (object) resources).Calls);
    }

    public class NeverCalled : DispatchProxy
    {
        public int Calls;
        protected override object? Invoke(MethodInfo? method, object?[]? args)
        {
            Calls++;
            throw new InvalidOperationException("Foreign criteria invoked local service " + method?.Name);
        }
    }

}
