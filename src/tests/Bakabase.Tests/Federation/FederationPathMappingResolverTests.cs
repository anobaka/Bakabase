using System.Text.Json;
using Bakabase.Modules.Federation.Contracts;
using Bakabase.Modules.Federation.Media;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Service.Components.Federation;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.Federation;

[TestClass]
public sealed class FederationPathMappingResolverTests
{
    private static readonly string WorkRoot = "g" + new string('a',64);
    private static readonly string ShareRoot = "g" + new string('b',64);
    private static readonly string OtherRoot = "g" + new string('c',64);
    [TestMethod]
    public void GroupMappingAppliesToMediaAndResourceDirectories()
    {
        using var directory = new FederationBrowsingControlTests.StateDirectory();
        var work = Directory.CreateDirectory(Path.Combine(directory.Ensure(), "Movies", "Work")).FullName;
        var image = Path.Combine(work, "cover.jpg");
        File.WriteAllText(image, "fixture");
        var asset = Asset() with { RelativePath="cover.jpg", MappingLocations = [new("legacy","cover.jpg"), new(WorkRoot, "cover.jpg"), new(ShareRoot, "Movies/Work/cover.jpg")] };
        Assert.AreEqual(image, FederationPathMappingResolver.MapAsset(asset, [new(ShareRoot, directory.Path)]));
        var location = new FederatedResourceLocation("legacy", ".", true,
            [new("legacy","."), new(WorkRoot, "."), new(ShareRoot, "Movies/Work")]);
        Assert.AreEqual(work, FederationPathMappingResolver.MapLocation(location, [new(ShareRoot, directory.Path)]).Path);
        Assert.AreEqual(MediaPathBoundary.ResolvePhysical(work), FederationPathMappingResolver.MapLocation(location, [new(WorkRoot, work)]).Path);
    }

    [TestMethod]
    public void ExactLegacyAndSpecificOverrideWinEvenWhenTheirFileIsMissing()
    {
        using var directory = new FederationBrowsingControlTests.StateDirectory();
        var root = directory.Ensure();
        var work = Directory.CreateDirectory(Path.Combine(root, "Work")).FullName;
        File.WriteAllText(Path.Combine(work, "image.jpg"), "fixture");
        var missing = Path.Combine(root, "missing");
        var asset = Asset() with { MappingLocations = [new("legacy","image.jpg"), new(WorkRoot, "image.jpg"), new(ShareRoot, "Work/image.jpg")] };
        Assert.IsNull(FederationPathMappingResolver.MapAsset(asset,
            [new("legacy", missing), new(WorkRoot, work), new(ShareRoot, root)]));
        Assert.IsNull(FederationPathMappingResolver.MapAsset(asset, [new(WorkRoot, missing), new(ShareRoot, root)]));
        Assert.AreEqual(Path.Combine(work, "image.jpg"), FederationPathMappingResolver.MapAsset(asset, [new(ShareRoot, root)]));
        var location = new FederatedResourceLocation("legacy", ".", true,
            [new("legacy","."), new(WorkRoot, "."), new(ShareRoot, "Work")]);
        var unavailable=FederationPathMappingResolver.MapLocation(location,[new(WorkRoot,missing),new(ShareRoot,root)]);
        Assert.IsNull(unavailable.Path);
        Assert.AreEqual("MappedPathUnavailable",unavailable.Reason);
        Assert.AreEqual("PathMappingRequired",FederationPathMappingResolver.MapLocation(location,[]).Reason);
    }

