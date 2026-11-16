using System.Text;
using System.Text.Json;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    private static string[] DenseGameWithRemainingPackages(string longName = "") =>
        Enumerable.Range(0, 1051).Select(i => $"wrapper/first-game/assets/images/{longName}{i:D4}.png")
            .Concat([
                $"wrapper/first-game/{longName}package.part1.rar",
                $"wrapper/first-game/{longName}package.part2.rar",
                $"wrapper/second-game/{longName}main.exe",
                $"wrapper/second-game/{longName}main.e01",
                $"wrapper/second-game/{longName}bonus.exe"
            ]).ToArray();

    [TestMethod]
    public void PartialInventoryShowsRemainingPackagesAcrossDenseGameBranches()
    {
        var files = DenseGameWithRemainingPackages();
        var input = Input(files);
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsTrue(view.Truncated);
        Assert.IsTrue(view.Paths.Length <= 200);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(view.Json) <= 16 * 1024);
        foreach (var file in files.TakeLast(5)) CollectionAssert.Contains(view.Paths, file);
        Assert.IsTrue(Array.IndexOf(view.Paths, files[^5]) < Array.IndexOf(view.Paths, files[0]));
        Assert.IsTrue(Array.IndexOf(view.Paths, files[^4]) < Array.IndexOf(view.Paths, files[0]));
        CollectionAssert.AreEqual(view.Paths,
            AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest).Paths);
        AssertInventoryIds(view.Json, view.Paths);

        // A reordered ID binds the exact file; family expansion is independently checked
        // against all registered files, including the unshown tail.
        var inputId = Array.IndexOf(view.Paths, files[^5]) + 1;
        var decision = Decide(input, JsonSerializer.Serialize(new {status = "continue", operations = new[] {
            new {type = "rename", inputId, destination = "renamed.part1.rar"}}}));
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        var step = AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single();
        Assert.AreEqual(files[^5], step.Selector);
        var extract = Decide(input, JsonSerializer.Serialize(new {status = "continue", operations = new[] {
            new {type = "extract", inputId}}}));
        Assert.IsTrue(extract.ShouldContinue, extract.ValidationError);
        Assert.AreEqual("wrapper/first-game/package.part*",
            AdaptiveFileProcessingAssessmentService.BuildPlan(extract)!.Steps.Single().Selector);

        // Merely exposing an executable does not add an executable-operation capability.
        var executableId = Array.IndexOf(view.Paths, files[^3]) + 1;
        Assert.IsTrue(Decide(input, JsonSerializer.Serialize(new {status = "continue", operations = new[] {
            new {type = "execute", inputId = executableId}}})).NeedsHumanReview);
    }

    [TestMethod]
    public void ByteBudgetReductionRetainsPackageAndSiblingBranchRepresentatives()
    {
        var files = DenseGameWithRemainingPackages(new string('文', 220));
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(Input(files).Manifest);
        Assert.IsTrue(view.Truncated);
        Assert.IsTrue(view.Paths.Length < 200);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(view.Json) <= 16 * 1024);
        foreach (var file in files.TakeLast(5)) CollectionAssert.Contains(view.Paths, file);
        AssertInventoryIds(view.Json, view.Paths);
    }

    [TestMethod]
    public void FairSelectionDoesNotMakePartialInventoryCompleteOrAllowDirectoryRebasing()
    {
        var input = Input(DenseGameWithRemainingPackages());
        var final = Decide(input, """{"status":"final"}""");
        Assert.IsTrue(final.NeedsHumanReview);
        Assert.AreEqual("INCOMPLETE_FILE_INVENTORY", final.DiagnosticCode);
        Assert.IsTrue(Decide(input,
            """{"status":"continue","operations":[{"type":"rebaseDirectory","directoryId":1,"destination":"."}]}""")
            .NeedsHumanReview);
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        var omitted = input.Manifest.Entries.Select(entry => entry.RelativePath).Except(view.Paths).First();
        Assert.IsTrue(Decide(input, JsonSerializer.Serialize(new {status = "continue", operations = new[] {
            new {type = "move", input = omitted, destination = "elsewhere"}}})).NeedsHumanReview);
    }

    [TestMethod]
    public void CompleteSmallInventoryPreservesOriginalOrderAndSelectors()
    {
        var files = new[] {"z/cover.jpg", "z/image.png", "a/package.zip", "z/readme.txt"};
        var input = Input(files);
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsFalse(view.Truncated);
        CollectionAssert.AreEqual(files, view.Paths);
        AssertInventoryIds(view.Json, view.Paths);
        Assert.AreEqual(files[2], Decide(input,
            """{"status":"continue","operations":[{"type":"extract","inputId":3}]}""")
            .Operations.Single().Input);
    }

    [TestMethod]
    public void PartialInventoryCanProcessTheExactNewlyVisibleSingleArchive()
    {
        var input = Input(Enumerable.Range(0, 1051).Select(i => $"first/assets/{i:D4}.png")
            .Concat(["later/resource.zip"]).ToArray());
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        var inputId = Array.IndexOf(view.Paths, "later/resource.zip") + 1;
        Assert.IsTrue(inputId > 0);
        Assert.IsTrue(view.Truncated);
        var decision = Decide(input, JsonSerializer.Serialize(new {status = "continue", operations = new[] {
            new {type = "extract", inputId}}}));
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        Assert.AreEqual("later/resource.zip",
            AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single().Selector);
    }

    [TestMethod]
    public void SharedOuterDirectoryAndManyArchivesCannotStarveSiblingBranch()
    {
        var input = Input(Enumerable.Range(0, 400).Select(i => $"outer/shared/a/{i:D4}.zip")
            .Concat(["outer/shared/z/standalone.exe", "outer/shared/z/standalone.e01"]).ToArray());
        var view = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsTrue(view.Truncated);
        CollectionAssert.Contains(view.Paths, "outer/shared/z/standalone.exe");
        CollectionAssert.Contains(view.Paths, "outer/shared/z/standalone.e01");
        AssertInventoryIds(view.Json, view.Paths);
    }

    [TestMethod]
    public void ManyShortBranchesAndOneDenseBranchHaveDeterministicBoundedSelection()
    {
        // Exhausted branches must leave the round-robin queue instead of being visited
        // once per remaining file in the large branch. No timing-dependent assertion.
        var files = Enumerable.Range(0, 10000).Select(i => $"outer/branch{i:D5}/single.txt")
            .Concat(Enumerable.Range(0, 10000).Select(i => $"outer/dense/{i:D5}.txt")).ToArray();
        var input = Input(files);
        var first = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        var second = AdaptiveFileProcessingAssessmentService.DescribeManifest(input.Manifest);
        Assert.IsTrue(first.Truncated);
        Assert.IsTrue(first.Paths.Length <= 200);
        Assert.IsTrue(Encoding.UTF8.GetByteCount(first.Json) <= 16 * 1024);
        CollectionAssert.AreEqual(first.Paths, second.Paths);
        Assert.AreEqual(first.Paths.Length, first.Paths.Select(path => path.Split('/')[1]).Distinct().Count());
        AssertInventoryIds(first.Json, first.Paths);
    }

    private static void AssertInventoryIds(string jsonText, string[] paths)
    {
        using var json = JsonDocument.Parse(jsonText);
        var root = json.RootElement;
        var ids = new List<int>();
        foreach (var folder in root.GetProperty("folders").EnumerateArray())
        foreach (var file in folder.GetProperty("files").EnumerateArray())
        {
            var id = file.GetProperty("id").GetInt32();
            ids.Add(id);
            Assert.AreEqual(string.Join('/', new[] {root.GetProperty("prefix").GetString(),
                folder.GetProperty("folder").GetString(), file.GetProperty("name").GetString()}.Where(p => p != ".")),
                paths[id - 1]);
        }
        CollectionAssert.AreEquivalent(Enumerable.Range(1, paths.Length).ToArray(), ids.ToArray());
    }
}
