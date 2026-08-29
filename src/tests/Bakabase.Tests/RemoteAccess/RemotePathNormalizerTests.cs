using System;
using System.IO;
using Bakabase.Modules.RemoteAccess.Components;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Bakabase.Tests.RemoteAccess;

/// <summary>
/// The normalizer decides what a client-supplied path actually points at. A bug
/// here is an arbitrary-file-read for every paired device, so the traversal and
/// boundary cases are pinned down explicitly.
/// </summary>
[TestClass]
public class RemotePathNormalizerTests
{
    /// <summary>
    /// Builds an absolute path valid on the host running the test, so the same
    /// assertions hold on Windows and POSIX.
    /// </summary>
    private static string Rooted(params string[] segments) =>
        Path.Combine([Path.GetPathRoot(Path.GetTempPath())!, .. segments]);

    [TestMethod]
    public void Normalize_Rejects_NullEmptyAndWhitespace()
    {
        Assert.IsNull(RemotePathNormalizer.Normalize(null));
        Assert.IsNull(RemotePathNormalizer.Normalize(""));
        Assert.IsNull(RemotePathNormalizer.Normalize("   "));
    }

    [TestMethod]
    public void Normalize_Rejects_RelativePaths()
    {
        // A relative path has no meaning without a base, and silently resolving it
        // against the process's working directory would be a way out of the roots.
        Assert.IsNull(RemotePathNormalizer.Normalize("media/movie.mkv"));
        Assert.IsNull(RemotePathNormalizer.Normalize("../../etc/passwd"));
    }

    [TestMethod]
    public void Normalize_Rejects_EmbeddedNul()
    {
        Assert.IsNull(RemotePathNormalizer.Normalize("/media/movie.mkv\0.txt"));
    }

    [TestMethod]
    public void Normalize_Collapses_DotDotSegments()
    {
        var normalized = RemotePathNormalizer.Normalize(Rooted("media", "library", "..", "..", "secrets.txt"));
        Assert.IsNotNull(normalized);
        Assert.IsFalse(normalized!.Contains(".."), $"'..' survived normalization: {normalized}");
        StringAssert.EndsWith(normalized, "secrets.txt");
    }

    [TestMethod]
    public void Normalize_Is_Idempotent()
    {
        var once = RemotePathNormalizer.Normalize(Rooted("media", "library", "movie.mkv"));
        var twice = RemotePathNormalizer.Normalize(once);
        Assert.AreEqual(once, twice);
    }

    [TestMethod]
    public void StripArchiveEntry_Returns_ArchivePath()
    {
        Assert.AreEqual("/media/lib/book.zip",
            RemotePathNormalizer.StripArchiveEntry("/media/lib/book.zip!chapter1/page01.jpg"));
    }

    [TestMethod]
    public void StripArchiveEntry_Leaves_PlainPathsAlone()
    {
        Assert.AreEqual("/media/lib/movie.mkv", RemotePathNormalizer.StripArchiveEntry("/media/lib/movie.mkv"));
        // A bang that is not the archive separator must not truncate the path.
        Assert.AreEqual("/media/lib/what!.mkv", RemotePathNormalizer.StripArchiveEntry("/media/lib/what!.mkv"));
    }

    [TestMethod]
    public void Normalize_Judges_ArchiveEntry_ByItsArchive()
    {
        var archive = Rooted("media", "library", "book.zip");
        var entry = archive + "!chapter1/page01.jpg";

        Assert.AreEqual(RemotePathNormalizer.Normalize(archive), RemotePathNormalizer.Normalize(entry));
    }

    [TestMethod]
    public void IsUnder_Accepts_TheRootItselfAndItsChildren()
    {
        Assert.IsTrue(RemotePathNormalizer.IsUnder("/media/library", "/media/library"));
        Assert.IsTrue(RemotePathNormalizer.IsUnder("/media/library/anime/ep1.mkv", "/media/library"));
    }

    [TestMethod]
    public void IsUnder_Rejects_SiblingWithSharedPrefix()
    {
        // The classic prefix bug: /media/library-private must not pass as a child of
        // /media/library just because the string starts the same way.
        Assert.IsFalse(RemotePathNormalizer.IsUnder("/media/library-private/tax.pdf", "/media/library"));
        Assert.IsFalse(RemotePathNormalizer.IsUnder("/media/librarian", "/media/library"));
    }

    [TestMethod]
    public void IsUnder_Rejects_Parent()
    {
        Assert.IsFalse(RemotePathNormalizer.IsUnder("/media", "/media/library"));
        Assert.IsFalse(RemotePathNormalizer.IsUnder("/", "/media/library"));
    }

    [TestMethod]
    public void IsUnder_Handles_RootEndingInSeparator()
    {
        // Drive roots ("Z:/") and POSIX root ("/") already carry the separator.
        Assert.IsTrue(RemotePathNormalizer.IsUnder("/media/library/x.mkv", "/"));
        Assert.IsTrue(RemotePathNormalizer.IsUnder("Z:/anime/x.mkv", "Z:/"));
    }

    [TestMethod]
    public void IsUnder_Rejects_EmptyInputs()
    {
        Assert.IsFalse(RemotePathNormalizer.IsUnder("", "/media"));
        Assert.IsFalse(RemotePathNormalizer.IsUnder("/media/x", ""));
    }

    [TestMethod]
    public void IsUnder_Follows_HostCaseRules()
    {
        var differsOnlyByCase = RemotePathNormalizer.IsUnder("/media/library/x.mkv", "/media/LIBRARY");

        // On a case-insensitive filesystem the two names are the same directory; on a
        // case-sensitive one they are different directories and must not match.
        Assert.AreEqual(OperatingSystem.IsWindows(), differsOnlyByCase);
    }
}
