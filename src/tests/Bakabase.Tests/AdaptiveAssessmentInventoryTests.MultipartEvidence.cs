using System.Buffers.Binary;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using SharpCompress.Compressors.Deflate;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [TestMethod]
    public async Task Real_small_split_archive_offers_only_the_missing_entry_name_without_mutation()
    {
        using var fixture = new MultipartEvidenceFixture();
        var input = fixture.Input();
        var evidence = await Inspect(input);
        Assert.IsNotNull(evidence);
        Assert.AreEqual(new MultipartRename(1, "fixture.7z.001"), evidence.Renames.Single());
        Assert.AreEqual(310L, evidence.DeclaredArchiveBytes);
        CollectionAssert.AreEqual(new[] {1, 2, 3}, evidence.InputIds.ToArray());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Root, evidence.Renames.Single().ExpectedName)));
        for (var i = 0; i < input.Files.Count; i++)
            CollectionAssert.AreEqual(MultipartEvidenceFixture.Volumes[i], File.ReadAllBytes(input.Files[i]));
    }

    [DataTestMethod]
    [DataRow("fixture.001.pdf")]
    [DataRow("fixture.001")]
    public async Task Bare_volumes_with_a_proven_7z_extent_are_renamed_together_then_extracted_as_one_family(string entryName)
    {
        using var fixture = new MultipartEvidenceFixture();
        var original = MultipartEvidenceFixture.Volumes;
        // Inventory IDs need not follow volume order.
        var input = fixture.Input(["fixture.003", entryName, "fixture.002"], [original[2], original[0], original[1]]);
        var evidence = await Inspect(input);
        Assert.IsNotNull(evidence);
        CollectionAssert.AreEqual(new[] {new MultipartRename(2, "fixture.7z.001"), new(3, "fixture.7z.002"), new(1, "fixture.7z.003")},
            evidence.Renames.ToArray());
        var paths = input.Manifest.Entries.Select(e => e.RelativePath).ToArray();
        var repair = new AdaptiveFileProcessingDecision("continue", null, null,
            evidence.Renames.Select(r => new AdaptiveFileProcessingOperation("rename", paths[r.InputId - 1], r.ExpectedName)).ToArray(), [], []);
        Assert.IsTrue(AdaptiveFileProcessingAssessmentService.RequireMultipartNaming(repair, evidence, paths).ShouldContinue);
        var steps = AdaptiveFileProcessingAssessmentService.BuildPlan(repair)!.Steps;
        Assert.AreEqual(3, steps.Count);
        for (var i = 0; i < steps.Count; i++)
        {
            Assert.AreEqual("renameFile", steps[i].Op);
            Assert.AreEqual(paths[evidence.Renames[i].InputId - 1], steps[i].Selector);
            Assert.AreEqual(evidence.Renames[i].ExpectedName, steps[i].TargetName);
        }
        foreach (var invalid in new[] {
            repair with {Operations = repair.Operations.Take(1).ToArray()},
            repair with {Operations = [.. repair.Operations, new("extract", paths[1])]},
            repair with {Operations = [repair.Operations[0], repair.Operations[1], repair.Operations[1]]}
        })
            Assert.IsTrue(AdaptiveFileProcessingAssessmentService.RequireMultipartNaming(invalid, evidence, paths).NeedsHumanReview);
        var canonical = fixture.Input(["fixture.7z.001", "fixture.7z.002", "fixture.7z.003"]);
        Assert.IsNull(await Inspect(canonical));
        var extract = Decide(canonical, """{"status":"continue","operations":[{"type":"extract","inputId":1}]}""");
        Assert.IsTrue(extract.ShouldContinue, extract.Reason);
        var step = AdaptiveFileProcessingAssessmentService.BuildPlan(extract)!.Steps.Single();
        Assert.AreEqual("extractArchive", step.Op);
        Assert.IsTrue(step.Selector!.EndsWith('*'));
    }

    [DataTestMethod]
    [DataRow("fixture.jpg", "fixture.002", "fixture.003")]
    [DataRow("unrelated.001.pdf", "fixture.002", "fixture.003")]
    [DataRow("fixture.001.pdf", "fixture.002", "fixture.004")]
    [DataRow("fixture.001.pdf", "fixture.002", "fixture.002.tmp")]
    [DataRow("fixture.001.pdf", "other.002", "fixture.003")]
    public async Task Bare_numeric_siblings_without_the_same_exact_consecutive_family_are_not_bound(
        string first, string middle, string last)
    {
        using var fixture = new MultipartEvidenceFixture();
        Assert.IsNull(await Inspect(fixture.Input([first, middle, last])));
    }

    [DataTestMethod]
    [DataRow("corrupt-crc")]
    [DataRow("wrong-extent")]
    [DataRow("missing-part")]
    [DataRow("other-family")]
    [DataRow("other-entry-stem")]
    [DataRow("other-header")]
    [DataRow("two-candidates")]
    [DataRow("different-directory")]
    [DataRow("unequal-middle")]
    [DataRow("changed-file")]
    [DataRow("truncated-inventory")]
    public async Task Ambiguous_incomplete_or_changed_parts_never_produce_a_naming_repair(string change)
    {
        using var fixture = new MultipartEvidenceFixture();
        var names = new[] {"fixture.7z.0masked01", "fixture.7z.002", "fixture.7z.003"};
        var bytes = MultipartEvidenceFixture.Volumes.Select(v => v.ToArray()).ToArray();
        switch (change)
        {
            case "corrupt-crc": bytes[0][8] ^= 1; break;
            case "wrong-extent":
                BinaryPrimitives.WriteUInt64LittleEndian(bytes[0].AsSpan(12), 999);
                var crc = new CRC32(); crc.SlurpBlock(bytes[0], 12, 20);
                BinaryPrimitives.WriteUInt32LittleEndian(bytes[0].AsSpan(8), (uint)crc.Crc32Result); break;
            case "missing-part": names[2] = "fixture.7z.004"; break;
            case "other-family": names[2] = "unrelated.7z.003"; break;
            case "other-entry-stem": names[0] = "unrelated.7z.0masked01"; break;
            case "other-header": Array.Copy(bytes[0], bytes[1], 32); break;
            case "two-candidates": names[1] = "another-disguised.bin"; break;
            case "different-directory": names[1] = "different/fixture.7z.002"; break;
            case "unequal-middle": bytes[1] = bytes[1][..^1]; break;
        }
        var input = fixture.Input(names, bytes);
        if (change == "changed-file") File.AppendAllText(input.Files[1], "changed");
        var paths = input.Manifest.Entries.Select(e => e.RelativePath).ToArray();
        if (change == "truncated-inventory") paths = paths[..2];
        Assert.IsNull(await AdaptiveFileProcessingAssessmentService.InspectMultipartNamingAsync(input, paths, default));
    }

    [TestMethod]
    public async Task Structural_naming_evidence_blocks_early_extraction_and_final_but_allows_a_rename_proposal()
    {
        using var fixture = new MultipartEvidenceFixture();
        var input = fixture.Input();
        var evidence = await Inspect(input);
        var paths = input.Manifest.Entries.Select(e => e.RelativePath).ToArray();
        foreach (var decision in new[] {
            new AdaptiveFileProcessingDecision("final", null, null, [], [], []),
            new AdaptiveFileProcessingDecision("continue", null, null, [new("extract", paths[0])], [], [])})
        {
            var guarded = AdaptiveFileProcessingAssessmentService.RequireMultipartNaming(decision, evidence, paths);
            Assert.IsTrue(guarded.NeedsHumanReview);
            Assert.AreEqual("ARCHIVE_FAMILY_REVIEW_REQUIRED", guarded.DiagnosticCode);
        }
        var rename = Decide(input, """{"status":"continue","operations":[{"type":"rename","inputId":1,"destination":"fixture.7z.001"}]}""");
        Assert.IsTrue(AdaptiveFileProcessingAssessmentService.RequireMultipartNaming(rename, evidence, paths).ShouldContinue);
        Assert.AreEqual("renameFile", AdaptiveFileProcessingAssessmentService.BuildPlan(rename)!.Steps.Single().Op);
    }

    [TestMethod]
    public async Task Naming_advice_and_current_inventory_precedence_reach_the_model()
    {
        using var fixture = new MultipartEvidenceFixture();
        var input = fixture.Input();
        var model = new EvidenceModel();
        var service = new AdaptiveFileProcessingAssessmentService(model,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AdaptiveFileProcessingAssessmentService>.Instance);
        await service.AssessAsync(input, "Earlier instructions describe the original download.", 1);
        StringAssert.Contains(model.UserMessage, "multipartNamingEvidence");
        StringAssert.Contains(model.UserMessage, "\"expectedName\":\"fixture.7z.001\"");
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "Current filenames take precedence");
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "A directory whose name ends with an archive suffix is not an archive file");
    }

    [TestMethod]
    public void Repeating_an_already_applied_rename_is_rejected_before_a_plan_can_materialize_files()
    {
        var decision = Decide(Input("nested/package.7z.001"),
            """{"status":"continue","operations":[{"type":"rename","inputId":1,"destination":"package.7z.001"}]}""");
        Assert.IsTrue(decision.NeedsHumanReview);
        StringAssert.Contains(decision.ValidationError!, "rename would not change");
        Assert.IsNull(AdaptiveFileProcessingAssessmentService.BuildPlan(decision));
        var direct = new AdaptiveFileProcessingDecision("continue", null, null,
            [new("rename", "nested/package.7z.001", "package.7z.001")], [], []);
        Assert.ThrowsException<InvalidOperationException>(() => AdaptiveFileProcessingAssessmentService.BuildPlan(direct));
        var realRename = Decide(Input("nested/entry.disguised"),
            """{"status":"continue","operations":[{"type":"rename","inputId":1,"destination":"package.7z.001"}]}""");
        Assert.AreEqual("package.7z.001", AdaptiveFileProcessingAssessmentService.BuildPlan(realRename)!.Steps.Single().TargetName);
    }

    [TestMethod]
    public void Case_only_rename_uses_the_same_platform_comparison_as_file_processing()
    {
        var input = Input("nested/PACK.7z.001");
        var decision = Decide(input,
            """{"status":"continue","operations":[{"type":"rename","inputId":1,"destination":"pack.7z.001"}]}""");
        Assert.AreEqual(OperatingSystem.IsWindows(), decision.NeedsHumanReview);
    }

    private static Task<AdaptiveMultipartNamingEvidence?> Inspect(ArtifactRecord artifact) =>
        AdaptiveFileProcessingAssessmentService.InspectMultipartNamingAsync(artifact,
            artifact.Manifest.Entries.Select(e => e.RelativePath).ToArray(), default);

    private sealed class MultipartEvidenceFixture : IDisposable
    {
        internal string Root { get; } = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "adaptive-volumes-" + Guid.NewGuid().ToString("N"))).FullName;
        // Generated by 7zz 23.01: a -t7z -mx=0 -mtc=off -mta=off -v128b fixture.7z payload.bin.
        // payload.bin contains bytes 0..179. All three original parts passed `7zz t`.
        // Embedded so the real-format regression runs without a platform-specific tool.
        internal static byte[][] Volumes => new[] {
            "N3q8ryccAAQM+bLstAAAAAAAAABiAAAAAAAAABvXiRAAAQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyAhIiMkJSYnKCkqKywtLi8wMTIzNDU2Nzg5Ojs8PT4/QEFCQ0RFRkdISUpLTE1OT1BRUlNUVVZXWFlaW1xdXl8=",
            "YGFiY2RlZmdoaWprbG1ub3BxcnN0dXZ3eHl6e3x9fn+AgYKDhIWGh4iJiouMjY6PkJGSk5SVlpeYmZqbnJ2en6ChoqOkpaanqKmqq6ytrq+wsbKzAQQGAAEJgLQABwsBAAEBAAyAtAAICgFEbnWhAAAFARkMAAAAAAAAAAAAAAA=",
            "ABEZAHAAYQB5AGwAbwBhAGQALgBiAGkAbgAAABkCAAAUCgEAL8F5eq9y3QEVBgEAIICkgQAA"
        }.Select(Convert.FromBase64String).ToArray();

        internal ArtifactRecord Input(string[]? names = null, byte[][]? bytes = null)
        {
            names ??= ["fixture.7z.0masked01", "fixture.7z.002", "fixture.7z.003"];
            bytes ??= Volumes;
            var paths = names.Select(name => Path.Combine(Root, name)).ToArray();
            for (var i = 0; i < paths.Length; i++)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(paths[i])!);
                File.WriteAllBytes(paths[i], bytes[i]);
            }
            var entries = names.Select((name, i) => new ArtifactManifestEntry(name, bytes[i].Length, new string('a', 64))).ToArray();
            var fingerprint = ArtifactManifestFiles.Fingerprint(entries);
            return new("multipart-fixture", "operation", "files", Root, paths, new(entries, fingerprint),
                fingerprint, ArtifactLocationState.Available, true, [], null);
        }
        public void Dispose() => Directory.Delete(Root, true);
    }
}
