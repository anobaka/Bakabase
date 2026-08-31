using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Events;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Input;
using Bakabase.Abstractions.Services;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// ChangeMediaLibrary rewrites resource paths and media-library membership — both indexed —
/// so it must publish resource-changed events the same way ChangePath does; a silent update
/// leaves the search index, profile index and UI push stale.
/// </summary>
[TestClass]
public sealed class ResourceServiceChangeMediaLibraryTests
{
    private string _testRoot = null!;
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
        _testRoot = Path.Combine(
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location)!,
            $"ChangeMediaLibraryTests.{DateTime.Now:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testRoot);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_testRoot))
        {
            try { Directory.Delete(_testRoot, true); } catch { }
        }
    }

    [TestMethod]
    public async Task ChangeMediaLibrary_UpdatesPathAndPublishesResourceChangeEvents()
    {
        var resourceService = _sp.GetRequiredService<IResourceService>();
        var oldPath = Path.Combine(_testRoot, "A").Replace('\\', '/');

        await resourceService.AddOrPutRange([new Resource { Path = oldPath }]);
        var resource = (await resourceService.GetAll()).Single();

        var library = await _sp.GetRequiredService<IMediaLibraryV2Service>()
            .Add(new MediaLibraryV2AddOrPutInputModel("lib", [_testRoot.Replace('\\', '/')]));

        // Subscribe after seeding so the assertion only sees ChangeMediaLibrary's publishes.
        var changedIds = new List<int>();
        _sp.GetRequiredService<IResourceDataChangeEvent>().OnResourceDataChanged +=
            args => changedIds.AddRange(args.ResourceIds);

        var newPath = Path.Combine(_testRoot, "B").Replace('\\', '/');
        var rsp = await resourceService.ChangeMediaLibrary([resource.Id], library.Id,
            new Dictionary<int, string> { [resource.Id] = newPath });

        rsp.Code.Should().Be(0);
        changedIds.Should().Contain(resource.Id);
        (await resourceService.Get(resource.Id))!.Path.Should().Be(newPath);
    }
}
