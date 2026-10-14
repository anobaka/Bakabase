using System;
using System.Linq;
using Bakabase.Service.Components.RemoteAccess;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests;

/// <summary>
/// Locks the set of actions that must run on the machine the user is sitting at.
/// </summary>
/// <remarks>
/// Both directions of drift are bugs, and they fail differently:
/// <list type="bullet">
/// <item>
/// Losing a marker sends the action to whatever machine the server runs on. In the
/// container build that means a player starting on a screen nobody is watching, and
/// the caller being told it worked.
/// </item>
/// <item>
/// Adding one silently makes the action unavailable to every remote caller, which
/// looks like the feature is broken.
/// </item>
/// </list>
/// Neither should ever happen as a side effect of an unrelated change, so the set is
/// a snapshot: changing it means editing this list on purpose.
/// </remarks>
[TestClass]
public class UserMachineEndpointCatalogTests
{
    private static readonly string[] Expected =
    [
        "GET /file/icon",
        "GET /file/recycle-bin",
        "GET /gui/url",
        "GET /player/device-playback/preferences",
        "PUT /player/device-playback/preferences",
        "GET /player/playlist/{playlistId:int}/batch-play/candidates",
        "GET /player/resource-profile",
        "GET /resource/directory",
        "GET /resource/play/random",
        "GET /resource/{resourceId}/play",
        "GET /resource/{resourceId}/play-item",
        "GET /tampermonkey/install",
        "GET /tool/open",
        "GET /tool/open-file",
        "POST /aigc/artifacts/{id:int}/open",
        "POST /dlsite-work/{workId}/launch",
        "POST /player/batch-play",
        "POST /player/batch-play/candidates",
        "POST /player/playlist/{playlistId:int}/batch-play",
        "POST /player/resource-profiles",
        "POST /tool/cookie-capture",
    ];

    [TestMethod]
    public void Catalog_matches_the_golden_set()
    {
        var actual = UserMachineEndpointCatalog.Entries.Select(e => e.Key).ToArray();

        var missing = Expected.Except(actual, StringComparer.OrdinalIgnoreCase).ToArray();
        var unexpected = actual.Except(Expected, StringComparer.OrdinalIgnoreCase).ToArray();

        Assert.IsTrue(
            missing.Length == 0 && unexpected.Length == 0,
            $"user-machine endpoint set drifted.{Environment.NewLine}" +
            $"lost the marker: {string.Join(", ", missing)}{Environment.NewLine}" +
            $"newly marked: {string.Join(", ", unexpected)}");
    }

    [TestMethod]
    public void Every_entry_carries_a_reason_the_user_can_read()
    {
        // The reason is what a remote caller gets instead of the action running, so an
        // empty one leaves them staring at a refusal with no explanation.
        var without = UserMachineEndpointCatalog.Entries
            .Where(e => string.IsNullOrWhiteSpace(e.Reason))
            .Select(e => e.Key)
            .ToArray();

        Assert.AreEqual(0, without.Length, $"missing a reason: {string.Join(", ", without)}");
    }

    [TestMethod]
    public void Keys_are_unique()
    {
        // A duplicate key means the forwarder's lookup would be ambiguous.
        var duplicates = UserMachineEndpointCatalog.Entries
            .GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToArray();

        Assert.AreEqual(0, duplicates.Length, $"duplicate keys: {string.Join(", ", duplicates)}");
    }
}
