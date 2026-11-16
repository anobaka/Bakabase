using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Service.Components.FileProcessing;

namespace Bakabase.Tests.Artifacts;

[TestClass]
public sealed class FileProcessingArchiveOpeningFailureTests
{
    [DataTestMethod]
    [DataRow("ArchiveInputRequired")]
    [DataRow("MissingVolumes")]
    [DataRow(null)]
    public async Task Clear_opening_failure_survives_execution_without_becoming_a_password_request(string? code)
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "archive-failure-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            var input = Directory.CreateDirectory(Path.Combine(root, "input")).FullName;
            var file = Path.Combine(input, "selected.7z");
            await File.WriteAllTextAsync(file, "unchanged input");
            var archives = new OpeningFailure(code);
            var result = await new FileProcessingPlanExecutor(archives).ExecuteAsync(new()
                {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive"}]},
                input, [file], Path.Combine(root, "checkpoint"), password: "source-password");
            Assert.IsFalse(result.Completed);
            Assert.AreEqual(code == null, result.NeedsPassword);
            Assert.AreEqual(code, result.FailureCode);
            Assert.AreEqual(0, archives.Extractions);
            Assert.AreEqual("unchanged input", await File.ReadAllTextAsync(file));
        }
        finally { Directory.Delete(root, true); }
    }

    private sealed class OpeningFailure(string? code) : IArchiveExtractionService
    {
        internal int Extractions;
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => Task.FromResult(new ArchivePasswordProbe(false, null, [], "", "tool failure") {FailureCode = code});
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        { Extractions++; throw new InvalidOperationException("An unopenable archive must not be extracted."); }
    }
}
