using System;
using System.Text.RegularExpressions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.Resolvers;

namespace Bakabase.Tests;

/// <summary>All inputs are persisted path strings; these tests never create or enumerate files.</summary>
[TestClass]
public sealed class ResourceMarkPathMatcherTests
{
    private static ResourceMarkConfig Layer(int value,
        PathMarkApplyScope scope = PathMarkApplyScope.MatchedOnly, PathFilterFsType? type = null) => new()
    {
        MatchMode = PathMatchMode.Layer, Layer = value, ApplyScope = scope, FsTypeFilter = type
    };

    private static ResourceMarkConfig Pattern(string pattern,
        PathMarkApplyScope scope = PathMarkApplyScope.MatchedOnly, PathFilterFsType? type = null) => new()
    {
        MatchMode = PathMatchMode.Regex, Regex = pattern, ApplyScope = scope, FsTypeFilter = type
    };

    [TestMethod]
    public void LayerOnly_MatchesExactDepthAndWholeSegments()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library", Layer(1));
        Assert.IsTrue(matches("/MISSING/library/work", false));
        Assert.IsTrue(matches("/missing/library/work.zip", true));
        Assert.IsFalse(matches("/missing/library", false));
        Assert.IsFalse(matches("/missing/library/work/chapter", false));
        Assert.IsFalse(matches("/missing/library-sibling/work", false));
    }

    [TestMethod]
    public void LayerSubtree_ExpandsFromMatchedDirectories()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library",
            Layer(2, PathMarkApplyScope.MatchedAndSubdirectories));
        Assert.IsFalse(matches("/missing/library/category", false));
        Assert.IsTrue(matches("/missing/library/category/work", false));
        Assert.IsTrue(matches("/missing/library/category/work/chapter/page.jpg", true));
        Assert.IsFalse(matches("/missing/library2/category/work", false));
    }

    [TestMethod]
    public void LayerZero_MatchesDirectoryRootAndOptionalDescendants()
    {
        var only = ResourceMarkPathMatcher.Create("/missing/library", Layer(0));
        Assert.IsTrue(only("/missing/library/", false));
        Assert.IsFalse(only("/missing/library", true));
        Assert.IsFalse(only("/missing/library/work", false));
        var subtree = ResourceMarkPathMatcher.Create("/missing/library",
            Layer(0, PathMarkApplyScope.MatchedAndSubdirectories));
        Assert.IsTrue(subtree("/missing/library/work/file.zip", true));
        Assert.IsFalse(subtree("/missing/library2/file.zip", true));
    }

    [TestMethod]
    public void NegativeLayer_SelectsAncestorDirectoryWithoutFilesystemDiscovery()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library/category", Layer(-2));
        Assert.IsTrue(matches("/missing", false));
        Assert.IsFalse(matches("/missing", true));
        Assert.IsFalse(matches("/missing/library", false));
        Assert.IsFalse(matches("/missing-other", false));
    }

    [TestMethod]
    public void NegativeLayerSubtree_IncludesSiblingBranchesUnderSelectedAncestor()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library/category",
            Layer(-1, PathMarkApplyScope.MatchedAndSubdirectories));
        Assert.IsTrue(matches("/missing/library", false));
        Assert.IsTrue(matches("/missing/library/other/work.zip", true));
        Assert.IsFalse(matches("/missing/library-sibling/work.zip", true));
    }

    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    public void FileOnly_ZeroAndNegativeLayersCannotExpandFromDirectory(int layer)
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library/category",
            Layer(layer, PathMarkApplyScope.MatchedAndSubdirectories, PathFilterFsType.File));
        Assert.IsFalse(matches("/missing/library/category", true));
        Assert.IsFalse(matches("/missing/library/category/work.zip", true));
        Assert.IsFalse(matches("/missing/library/other/work.zip", true));
    }

    [TestMethod]
    public void FileOnly_PositiveLayerDoesNotExpandThroughUnmatchedDirectories()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library",
            Layer(1, PathMarkApplyScope.MatchedAndSubdirectories, PathFilterFsType.File));
        Assert.IsTrue(matches("/missing/library/work.zip", true));
        Assert.IsFalse(matches("/missing/library/work", false));
        Assert.IsFalse(matches("/missing/library/work/page.jpg", true));
    }

    [TestMethod]
    public void Extensions_AreCaseInsensitiveSuffixesAndApplyOnlyToFiles()
    {
        var config = Layer(1, PathMarkApplyScope.MatchedAndSubdirectories);
        config.Extensions = [".tar.gz", "jpg"];
        var matches = ResourceMarkPathMatcher.Create("/missing/library", config);
        Assert.IsTrue(matches("/missing/library/work", false));
        Assert.IsTrue(matches("/missing/library/work/page.JPG", true));
        Assert.IsTrue(matches("/missing/library/archive.TAR.GZ", true));
        Assert.IsFalse(matches("/missing/library/work/page.png", true));
        Assert.IsFalse(matches("/missing/library/archive.gz", true));
    }

    [TestMethod]
    public void DirectoryFilter_IgnoresExtensionsAndExcludesFiles()
    {
        var config = Layer(1, PathMarkApplyScope.MatchedAndSubdirectories, PathFilterFsType.Directory);
        config.Extensions = [".zip"];
        var matches = ResourceMarkPathMatcher.Create("/missing/library", config);
        Assert.IsTrue(matches("/missing/library/work/chapters", false));
        Assert.IsFalse(matches("/missing/library/work/archive.zip", true));
    }

    [TestMethod]
    public void EmptyExtensions_DoNotFilterFiles()
    {
        var config = Layer(1, type: PathFilterFsType.File);
        config.Extensions = [];
        Assert.IsTrue(ResourceMarkPathMatcher.Create("/missing/library", config)("/missing/library/work", true));
    }

    [TestMethod]
    [DataRow("^work")]
    [DataRow("^work$")]
    public void RegexMatchedOnly_PreservesResolverSuffixAdjustment(string pattern)
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library", Pattern(pattern));
        Assert.IsTrue(matches("/missing/library/work", false));
        Assert.IsTrue(matches("/missing/library/work-extra", false));
        Assert.IsFalse(matches("/missing/library/work/chapter", false));
        Assert.IsFalse(matches("/missing/library/other/work", false));
    }

    [TestMethod]
    public void RegexSubtree_UsesUnadjustedPatternAndMatchedAncestors()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library",
            Pattern("^work$", PathMarkApplyScope.MatchedAndSubdirectories));
        Assert.IsTrue(matches("/missing/library/work", false));
        Assert.IsTrue(matches("/missing/library/work/chapter/page.jpg", true));
        Assert.IsFalse(matches("/missing/library/work-extra", false));
        Assert.IsFalse(matches("/missing/library/other/work/page.jpg", true));
    }

    [TestMethod]
    public void RegexSubtree_FileOnlyFilterCannotInheritFromMatchingDirectory()
    {
        var config = Pattern("^work$", PathMarkApplyScope.MatchedAndSubdirectories, PathFilterFsType.File);
        var matches = ResourceMarkPathMatcher.Create("/missing/library", config);
        Assert.IsTrue(matches("/missing/library/work", true));
        Assert.IsFalse(matches("/missing/library/work", false));
        Assert.IsFalse(matches("/missing/library/work/page.jpg", true));
    }

    [TestMethod]
    public void RegexSubtree_StillAppliesExtensionFilterToInheritedFile()
    {
        var config = Pattern("^work$", PathMarkApplyScope.MatchedAndSubdirectories);
        config.Extensions = [".jpg"];
        var matches = ResourceMarkPathMatcher.Create("/missing/library", config);
        Assert.IsTrue(matches("/missing/library/work/page.JPG", true));
        Assert.IsFalse(matches("/missing/library/work/page.png", true));
    }

    [TestMethod]
    public void Regex_NeverTreatsMarkRootAsInitialMatch()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library",
            Pattern("^$", PathMarkApplyScope.MatchedAndSubdirectories));
        Assert.IsFalse(matches("/missing/library", false));
        Assert.IsFalse(matches("/missing/library/work", false));
    }

    [TestMethod]
    public void ActiveNestedBoundary_BlocksOnlyWholeSubtreeOfAncestorMark()
    {
        var matches = ResourceMarkPathMatcher.Create("/missing/library",
            Layer(0, PathMarkApplyScope.MatchedAndSubdirectories),
            ["/missing/library/special"]);
        Assert.IsFalse(matches("/missing/library/special", false));
        Assert.IsFalse(matches("/missing/library/SPECIAL/work.zip", true));
        Assert.IsTrue(matches("/missing/library/special-other/work.zip", true));
        Assert.IsTrue(matches("/missing/library/normal/work.zip", true));
    }

    [TestMethod]
    public void Boundary_DoesNotBlockItsOwnMarkOrMarksInsideIt()
    {
        var own = ResourceMarkPathMatcher.Create("/missing/library/special", Layer(1),
            ["/missing/library/special"]);
        Assert.IsTrue(own("/missing/library/special/work", false));
        var inside = ResourceMarkPathMatcher.Create("/missing/library/special/deeper", Layer(1),
            ["/missing/library/special"]);
        Assert.IsTrue(inside("/missing/library/special/deeper/work", false));
    }

    [TestMethod]
    public void WindowsPaths_AreMatchedOnAnyHostWithCaseAndSeparatorNormalization()
    {
        var matches = ResourceMarkPathMatcher.Create(@"Y:\NAS\Library\", Layer(1),
            [@"y:\nas\library\special"]);
        Assert.IsTrue(matches("y:/nas/library/work", false));
        Assert.IsFalse(matches("Y:/NAS/Library-extra/work", false));
        Assert.IsFalse(matches(@"Y:\NAS\Library\special", false));
        var ancestor = ResourceMarkPathMatcher.Create(@"Y:\NAS\Library", Layer(-2));
        Assert.IsTrue(ancestor("y:/", false));
        Assert.IsFalse(ancestor("Y:/NAS", false));
    }

    [TestMethod]
    public void UncPaths_NegativeLayersStopAtShareRoot()
    {
        var matches = ResourceMarkPathMatcher.Create(@"\\missing-server\share\library\category", Layer(-2));
        Assert.IsTrue(matches("//MISSING-server/share", false));
        Assert.IsFalse(matches("//missing-server/share-sibling", false));
        var tooHigh = ResourceMarkPathMatcher.Create(@"\\missing-server\share\library", Layer(-2));
        Assert.IsFalse(tooHigh("//missing-server", false));
    }

    [TestMethod]
    public void NegativeLayersBeyondRoot_MatchNothingWithoutOverflow()
    {
        Assert.IsFalse(ResourceMarkPathMatcher.Create("/missing", Layer(-2))("/", false));
        Assert.IsFalse(ResourceMarkPathMatcher.Create("Y:/missing", Layer(int.MinValue))("Y:/", false));
    }

    [TestMethod]
    public void UnixRootAndDriveRoot_KeepValidSegmentDepth()
    {
        var unix = ResourceMarkPathMatcher.Create("/", Layer(1));
        Assert.IsTrue(unix("/missing", false));
        Assert.IsFalse(unix("/missing/work", false));
        var drive = ResourceMarkPathMatcher.Create("Z:/", Layer(1));
        Assert.IsTrue(drive("z:/missing", false));
        Assert.IsFalse(drive("Z:/missing/work", false));
    }

    [TestMethod]
    [DataRow("[")]
    [DataRow("(")]
    public void InvalidRegex_ThrowsDuringPreparationEvenWithoutCandidates(string pattern)
    {
        Assert.ThrowsException<RegexParseException>(() => ResourceMarkPathMatcher.Create("/missing", Pattern(pattern)));
        Assert.ThrowsException<RegexParseException>(() => ResourceMarkPathMatcher.Create("/missing",
            Pattern(pattern, PathMarkApplyScope.MatchedAndSubdirectories)));
    }

    [TestMethod]
    public void IncompleteConfiguration_MatchesNothing()
    {
        Assert.IsFalse(ResourceMarkPathMatcher.Create("/missing", new ResourceMarkConfig
        {
            MatchMode = PathMatchMode.Layer
        })("/missing/work", false));
        Assert.IsFalse(ResourceMarkPathMatcher.Create("/missing", Pattern(""))("/missing/work", false));
    }
}
