using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Components.FileProcessing;
using Bakabase.Tests.CloudDownloads;
using CliWrap;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    [TestMethod]
    public async Task Installed_tool_identifies_a_pe_prefixed_7z_in_the_model_inventory_without_unpacking()
    {
        var toolPath = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (!File.Exists(toolPath)) Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        using var source = new FormatEvidenceFixture();
        var file = source.Input(("page.jpg", [1], 2 * 1024 * 1024L)).Files.Single();
        var archive = Path.Combine(source.Root, "book.7z");
        await Cli.Wrap(toolPath!).WithArguments(["a", "-t7z", "-mx=0", archive, file]).ExecuteAsync();
        var bytes = ExplicitExecutableArchiveTests.CreateInertPeHeader().Concat(await File.ReadAllBytesAsync(archive)).ToArray();
        using var fixture = new FormatEvidenceFixture();
        var input = fixture.Input("chapters.exe", bytes);
        var model = new EvidenceModel();
        var service = new AdaptiveFileProcessingAssessmentService(model, NullLogger<AdaptiveFileProcessingAssessmentService>.Instance,
            archiveProbe: new ArchiveExtractionService(new TestableCompressedFileService(toolPath!)));
        await service.AssessAsync(input, "Unpack the requested book", 0, inspectImagePackaging: true);
        StringAssert.Contains(model.UserMessage, "\"inputId\":1,\"status\":\"recognizedExecutableArchive\",\"archiveFormat\":\".7z\"");
        CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(input.Files.Single()));
        CollectionAssert.AreEquivalent(input.Files.ToArray(), Directory.GetFiles(fixture.Root));
    }

    [DataTestMethod]
    [DataRow("recognized")] [DataRow("ordinary")] [DataRow("limit")] [DataRow("changed")]
    public async Task Outer_executable_format_evidence_requires_a_successful_bounded_list(string outcome)
    {
        using var fixture = new FormatEvidenceFixture();
        var input = fixture.Input(("book.exe", "MZ"u8.ToArray(), 2 * 1024 * 1024L), ("note.txt", [1], 1));
        var model = new EvidenceModel();
        var probe = new ListingProbe((path, passwords) =>
        {
            Assert.AreEqual(input.Files[0], path);
            CollectionAssert.AreEqual(new[] {"local-secret"}, passwords.ToArray());
            if (outcome == "limit") throw new ArchiveFormatProbeIncompleteException("time budget");
            if (outcome == "changed") File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddMinutes(-10));
            return outcome == "ordinary" ? null : new(".7z");
        });
        var service = new AdaptiveFileProcessingAssessmentService(model,
            NullLogger<AdaptiveFileProcessingAssessmentService>.Instance, archiveProbe: probe);
        await service.AssessAsync(input, "Unpack the outer package.", 0, default, true, null, "local-secret");
        Assert.AreEqual(1, probe.Calls);
        Assert.IsFalse(model.UserMessage.Contains("local-secret", StringComparison.Ordinal));
        var expected = outcome switch
        {
            "recognized" => "\"recognizedExecutableArchive\",\"archiveFormat\":\".7z\"",
            "ordinary" => "\"notRecognizedAtOffsetZero\",\"archiveFormat\":null",
            _ => "\"unavailable\",\"archiveFormat\":null"
        };
        StringAssert.Contains(model.UserMessage, "\"inputId\":1,\"status\":" + expected);
        CollectionAssert.AreEquivalent(input.Files.ToArray(), Directory.GetFiles(fixture.Root));
    }

    [DataTestMethod]
    [DataRow("no-unpacking-context")] [DataRow("already-extracted")] [DataRow("multiple-executables")]
    [DataRow("installed-files")] [DataRow("small")]
    public async Task Assessment_does_not_search_game_executables_or_probe_without_unpacking_evidence(string situation)
    {
        using var fixture = new FormatEvidenceFixture();
        var files = new List<(string, byte[], long)> {("app.exe", "MZ"u8.ToArray(), situation == "small" ? 64 : 2 * 1024 * 1024)};
        if (situation == "multiple-executables") files.Add(("app2.exe", [0], 2 * 1024 * 1024L));
        if (situation == "installed-files") files.Add(("game/data.dll", [0], 64));
        var input = fixture.Input(files.ToArray());
        var history = situation == "already-extracted"
            ? new AdaptiveProcessingExecutionEvidence(input.Id, input.Fingerprint, [
                new(1, "archive", "old", input.Id, input.Fingerprint, [new("extractArchive", 1)])])
            : null;
        var model = new EvidenceModel();
        var probe = new ListingProbe((_, _) => throw new AssertFailedException("Unexpected executable scan"));
        var service = new AdaptiveFileProcessingAssessmentService(model,
            NullLogger<AdaptiveFileProcessingAssessmentService>.Instance, archiveProbe: probe);
        await service.AssessAsync(input, null, 1, default, situation != "no-unpacking-context", history);
        Assert.AreEqual(0, probe.Calls);
        StringAssert.Contains(model.UserMessage, "notRecognizedAtOffsetZero");
    }

    private sealed class ListingProbe(Func<string, IReadOnlyList<string?>, ArchiveFormatProbeResult?> list) : IArchiveExtractionService
    {
        public int Calls { get; private set; }
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string path, IReadOnlyList<string?> passwords, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(list(path, passwords));
        }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string path, IReadOnlyList<string?> passwords, Action<string>? output,
            CancellationToken ct) => throw new AssertFailedException("Assessment must not test or extract file contents");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress,
            CancellationToken ct) => throw new AssertFailedException("Assessment must not extract files");
    }
}
