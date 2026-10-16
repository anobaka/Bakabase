using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Modules.RemoteAccess.Components.Management;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

[TestClass]
public class ManagedPathMapperRootTests
{
    [DataTestMethod]
    [DataRow("/media/movie.mp4", "/", "Z:\\", "Z:\\media\\movie.mp4")]
    [DataRow("/", "/", "/", "/")]
    [DataRow("/media", "/media", "/", "/")]
    [DataRow("/media/work", "/media", "/", "/work")]
    [DataRow("/media", "/media", "Z:\\", "Z:\\")]
    [DataRow("//NAS/Share/Movie.mp4", "//nas/share", "Z:\\", "Z:\\Movie.mp4")]
    public void Root_and_normalized_unc_tree_prefixes_map_to_absolute_local_paths(
        string path, string prefix, string local, string expected) =>
        Assert.AreEqual(expected, ManagedPathMapper.Map(path, [new(prefix, local)]).LocalPath);

    [DataTestMethod]
    [DataRow("//nas/share/movie.mp4")]
    [DataRow("Y:/movie.mp4")]
    [DataRow("relative/movie.mp4")]
    public void Posix_root_does_not_claim_unc_windows_or_relative_paths(string path) =>
        Assert.IsFalse(ManagedPathMapper.Map(path, [new("/", "/mounted")]).Mapped);

    [TestMethod]
    public void Nested_specific_mapping_still_wins_over_the_root() =>
        Assert.AreEqual("Z:\\movie.mp4", ManagedPathMapper.Map("/media/movie.mp4",
            [new("/", "/root"), new("/media", "Z:\\")]).LocalPath);
}
