using System.Collections.Generic;
using Bakabase.Remoting.Abstractions.Models;
using Bakabase.Remoting.Components.Paths;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// Turning a path the server named into one this machine can open.
/// </summary>
/// <remarks>
/// Every failure here is silent by nature: the user asks to open a folder and gets a
/// different one, or nothing, with no indication that a translation went wrong. So the
/// cases that could quietly produce a plausible-but-wrong path are the ones worth
/// writing down.
/// </remarks>
[TestClass]
public class ClientPathMapperTests
{
    private static ClientPathMapping Map(string server, string local) =>
        new() {ServerPath = server, LocalPath = local};

    private static string? Result(string serverPath, params ClientPathMapping[] mappings) =>
        ClientPathMapper.Map(serverPath, mappings).LocalPath;

    [TestMethod]
    public void A_posix_server_maps_onto_a_windows_client()
    {
        // The container-plus-desktop case, and the one where forgetting to translate
        // separators produces a path that exists nowhere.
        Assert.AreEqual(@"Z:\media\anime\Show [2024]\ep01.mkv",
            Result("/data/media/anime/Show [2024]/ep01.mkv", Map("/data/media", @"Z:\media")));
    }

    [TestMethod]
    public void A_windows_server_maps_onto_a_posix_client()
    {
        Assert.AreEqual("/mnt/nas/anime/ep01.mkv",
            Result(@"D:\Media\anime\ep01.mkv", Map(@"D:\Media", "/mnt/nas")));
    }

    [TestMethod]
    public void The_library_root_itself_maps_to_the_local_root()
    {
        // "Open this library's folder" names the prefix exactly, and a naive join would
        // leave a trailing separator or a doubled one.
        Assert.AreEqual(@"Z:\media", Result("/data/media", Map("/data/media", @"Z:\media")));
        Assert.AreEqual(@"Z:\media", Result("/data/media/", Map("/data/media", @"Z:\media")));
        Assert.AreEqual(@"Z:\media", Result("/data/media", Map("/data/media/", @"Z:\media\")));
    }

    [TestMethod]
    public void A_sibling_that_merely_starts_the_same_is_not_swallowed()
    {
        // The bug a textual prefix check produces, and the one that sends an opener into
        // a directory with nothing to do with the one asked for.
        Assert.IsNull(Result("/data/media-backup/x", Map("/data/media", @"Z:\media")));
        Assert.IsNull(Result("/datamedia/x", Map("/data/media", @"Z:\media")));
    }

    [TestMethod]
    public void The_longest_matching_mapping_wins()
    {
        // Libraries nest, and both can be mapped to different places. Taking the first
        // match in list order would make the answer depend on what the user added first.
        var mappings = new[]
        {
            Map("/data", @"Z:\everything"),
            Map("/data/media", @"Y:\media")
        };

        Assert.AreEqual(@"Y:\media\a.mkv", Result("/data/media/a.mkv", mappings));
        Assert.AreEqual(@"Z:\everything\other\b.txt", Result("/data/other/b.txt", mappings));

        // And the same however they were listed.
        Assert.AreEqual(@"Y:\media\a.mkv", Result("/data/media/a.mkv", mappings[1], mappings[0]));
    }

    [TestMethod]
    public void A_windows_server_path_matches_regardless_of_case()
    {
        // Windows filesystems do not distinguish it, and the server's stored casing is
        // whatever the scan happened to see.
        Assert.AreEqual("/mnt/nas/ep01.mkv", Result(@"d:\media\ep01.mkv", Map(@"D:\Media", "/mnt/nas")));
        Assert.AreEqual("/mnt/nas/ep01.mkv", Result(@"D:/MEDIA/ep01.mkv", Map(@"d:\media", "/mnt/nas")));
    }

    [TestMethod]
    public void A_posix_server_path_does_not()
    {
        // Two files differing only in case are two files there, and folding them would
        // open the wrong one.
        Assert.IsNull(Result("/Data/Media/a.mkv", Map("/data/media", @"Z:\media")));
    }

    [TestMethod]
    public void An_unmapped_path_says_which_path_it_was()
    {
        // The user has to be told which library to set up, not just that something
        // failed.
        var result = ClientPathMapper.Map("/srv/other/a.mkv", [Map("/data/media", @"Z:\media")]);

        Assert.IsFalse(result.Mapped);
        Assert.AreEqual("/srv/other/a.mkv", result.UnmappedServerPath);
    }

    [TestMethod]
    public void Nothing_is_invented_when_there_are_no_mappings()
    {
        Assert.IsFalse(ClientPathMapper.Map("/data/media/a.mkv", []).Mapped);
        Assert.IsFalse(ClientPathMapper.Map(null, [Map("/data", "/x")]).Mapped);
        Assert.IsFalse(ClientPathMapper.Map("   ", [Map("/data", "/x")]).Mapped);
    }

    [TestMethod]
    public void A_half_filled_mapping_is_ignored_rather_than_applied()
    {
        // A row the user started and did not finish must not silently map everything to
        // an empty root.
        var mappings = new List<ClientPathMapping>
        {
            new() {ServerPath = "/data", LocalPath = ""},
            new() {ServerPath = "", LocalPath = @"Z:\media"},
            Map("/data/media", @"Z:\media")
        };

        Assert.AreEqual(@"Z:\media\a.mkv", Result("/data/media/a.mkv", [.. mappings]));
        Assert.IsNull(Result("/data/other/a.mkv", [.. mappings]));
    }

    [TestMethod]
    public void A_unc_share_is_a_windows_root()
    {
        Assert.AreEqual(@"\\nas\media\anime\a.mkv",
            Result("/data/media/anime/a.mkv", Map("/data/media", @"\\nas\media")));
    }

    [TestMethod]
    public void Separators_follow_the_root_the_user_wrote_not_the_running_platform()
    {
        // The tests run on Linux and the app ships on Windows; an answer that depended
        // on which would be untestable where it matters most.
        Assert.AreEqual(@"Z:\media\a.mkv", Result("/data/media/a.mkv", Map("/data/media", @"Z:\media")));
        Assert.AreEqual("/mnt/media/a.mkv", Result("/data/media/a.mkv", Map("/data/media", "/mnt/media")));
    }

    [TestMethod]
    public void A_mixed_separator_server_path_still_matches()
    {
        // Paths reach the server from scans, from templates and from users typing them,
        // and they are not always consistent.
        Assert.AreEqual(@"Z:\media\anime\a.mkv",
            Result(@"/data\media/anime\a.mkv", Map("/data/media", @"Z:\media")));
    }
}
