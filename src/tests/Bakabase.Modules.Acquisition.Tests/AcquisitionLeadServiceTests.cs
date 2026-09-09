using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Extensions;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.TestKit.Utils;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Modules.Acquisition.Tests;

/// <summary>
/// Acquisition leads answer "where can I get this resource". The rule that shapes the whole model:
/// a shared link describes exactly one resource, while a platform the user holds the resource on is
/// an identity rather than a lead and is derived from the source links instead of being stored.
/// </summary>
[TestClass]
public sealed class AcquisitionLeadServiceTests
{
    private IServiceProvider _sp = null!;

    [TestInitialize]
    public async Task Setup()
    {
        _sp = await TestServiceBuilder.BuildServiceProvider();
    }

    private IAcquisitionLeadService Service => _sp.GetRequiredService<IAcquisitionLeadService>();
    private IResourceService ResourceService => _sp.GetRequiredService<IResourceService>();

    /// <summary>
    /// The shape a resource has before it is obtained: an identity, no local files.
    /// </summary>
    private async Task<Resource> AddResource(string name, ResourceSource? source = null,
        string? sourceKey = null)
    {
        var resource = new Resource
        {
            Path = null,
            IsFile = false,
            Status = ResourceStatus.Active,
            DisplayName = name,
            FileCreatedAt = DateTime.Now,
            FileModifiedAt = DateTime.Now,
            SourceLinks = source == null
                ? null
                : [new ResourceSourceLink { Source = source.Value, SourceKey = sourceKey! }]
        };

        // AddOrPutRange writes the assigned id back onto the instance it was handed; the display
        // name is not persisted, so it is no use for finding the row again.
        await ResourceService.AddOrPutRange([resource]);

        return resource;
    }

    private static AcquisitionLeadAddInputModel SharedPage(string url) => new()
    {
        Kind = AcquisitionLeadKind.SharedPage,
        Value = url,
        Origin = AcquisitionLeadOrigin.User
    };

    [TestMethod]
    public async Task Add_AttachesTheLinkAndReadsItBack()
    {
        var resource = await AddResource("a work");

        var result = await Service.Add(resource.Id, SharedPage("https://example.com/thread/1"));

        Assert.IsNotNull(result.Lead);
        Assert.IsNull(result.ConflictingResourceId);

        var leads = await Service.GetByResourceId(resource.Id);
        Assert.AreEqual(1, leads.Count);
        Assert.AreEqual(AcquisitionLeadKind.SharedPage, leads[0].Kind);
        Assert.AreEqual("https://example.com/thread/1", leads[0].Value);
        Assert.IsFalse(leads[0].IsDerived);
    }

    [TestMethod]
    public async Task Add_SameLinkOnASecondResource_IsRefusedAndNamesTheOneHoldingIt()
    {
        var first = await AddResource("first");
        var second = await AddResource("second");
        const string url = "https://example.com/thread/42";

        await Service.Add(first.Id, SharedPage(url));
        var result = await Service.Add(second.Id, SharedPage(url));

        Assert.IsNull(result.Lead, "the link cannot describe two resources");
        Assert.AreEqual(first.Id, result.ConflictingResourceId,
            "the caller has to be told which resource already claims it");
        Assert.AreEqual(0, (await Service.GetByResourceId(second.Id)).Count);
    }

    [TestMethod]
    public async Task Add_SameLinkOnTheSameResource_IsANoOp()
    {
        var resource = await AddResource("a work");
        const string url = "https://example.com/thread/7";

        var first = await Service.Add(resource.Id, SharedPage(url));
        var again = await Service.Add(resource.Id, SharedPage(url));

        Assert.IsNotNull(again.Lead);
        Assert.IsNull(again.ConflictingResourceId);
        Assert.AreEqual(first.Lead!.Id, again.Lead!.Id, "re-importing a list must not pile up rows");
        Assert.AreEqual(1, (await Service.GetByResourceId(resource.Id)).Count);
    }

    [TestMethod]
    public async Task Add_NormalizesTheLinkSoSpellingDoesNotDefeatDeduplication()
    {
        var first = await AddResource("first");
        var second = await AddResource("second");

        await Service.Add(first.Id, SharedPage("https://Example.COM/thread/9"));
        var result = await Service.Add(second.Id, SharedPage("  https://example.com/thread/9  "));

        Assert.AreEqual(first.Id, result.ConflictingResourceId,
            "the scheme and host are case-insensitive by definition, so this is the same link");

        var found = await Service.FindByValue(AcquisitionLeadKind.SharedPage, "HTTPS://EXAMPLE.COM/thread/9");
        Assert.IsNotNull(found);
        Assert.AreEqual(first.Id, found.ResourceId);
    }

    [TestMethod]
    public void NormalizeLeadValue_LeavesThePathAlone()
    {
        // Hosts are case-insensitive; paths are not, and a server may well serve different things
        // at /Work and /work.
        Assert.AreEqual("https://example.com/Work/1", "https://Example.com/Work/1".NormalizeLeadValue());
        Assert.AreEqual("magnet:?xt=urn:btih:ABCDEF", "  magnet:?xt=urn:btih:ABCDEF  ".NormalizeLeadValue());
    }

    [TestMethod]
    public async Task Add_RefusesKindsThatAreNotStored()
    {
        var resource = await AddResource("a work");

        foreach (var kind in new[] { AcquisitionLeadKind.PlatformHolding, AcquisitionLeadKind.Manual })
        {
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
                await Service.Add(resource.Id, new AcquisitionLeadAddInputModel
                {
                    Kind = kind,
                    Value = "whatever"
                }));
        }
    }

    [TestMethod]
    public async Task DeletingAResource_ReleasesItsLinks()
    {
        var first = await AddResource("first");
        const string url = "https://example.com/thread/100";
        await Service.Add(first.Id, SharedPage(url));

        await ResourceService.DeleteByKeys([first.Id]);

        var second = await AddResource("second");
        var result = await Service.Add(second.Id, SharedPage(url));

        Assert.IsNotNull(result.Lead,
            "a lead left behind by a deleted resource would hold the link's unique index hostage");
    }

    [TestMethod]
    public async Task PlatformHoldingLeads_AreDerivedFromSourceLinks_NotStored()
    {
        var resource = await AddResource("a bought work", ResourceSource.DLsite, "RJ00000123");

        Assert.AreEqual(0, (await Service.GetByResourceId(resource.Id)).Count,
            "an identity is not stored as a lead");

        var links = await _sp.GetRequiredService<IResourceSourceLinkService>()
            .GetByResourceIds([resource.Id]);
        var holdings = links.Where(l => l.Source.IsPlatformHolding()).ToList();
        Assert.AreEqual(1, holdings.Count);
        Assert.AreEqual("RJ00000123", holdings[0].SourceKey);

        // The distinction the derivation rests on: a path mark is where the files already are, not
        // somewhere they can be fetched from.
        Assert.IsFalse(ResourceSource.PathMark.IsPlatformHolding());
        Assert.IsTrue(ResourceSource.Steam.IsPlatformHolding());
        Assert.IsTrue(ResourceSource.ExHentai.IsPlatformHolding());
    }
}
