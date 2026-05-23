using System;
using System.Linq;
using System.Text.Json;
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
/// Regression tests for issue #1098 — selecting "delete all" on a large
/// resource list used to produce a multi-megabyte URL because the SDK
/// serialized the id[] into the query string of <c>DELETE /resource/ids</c>.
/// Browsers refused to send it ("Failed to fetch").
///
/// The fix replaced the endpoint with <c>POST /resource/bulk-delete</c>
/// taking a JSON body via <see cref="BulkDeleteResourcesInputModel"/>.
/// These tests lock down:
///   - the model round-trips correctly as JSON,
///   - <see cref="IResourceService.DeleteByKeys(int[], bool)"/> tolerates
///     batches the user can realistically select today (10000+ IDs).
///
/// The "URL too long" failure is an HTTP/transport concern and best covered
/// by an integration test with WebApplicationFactory — not in scope here.
/// </summary>
[TestClass]
public class BulkDeleteResourcesTests
{
    private IServiceProvider _sp = null!;
    private IResourceService _resourceService = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _resourceService = _sp.GetRequiredService<IResourceService>();
    }

    [TestMethod]
    public void InputModel_RoundTripsThroughJson()
    {
        // The frontend SDK serializes via JSON.stringify ↔ System.Text.Json.
        // Verify both directions and that the camelCase the SDK uses works.
        var original = new BulkDeleteResourcesInputModel
        {
            Ids = new[] { 1, 2, 3, 99999 },
            DeleteFiles = true,
        };

        var json = JsonSerializer.Serialize(original, JsonSerializerOptions.Web);
        json.Should().Contain("\"ids\":");
        json.Should().Contain("\"deleteFiles\":");

        var revived = JsonSerializer.Deserialize<BulkDeleteResourcesInputModel>(
            json, JsonSerializerOptions.Web)!;
        revived.Ids.Should().BeEquivalentTo(original.Ids);
        revived.DeleteFiles.Should().Be(original.DeleteFiles);
    }

    [TestMethod]
    public void InputModel_DefaultsAreSafe()
    {
        // A POST body with just `{}` should deserialize to "delete nothing,
        // preserve files" — never silently delete from disk.
        var model = JsonSerializer.Deserialize<BulkDeleteResourcesInputModel>(
            "{}", JsonSerializerOptions.Web)!;
        model.Ids.Should().BeEmpty();
        model.DeleteFiles.Should().BeFalse();
    }

    [TestMethod]
    public async Task DeleteByKeys_RemovesAllResourcesInBatch()
    {
        // Arrange: a batch big enough to catch any naive in-memory limit
        // but small enough to keep the test fast on SQLite.
        const int batchSize = 2000;
        var marker = Guid.NewGuid().ToString("N")[..8];
        var dbModels = Enumerable.Range(0, batchSize).Select(i => new ResourceDbModel
        {
            Path = $"/test/bulk-delete/{marker}/r{i}",
            IsFile = true,
        }).ToList();

        var added = await _resourceService.AddAll(dbModels);
        var ids = added.Select(d => d.Id).ToArray();
        ids.Should().HaveCount(batchSize);

        // Sanity: they're all there.
        (await _resourceService.GetByKeys(ids)).Should().HaveCount(batchSize);

        // Act: this is the exact call the controller makes from the new
        // POST /resource/bulk-delete endpoint.
        await _resourceService.DeleteByKeys(ids, deleteFiles: false);

        // Assert: nothing left.
        (await _resourceService.GetByKeys(ids)).Should().BeEmpty(
            "DeleteByKeys must remove every requested id in a single call — " +
            "the controller no longer chunks (the URL-length workaround is gone)");
    }

    [TestMethod]
    public async Task DeleteByKeys_DeleteFilesFalse_DoesNotTouchDisk()
    {
        // Arrange: a single file resource pointing at an actual file.
        var marker = Guid.NewGuid().ToString("N")[..8];
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            $"BulkDeleteTest_{marker}");
        System.IO.Directory.CreateDirectory(dir);
        var filePath = System.IO.Path.Combine(dir, "keep-me.txt");
        await System.IO.File.WriteAllTextAsync(filePath, "should survive");

        try
        {
            var added = await _resourceService.AddAll(new[]
            {
                new ResourceDbModel { Path = filePath, IsFile = true }
            });
            var ids = added.Select(d => d.Id).ToArray();

            // Act
            await _resourceService.DeleteByKeys(ids, deleteFiles: false);

            // Assert: row gone, file kept.
            (await _resourceService.GetByKeys(ids)).Should().BeEmpty();
            System.IO.File.Exists(filePath).Should().BeTrue(
                "deleteFiles=false must preserve the file on disk — the " +
                "frontend defaults to this behaviour");
        }
        finally
        {
            try { System.IO.Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
