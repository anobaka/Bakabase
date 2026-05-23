using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// Regression tests for issue #1098 — deleting a MediaLibraryV2 used to leave
/// orphan rows in MediaLibraryResourceMapping (the mapping table has no FK
/// cascade). After the fix, IMediaLibraryV2Service.Delete pre-cleans the
/// mapping rows but intentionally keeps the Resource rows so a user can
/// re-bind them if they re-create the library.
/// </summary>
[TestClass]
public class MediaLibraryV2DeleteCascadeTests
{
    private IServiceProvider _sp = null!;
    private IMediaLibraryV2Service _mediaLibraryService = null!;
    private IMediaLibraryResourceMappingService _mappingService = null!;
    private IResourceService _resourceService = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _mediaLibraryService = _sp.GetRequiredService<IMediaLibraryV2Service>();
        _mappingService = _sp.GetRequiredService<IMediaLibraryResourceMappingService>();
        _resourceService = _sp.GetRequiredService<IResourceService>();
    }

    private async Task<int> CreateLibrary(string name) =>
        (await _mediaLibraryService.Add(new MediaLibraryV2AddOrPutInputModel(
            Name: name,
            Paths: new System.Collections.Generic.List<string> { $"/test/{Guid.NewGuid():N}" }))).Id;

    private async Task<int[]> CreateResources(int count)
    {
        var marker = Guid.NewGuid().ToString("N")[..8];
        var dbModels = Enumerable.Range(0, count).Select(i => new ResourceDbModel
        {
            Path = $"/test/cascade/{marker}/r{i}",
            IsFile = true
        }).ToList();
        var added = await _resourceService.AddAll(dbModels);
        return added.Select(d => d.Id).ToArray();
    }

    [TestMethod]
    public async Task Delete_RemovesMappings_KeepsResources()
    {
        // Arrange: 1 library, 3 resources, all 3 mapped to the library
        var libraryId = await CreateLibrary("To-be-deleted");
        var resourceIds = await CreateResources(3);
        await _mappingService.EnsureMappingsRange(
            resourceIds.Select(rid => (rid, libraryId)));

        (await _mappingService.GetByMediaLibraryId(libraryId)).Should().HaveCount(3,
            "the test sets up three mappings before delete");

        // Act: delete the library
        await _mediaLibraryService.Delete(libraryId);

        // Assert: mappings are gone, resources remain
        (await _mappingService.GetByMediaLibraryId(libraryId)).Should().BeEmpty(
            "MediaLibraryV2Service.Delete must cascade-clean the mapping table " +
            "(no DB-level FK exists)");

        var remainingResources = await _resourceService.GetByKeys(resourceIds);
        remainingResources.Should().HaveCount(3,
            "Resource rows are intentionally preserved so the user can re-bind " +
            "them by re-creating the library");
    }

    [TestMethod]
    public async Task Delete_DoesNotAffectOtherLibrariesMappings()
    {
        // Arrange: 2 libraries, each with their own mapping to the same resource
        var libraryA = await CreateLibrary("KeepMe");
        var libraryB = await CreateLibrary("DeleteMe");
        var resourceIds = await CreateResources(2);

        await _mappingService.EnsureMappingsRange(new[]
        {
            (resourceIds[0], libraryA),
            (resourceIds[0], libraryB),
            (resourceIds[1], libraryA),
            (resourceIds[1], libraryB),
        });

        // Act: delete only library B
        await _mediaLibraryService.Delete(libraryB);

        // Assert: only library B's mappings are gone; library A is untouched
        (await _mappingService.GetByMediaLibraryId(libraryB)).Should().BeEmpty();

        var aMappings = await _mappingService.GetByMediaLibraryId(libraryA);
        aMappings.Should().HaveCount(2,
            "deleting library B must not touch library A's mappings");
        aMappings.Select(m => m.ResourceId).Should().BeEquivalentTo(resourceIds);
    }

    [TestMethod]
    public async Task Delete_WithNoMappings_StillRemovesLibrary()
    {
        // Arrange: library with no resources attached
        var libraryId = await CreateLibrary("Empty");

        // Act
        await _mediaLibraryService.Delete(libraryId);

        // Assert: library is gone, no exceptions thrown
        var all = await _mediaLibraryService.GetAll();
        all.Should().NotContain(l => l.Id == libraryId);
    }
}
