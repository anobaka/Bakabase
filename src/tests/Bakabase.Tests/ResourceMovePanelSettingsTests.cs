using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.TestKit.Implementations;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.InsideWorld.Business.Components.ResourceMove;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

[TestClass]
public class ResourceMovePanelSettingsTests
{
    [TestMethod]
    public async Task StaleClient_CannotOverwriteANewerGlobalConflictDecision()
    {
        var settings = new ResourceMovePanelSettings(
            new TestBOptionsManager<ResourceMovePanelOptions>(new ResourceMovePanelOptions()));
        var stale = settings.Get();
        await settings.SetAutoOverwrite(true);
        Assert.IsFalse(await settings.Save(stale));
        Assert.IsTrue(settings.Get().AutoOverwrite);
        Assert.AreEqual(1L, settings.Get().Revision);
    }

    [TestMethod]
    public async Task ReturnedBookmarks_CannotMutateThePersistedSettings()
    {
        var settings = new ResourceMovePanelSettings(
            new TestBOptionsManager<ResourceMovePanelOptions>(new ResourceMovePanelOptions
            {
                Destinations = [new ResourceMoveDestination { Id = "a", Path = Path.GetTempPath(), Scope = "global" }]
            }));
        settings.Get().Destinations[0].Path = "unexpected";
        Assert.AreEqual(Path.GetTempPath(), settings.Get().Destinations[0].Path);
        Assert.IsTrue(await settings.Save(settings.Get()));
        Assert.AreEqual(1L, settings.Get().Revision);
    }

    [TestMethod]
    public void PromoteGlobal_MergesLocalDuplicates_WithoutRemovingTheirRecoveryData()
    {
        var path = Path.Combine(Path.GetTempPath(), "offline-destination");
        var result = ResourceMovePanelSettings.Normalize([
            new ResourceMoveDestination { Id = "local", Path = path, Scope = "tab", TabId = "a" },
            new ResourceMoveDestination { Id = "global", Path = path, Scope = "global", TabId = "ignored" }
        ]);
        Assert.IsTrue(result.Single(x => x.Id == "local").IsDeleted);
        Assert.IsFalse(result.Single(x => x.Id == "global").IsDeleted);
        Assert.IsNull(result.Single(x => x.Id == "global").TabId);
    }

    [TestMethod]
    public void MissingTabOrRelativePath_IsRejected()
    {
        Assert.ThrowsException<ArgumentException>(() => ResourceMovePanelSettings.Normalize([
            new ResourceMoveDestination { Path = Path.GetTempPath(), Scope = "tab" }
        ]));
        Assert.ThrowsException<ArgumentException>(() => ResourceMovePanelSettings.Normalize([
            new ResourceMoveDestination { Path = "relative/path", Scope = "global" }
        ]));
    }

    [TestMethod]
    public void DifferentTabs_KeepIndependentOrder_AndDeletedEntryCanBeRestored()
    {
        var path = Path.Combine(Path.GetTempPath(), "offline-destination");
        var result = ResourceMovePanelSettings.Normalize([
            new ResourceMoveDestination { Id = "old", Path = path, Scope = "tab", TabId = "a", IsDeleted = true },
            new ResourceMoveDestination { Id = "a", Path = path, Scope = "tab", TabId = "a", Order = 5 },
            new ResourceMoveDestination { Id = "b", Path = path, Scope = "tab", TabId = "b", Order = 2 }
        ]);
        Assert.AreEqual(2, result.Count(x => !x.IsDeleted));
        Assert.AreEqual(5, result.Single(x => x.Id == "a").Order);
        Assert.AreEqual(2, result.Single(x => x.Id == "b").Order);
    }
}
