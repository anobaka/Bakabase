using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Services;
using Bakabase.Tests.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.BulkModification.Tests.Services;

[TestClass]
public class BulkModificationServiceTests
{
    private IServiceProvider _serviceProvider = null!;
    private IBulkModificationService _bulkModificationService = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _serviceProvider = await TestServiceBuilder.BuildServiceProvider();
        _bulkModificationService = _serviceProvider.GetRequiredService<IBulkModificationService>();
    }

    #region CRUD Operations

    [TestMethod]
    public async Task Add_And_Get_BulkModification()
    {
        var bm = CreateBasicBulkModification("Test BM " + Guid.NewGuid().ToString("N")[..8]);
        await _bulkModificationService.Add(bm);

        // After Add, get the ID by finding the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.FirstOrDefault(x => x.Name == bm.Name);
        addedItem.Should().NotBeNull();

        var retrieved = await _bulkModificationService.Get(addedItem!.Id);
        retrieved.Should().NotBeNull();
        retrieved!.Name.Should().Be(bm.Name);
        retrieved.IsActive.Should().BeTrue();
    }

    [TestMethod]
    public async Task GetAll_ReturnsAllBulkModifications()
    {
        await _bulkModificationService.Add(CreateBasicBulkModification("BM1"));
        await _bulkModificationService.Add(CreateBasicBulkModification("BM2"));
        await _bulkModificationService.Add(CreateBasicBulkModification("BM3"));

        var all = await _bulkModificationService.GetAll();
        all.Should().HaveCountGreaterThanOrEqualTo(3);
    }

    [TestMethod]
    public async Task Put_UpdatesBulkModification()
    {
        var uniqueName = "Original Name " + Guid.NewGuid().ToString("N")[..8];
        var bm = CreateBasicBulkModification(uniqueName);
        await _bulkModificationService.Add(bm);

        // Find the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.First(x => x.Name == uniqueName);

        addedItem.Name = "Updated Name";
        await _bulkModificationService.Put(addedItem);

        var updated = await _bulkModificationService.Get(addedItem.Id);
        updated!.Name.Should().Be("Updated Name");
    }

    [TestMethod]
    public async Task Delete_RemovesBulkModification()
    {
        var uniqueName = "To Delete " + Guid.NewGuid().ToString("N")[..8];
        var bm = CreateBasicBulkModification(uniqueName);
        await _bulkModificationService.Add(bm);

        // Find the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.First(x => x.Name == uniqueName);
        var id = addedItem.Id;

        await _bulkModificationService.Delete(id);

        var deleted = await _bulkModificationService.Get(id);
        deleted.Should().BeNull();
    }

    [TestMethod]
    public async Task Duplicate_CreatesCopyOfBulkModification()
    {
        var uniqueName = "Original " + Guid.NewGuid().ToString("N")[..8];
        var bm = CreateBasicBulkModification(uniqueName);
        await _bulkModificationService.Add(bm);

        // Find the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.First(x => x.Name == uniqueName);

        await _bulkModificationService.Duplicate(addedItem.Id);

        var afterAll = await _bulkModificationService.GetAll();
        var duplicates = afterAll.Where(x => x.Name == uniqueName).ToList();
        duplicates.Should().HaveCount(2);
        duplicates.Select(x => x.Id).Distinct().Should().HaveCount(2);
    }

    [TestMethod]
    public async Task Patch_UpdatesSpecificFields()
    {
        var uniqueName = "Original " + Guid.NewGuid().ToString("N")[..8];
        var bm = CreateBasicBulkModification(uniqueName);
        bm.IsActive = true;
        await _bulkModificationService.Add(bm);

        // Find the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.First(x => x.Name == uniqueName);

        await _bulkModificationService.Patch(addedItem.Id, new Models.Input.PatchBulkModification
        {
            Name = "Patched Name",
            IsActive = false
        });

        var patched = await _bulkModificationService.Get(addedItem.Id);
        patched!.Name.Should().Be("Patched Name");
        patched.IsActive.Should().BeFalse();
    }

    #endregion

    #region Error Cases

    [TestMethod]
    public async Task Apply_InactiveBulkModification_ThrowsException()
    {
        var uniqueName = "Inactive Test " + Guid.NewGuid().ToString("N")[..8];
        var bm = CreateBasicBulkModification(uniqueName);
        bm.IsActive = false;
        await _bulkModificationService.Add(bm);

        // Find the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.First(x => x.Name == uniqueName);

        var action = async () => await _bulkModificationService.Apply(addedItem.Id);
        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*inactive*");
    }

    [TestMethod]
    public async Task Revert_NotAppliedBulkModification_ThrowsException()
    {
        var uniqueName = "Not Applied Test " + Guid.NewGuid().ToString("N")[..8];
        var bm = CreateBasicBulkModification(uniqueName);
        await _bulkModificationService.Add(bm);

        // Find the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.First(x => x.Name == uniqueName);

        var action = async () => await _bulkModificationService.Revert(addedItem.Id);
        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*not applied*");
    }

    [TestMethod]
    public async Task Apply_NoDiffs_ThrowsException()
    {
        var uniqueName = "No Diffs Test " + Guid.NewGuid().ToString("N")[..8];
        var bm = CreateBasicBulkModification(uniqueName);
        await _bulkModificationService.Add(bm);

        // Find the item we just added
        var all = await _bulkModificationService.GetAll();
        var addedItem = all.First(x => x.Name == uniqueName);

        var action = async () => await _bulkModificationService.Apply(addedItem.Id);
        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*No resource diff*");
    }

    [TestMethod]
    public async Task Get_NonExistentId_ReturnsNull()
    {
        var result = await _bulkModificationService.Get(99999);
        result.Should().BeNull();
    }

    #endregion

    #region Helper Methods

    private static Abstractions.Models.BulkModification CreateBasicBulkModification(string name)
    {
        return new Abstractions.Models.BulkModification
        {
            Name = name,
            IsActive = true,
            CreatedAt = DateTime.Now
        };
    }

    #endregion
}