    [TestMethod]
    public void NewGroupIdsRemainSafeForOldPeersAndExistingLegacyConfiguration()
    {
        var legacyJson = """{"assetId":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","kind":"image","fileName":"image.jpg","contentType":"image/jpeg","sourceRootId":"legacy","relativePath":"image.jpg","expiresAt":"2099-10-13T00:00:00Z"}""";
        var asset = JsonSerializer.Deserialize<FederatedAsset>(legacyJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        Assert.IsNull(asset.MappingLocations);
        Assert.IsNull(asset.CanOpenLocally);
        using var directory = new FederationBrowsingControlTests.StateDirectory();
        var file = Path.Combine(directory.Ensure(), "image.jpg");
        File.WriteAllText(file, "fixture");
        Assert.AreEqual(file, FederationPathMappingResolver.MapAsset(asset, [new("legacy", directory.Path)]));
        // An old source response has no new group candidates, so a group mapping cannot accidentally
        // apply the legacy resource-relative path at a parent share's local root.
        Assert.IsNull(FederationPathMappingResolver.MapAsset(asset, [new(ShareRoot, directory.Path)]));
        var updated = asset with { MappingLocations = [new("legacy","image.jpg"),new(ShareRoot, "image.jpg")] };
        var oldClientView = updated with { MappingLocations = null };
        Assert.IsNull(FederationPathMappingResolver.MapAsset(oldClientView, [new(ShareRoot, directory.Path)]));
    }

    [TestMethod]
    public void NestedAssetOverridePrecedesLegacyResourceMappingAndMissingCopyDoesNotFallBack()
    {
        using var directory = new FederationBrowsingControlTests.StateDirectory();
        var album = Directory.CreateDirectory(Path.Combine(directory.Ensure(),"album")).FullName;
        var photos = Directory.CreateDirectory(Path.Combine(album,"photos")).FullName;
        File.WriteAllText(Path.Combine(photos,"image.jpg"),"fixture");
        var locations = FederationMappingPaths.Locations("/share/album","/share/album/photos/image.jpg",
            ["/share"],[],"legacy","photos/image.jpg");
        Assert.AreEqual(FederationMappingPaths.GroupId("/share/album/photos"),locations[0].SourceRootId);
        Assert.AreEqual("legacy",locations[1].SourceRootId);
        var asset = Asset() with { RelativePath="photos/image.jpg", MappingLocations=locations };
        Assert.IsNull(FederationPathMappingResolver.MapAsset(asset,
            [new(locations[0].SourceRootId,Path.Combine(directory.Path,"missing")),new("legacy",album)]));
        Assert.AreEqual(Path.Combine(photos,"image.jpg"),FederationPathMappingResolver.MapAsset(asset,[new("legacy",album)]));
    }

    [TestMethod]
    public void RenewalCannotGrowPastSharedMediaBudgetAndFailurePreservesOriginalTicket()
    {
        var sessions = new FederationMediaSessions();
        var reference = new AssetRef(new("source","epoch",1),new string('a',64));
        var original = new AvailableFederationAsset(reference,Asset(),null);
        var ticket = sessions.Issue(original,null);
        var locations = Enumerable.Range(0,63).Select(x => new FederatedMappingLocation("g"+x.ToString("x64"),new string('x',4086)+"/image.jpg"))
            .Prepend(new("legacy","image.jpg")).ToArray();
        FederationPathMappingResolver.ValidateLocations(locations,"legacy","image.jpg");
        var enlarged = original with { Asset=original.Asset with { MappingLocations=locations } };
        while (true)
        {
            try { sessions.Issue(enlarged,null); }
            catch (FederationQueryException e) { Assert.AreEqual("Busy",e.Code); break; }
        }
        while (true)
        {
            try { sessions.Issue(original,null); }
            catch (FederationQueryException e) { Assert.AreEqual("Busy",e.Code); break; }
        }
        var bytesBefore=(long)typeof(FederationMediaSessions).GetField("_ticketBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(sessions)!;
        var countBefore=((System.Collections.IDictionary)typeof(FederationMediaSessions).GetField("_tickets",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(sessions)!).Count;
        var error=Assert.ThrowsExactly<FederationQueryException>(()=>sessions.Renew(ticket,enlarged));
        Assert.AreEqual("Busy",error.Code);
        Assert.AreSame(original,sessions.GetTicket(ticket.Id).Source);
        Assert.AreEqual(bytesBefore,(long)typeof(FederationMediaSessions).GetField("_ticketBytes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(sessions)!);
        Assert.AreEqual(countBefore,((System.Collections.IDictionary)typeof(FederationMediaSessions).GetField("_tickets",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.GetValue(sessions)!).Count);
        // A rejected growth must not corrupt accounting or prevent a same-size renewal.
        sessions.Renew(ticket,original);
        Assert.AreSame(original,sessions.GetTicket(ticket.Id).Source);
    }

    [TestMethod]
    public void MalformedOversizedOrDuplicateCandidatesAreRefusedBeforeRetention()
    {
        var sessions = new FederationMediaSessions();
        var asset = Asset();
        foreach (var locations in new FederatedMappingLocation[][]
                 {
                     [new("../root", "image.jpg")], [new(OtherRoot, new string('x',4097))],
                     [new(OtherRoot, "image.jpg"),new(OtherRoot, "another.jpg")],
                     Enumerable.Range(0,65).Select(x => new FederatedMappingLocation("g"+x,"image.jpg")).ToArray()
                 })
        {
            var detail = new FederatedResourceDetail(new("source", "epoch", 1), "Source", "Image", "image.jpg", "HasFile",
                [], [], [], [], [asset with { MappingLocations = locations }], null);
            Assert.AreEqual("InvalidPeerResponse", Assert.ThrowsExactly<FederationQueryException>(() => sessions.Remember(detail, null)).Code);
        }
    }

    [TestMethod]
    public void TraversalAndSymlinkEscapeStillFailWithAnAncestorMapping()
    {
        using var directory = new FederationBrowsingControlTests.StateDirectory();
        var root = Directory.CreateDirectory(Path.Combine(directory.Ensure(), "inside")).FullName;
        var outside = Path.Combine(directory.Path, "outside.jpg");
        File.WriteAllText(outside, "fixture");
        var asset = Asset() with { MappingLocations = [new("legacy","image.jpg"),new(ShareRoot, "../outside.jpg")] };
        Assert.AreEqual("InvalidPeerResponse",Assert.ThrowsExactly<FederationQueryException>(()=>
            FederationPathMappingResolver.MapAsset(asset,[new(ShareRoot,root)])).Code);
        if (!OperatingSystem.IsWindows())
        {
            File.CreateSymbolicLink(Path.Combine(root,"image.jpg"), outside);
            Assert.IsNull(FederationPathMappingResolver.MapAsset(asset with { MappingLocations=[new("legacy","image.jpg"),new(ShareRoot,"image.jpg")] }, [new(ShareRoot, root)]));
        }
    }

    private static FederatedAsset Asset() => new(new string('a',64), "image", "image.jpg", "image/jpeg",
        "legacy", "image.jpg", DateTimeOffset.UtcNow.AddMinutes(5));
}
