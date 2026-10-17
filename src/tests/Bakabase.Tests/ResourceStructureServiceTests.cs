using System.Threading;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.TestKit.Utils;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Tests;

/// <summary>
/// Runs the real hierarchy service over persisted paths without requiring those paths to
/// exist on the host. Windows path regressions must also run on Linux and macOS CI.
/// </summary>
[TestClass]
public sealed class ResourceStructureServiceTests
{
    private IServiceProvider _services = null!;

    [TestInitialize]
    public async Task Setup() => _services = await TestServiceBuilder.BuildServiceProvider();

    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Rebuild_WindowsPaths_FindNearestResourceAndIgnoreSiblingPrefixes(bool nativeSeparators)
    {
        var paths = nativeSeparators
            ? new[]
            {
                @"D:\动画/某部动画\第一季\",
                @"d:/动画\某部动画/第一季\子文件夹1",
                @"D:\动画\某部动画/第一季\未标记目录/子文件夹2\",
                @"D:\动画/某部动画\第一季\子文件夹1/孙文件夹",
                @"D:\动画\某部动画\第一季特别篇",
                @"D:\动画\某部动画\第二季"
            }
            : new[]
            {
                "D:/动画/某部动画/第一季",
                "D:/动画/某部动画/第一季/子文件夹1",
                "D:/动画/某部动画/第一季/未标记目录/子文件夹2",
                "D:/动画/某部动画/第一季/子文件夹1/孙文件夹",
                "D:/动画/某部动画/第一季特别篇",
                "D:/动画/某部动画/第二季"
            };
        var resources = await Seed(paths);

        await Rebuild();

        var actual = (await ResourceService.GetAll()).ToDictionary(r => r.Id);
        actual[resources[0].Id].ParentId.Should().BeNull();
        actual[resources[1].Id].ParentId.Should().Be(resources[0].Id);
        actual[resources[2].Id].ParentId.Should().Be(resources[0].Id,
            "unmarked intermediate directories do not break the resource hierarchy");
        actual[resources[3].Id].ParentId.Should().Be(resources[1].Id,
            "the nearest existing resource takes precedence over a more distant ancestor");
        actual[resources[4].Id].ParentId.Should().BeNull("a common string prefix is not an ancestor");
        actual[resources[5].Id].ParentId.Should().BeNull();
        actual.Values.Where(r => r.HasChildren).Select(r => r.Id).Should()
            .BeEquivalentTo(resources.Take(2).Select(r => r.Id));
        actual.Values.Should().OnlyContain(r => r.ParentId != r.Id);

        // A second rebuild must preserve the same tree and tags.
        await Rebuild();
        (await ResourceService.GetAll()).Select(r => (r.Id, r.ParentId, r.HasChildren)).Should()
            .BeEquivalentTo(actual.Values.Select(r => (r.Id, r.ParentId, r.HasChildren)));
    }

    [TestMethod]
    [DataRow("D:/", "D:/动画", "D:/动画/第一季")]
    [DataRow(@"D:\", @"D:\动画", @"D:/动画\第一季")]
    [DataRow("/", "/动画", "/动画/第一季")]
    [DataRow("//server/share", "//server/share/动画", "//server/share/动画/第一季")]
    [DataRow(@"\\server\share\", @"\\server\share\动画", @"//server/share/动画\第一季")]
    public async Task Rebuild_AbsoluteRoots_AreParentsWithoutBecomingTheirOwnAncestor(
        string root, string parent, string child)
    {
        var resources = await Seed(root, parent, child);
        // Simulate stale metadata so rebuilding must also clear a root's invalid parent/tag.
        resources[0].ParentId = resources[0].Id;
        resources[2].Tags = new HashSet<ResourceTag> { ResourceTag.IsParent };
        await ResourceService.AddOrPutRange(resources);

        await Rebuild();

        var actual = (await ResourceService.GetAll()).ToDictionary(r => r.Id);
        actual[resources[0].Id].ParentId.Should().BeNull();
        actual[resources[1].Id].ParentId.Should().Be(resources[0].Id);
        actual[resources[2].Id].ParentId.Should().Be(resources[1].Id);
        actual[resources[0].Id].HasChildren.Should().BeTrue();
        actual[resources[1].Id].HasChildren.Should().BeTrue();
        actual[resources[2].Id].HasChildren.Should().BeFalse("stale parent tags must be removed");
        actual.Values.Should().OnlyContain(r => r.ParentId != r.Id);
    }

    [TestMethod]
    public async Task Rebuild_UncPaths_DoNotCrossTheShareRoot()
    {
        var resources = await Seed("//server", "//server/share", "//server/share/动画",
            "//server/share-other/动画");

        await Rebuild();

        var actual = (await ResourceService.GetAll()).ToDictionary(r => r.Id);
        actual[resources[0].Id].ParentId.Should().BeNull();
        actual[resources[1].Id].ParentId.Should().BeNull("a UNC share is a filesystem root");
        actual[resources[2].Id].ParentId.Should().Be(resources[1].Id);
        actual[resources[3].Id].ParentId.Should().BeNull("a resource in another share has no matching ancestor");
    }

    private IResourceService ResourceService => _services.GetRequiredService<IResourceService>();

    private Task Rebuild() => _services.GetRequiredService<ResourceStructureService>()
        .RebuildParentChildRelationships(CancellationToken.None);

    private async Task<List<Resource>> Seed(params string[] paths)
    {
        var resources = paths.Select(path => new Resource
        {
            Path = path,
            FileCreatedAt = DateTime.Now,
            FileModifiedAt = DateTime.Now
        }).ToList();
        await ResourceService.AddOrPutRange(resources);
        return resources;
    }
}
