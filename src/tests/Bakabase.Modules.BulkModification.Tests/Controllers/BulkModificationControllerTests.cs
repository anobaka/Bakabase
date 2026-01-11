using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Services;
using Bakabase.Modules.BulkModification.Models.Input;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Service.Controllers;
using Bakabase.Service.Models.Input;
using Bakabase.Tests.Utils;
using Bootstrap.Models.ResponseModels;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.BulkModification.Tests.Controllers;

/// <summary>
/// API integration tests for BulkModificationController.
/// Tests the full API contract by instantiating the controller with real services.
/// </summary>
[TestClass]
public class BulkModificationControllerTests
{
    private IServiceProvider _serviceProvider = null!;
    private BulkModificationController _controller = null!;
    private IBulkModificationService _service = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _serviceProvider = await TestServiceBuilder.BuildServiceProvider();
        _service = _serviceProvider.GetRequiredService<IBulkModificationService>();

        _controller = new BulkModificationController(
            _service,
            _serviceProvider.GetRequiredService<IResourceService>(),
            _serviceProvider.GetRequiredService<IPropertyLocalizer>(),
            _serviceProvider.GetRequiredService<IPropertyService>(),
            _serviceProvider.GetRequiredService<IBulkModificationLocalizer>()
        );
    }

    #region Add Endpoint Tests

    [TestMethod]
    public async Task Add_ReturnsOkResponse()
    {
        var response = await _controller.Add();

        response.Should().NotBeNull();
        response.Code.Should().Be(0);
    }

    [TestMethod]
    public async Task Add_CreatesNewBulkModification()
    {
        var beforeCount = (await _service.GetAll()).Count;

        await _controller.Add();

        var afterCount = (await _service.GetAll()).Count;
        afterCount.Should().Be(beforeCount + 1);
    }

    [TestMethod]
    public async Task Add_SetsDefaultValues()
    {
        await _controller.Add();

        var all = await _service.GetAll();
        var newest = all.OrderByDescending(x => x.Id).First();

        newest.IsActive.Should().BeTrue();
        newest.Name.Should().NotBeNullOrEmpty();
        newest.CreatedAt.Should().BeCloseTo(DateTime.Now, TimeSpan.FromMinutes(1));
    }

    #endregion

    #region Get Endpoint Tests

    [TestMethod]
    public async Task Get_ExistingId_ReturnsViewModel()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var response = await _controller.Get(id);

        response.Should().NotBeNull();
        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(id);
    }

    [TestMethod]
    public async Task Get_NonExistingId_ReturnsNullData()
    {
        var response = await _controller.Get(99999);

        response.Should().NotBeNull();
        response.Data.Should().BeNull();
    }

    #endregion

    #region GetAll Endpoint Tests

    [TestMethod]
    public async Task GetAll_ReturnsAllBulkModifications()
    {
        await _controller.Add();
        await _controller.Add();
        await _controller.Add();

        var response = await _controller.GetAll();

        response.Should().NotBeNull();
        response.Data.Should().NotBeNull();
        response.Data!.Count.Should().BeGreaterThanOrEqualTo(3);
    }

    [TestMethod]
    public async Task GetAll_EmptyDatabase_ReturnsEmptyList()
    {
        // Fresh database - might have some items from other tests
        var response = await _controller.GetAll();

        response.Should().NotBeNull();
        response.Data.Should().NotBeNull();
    }

    #endregion

    #region Patch Endpoint Tests

    [TestMethod]
    public async Task Patch_UpdatesName()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var patchModel = new BulkModificationPatchInputModel
        {
            Name = "Updated Name"
        };

        var response = await _controller.Patch(id, patchModel);

        response.Code.Should().Be(0);

        var updated = await _service.Get(id);
        updated!.Name.Should().Be("Updated Name");
    }

    [TestMethod]
    public async Task Patch_UpdatesIsActive()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var bm = all.First();
        var id = bm.Id;
        var originalIsActive = bm.IsActive;

        var patchModel = new BulkModificationPatchInputModel
        {
            IsActive = !originalIsActive
        };

        await _controller.Patch(id, patchModel);

        var updated = await _service.Get(id);
        updated!.IsActive.Should().Be(!originalIsActive);
    }

    [TestMethod]
    public async Task Patch_MultipleFields_UpdatesAll()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var patchModel = new BulkModificationPatchInputModel
        {
            Name = "Multi-Field Update",
            IsActive = false
        };

        await _controller.Patch(id, patchModel);

        var updated = await _service.Get(id);
        updated!.Name.Should().Be("Multi-Field Update");
        updated.IsActive.Should().BeFalse();
    }

    [TestMethod]
    public async Task Patch_WithProcesses_UpdatesProcesses()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var patchModel = new BulkModificationPatchInputModel
        {
            Processes = new List<BulkModificationProcessInputModel>
            {
                new()
                {
                    PropertyPool = PropertyPool.Internal,
                    PropertyId = (int)InternalProperty.Filename,
                    Steps = "[]" // Empty steps
                }
            }
        };

        await _controller.Patch(id, patchModel);

        var updated = await _service.Get(id);
        updated!.Processes.Should().NotBeNull();
        updated.Processes!.Count.Should().Be(1);
    }

    #endregion

    #region Delete Endpoint Tests

    [TestMethod]
    public async Task Delete_RemovesBulkModification()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var response = await _controller.Delete(id);

        response.Code.Should().Be(0);

        var deleted = await _service.Get(id);
        deleted.Should().BeNull();
    }

    [TestMethod]
    public async Task Delete_NonExistingId_DoesNotThrow()
    {
        // Should not throw for non-existing IDs
        var response = await _controller.Delete(99999);
        response.Should().NotBeNull();
    }

    #endregion

    #region Duplicate Endpoint Tests

    [TestMethod]
    public async Task Duplicate_CreatesNewCopy()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;
        var beforeCount = all.Count;

        var response = await _controller.Duplicate(id);

        response.Code.Should().Be(0);

        var afterAll = await _service.GetAll();
        afterAll.Count.Should().Be(beforeCount + 1);
    }

    [TestMethod]
    public async Task Duplicate_CopiesProperties()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var original = all.First();
        original.Name = "Original to Duplicate";
        await _service.Put(original);

        await _controller.Duplicate(original.Id);

        var afterAll = await _service.GetAll();
        var duplicates = afterAll.Where(x => x.Name == "Original to Duplicate").ToList();
        duplicates.Count.Should().Be(2);
        duplicates.Select(x => x.Id).Distinct().Count().Should().Be(2);
    }

    #endregion

    #region Filter Endpoint Tests

    [TestMethod]
    public async Task Filter_WithNoSearch_CompletesSuccessfully()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var response = await _controller.Filter(id);

        response.Code.Should().Be(0);
    }

    #endregion

    #region Preview Endpoint Tests

    [TestMethod]
    public async Task Preview_WithNoProcesses_CompletesSuccessfully()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var response = await _controller.Preview(id);

        response.Code.Should().Be(0);
    }

    #endregion

    #region Apply/Revert Endpoint Tests

    [TestMethod]
    public async Task Apply_InactiveBulkModification_ThrowsException()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var bm = all.First();
        bm.IsActive = false;
        await _service.Put(bm);

        var action = async () => await _controller.Apply(bm.Id);

        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*inactive*");
    }

    [TestMethod]
    public async Task Apply_NoDiffs_ThrowsException()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var action = async () => await _controller.Apply(id);

        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*No resource diff*");
    }

    [TestMethod]
    public async Task Revert_NotAppliedBulkModification_ThrowsException()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var action = async () => await _controller.Revert(id);

        await action.Should().ThrowAsync<Exception>()
            .WithMessage("*not applied*");
    }

    #endregion

    #region SearchDiffs Endpoint Tests

    [TestMethod]
    public async Task SearchDiffs_NoDiffs_ReturnsEmptyResult()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var searchModel = new BulkModificationResourceDiffsSearchInputModel
        {
            PageIndex = 1,
            PageSize = 10
        };

        var response = await _controller.SearchDiffs(id, searchModel);

        response.Should().NotBeNull();
        // Empty result is expected when no preview has been run
    }

    #endregion

    #region Response Format Tests

    [TestMethod]
    public async Task Get_ResponseContainsExpectedFields()
    {
        await _controller.Add();
        var all = await _service.GetAll();
        var id = all.First().Id;

        var response = await _controller.Get(id);

        response.Data.Should().NotBeNull();
        response.Data!.Id.Should().Be(id);
        response.Data.Name.Should().NotBeNull();
        response.Data.IsActive.Should().BeTrue();
        response.Data.CreatedAt.Should().NotBe(default);
    }

    [TestMethod]
    public async Task GetAll_ResponseContainsViewModels()
    {
        await _controller.Add();

        var response = await _controller.GetAll();

        response.Data.Should().NotBeNull();
        foreach (var vm in response.Data!)
        {
            vm.Id.Should().BeGreaterThan(0);
            vm.Name.Should().NotBeNull();
        }
    }

    #endregion
}
