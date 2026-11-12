using System.Text;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.FileProcessing;
using CliWrap;

namespace Bakabase.Tests.CloudDownloads;

[TestClass]
public sealed class EncryptedHeaderArchiveTests
{
    private string _root = null!;
    private string _input = null!;
    private const string Password = "synthetic-header-password";
    [TestInitialize] public void Initialize()
    {
        _root = Path.Combine(Path.GetTempPath(), "encrypted-header-" + Guid.NewGuid().ToString("N"));
        _input = Path.Combine(_root, "input"); Directory.CreateDirectory(_input);
    }
    [TestCleanup] public void Cleanup() => Directory.Delete(_root, true);

    [TestMethod]
    public async Task RealEncryptedHeaderUsesOnlyExplicitStepPasswordAndExtractsWithoutExecutingInput()
    {
        var (tool, file) = await Fixture();
        var original = await File.ReadAllBytesAsync(file);
        await Assert.ThrowsExactlyAsync<ArchiveFormatProbeIncompleteException>(() => tool.ProbeFormatAsync(file, default));
        var format = await tool.ProbeAuthorizedFormatAsync(file, [Password], default);
        Assert.AreEqual(".7z", format?.Extension);
        var done = await Execute(tool, file, Password);
        Assert.IsTrue(done.Completed, done.Message);
        Assert.AreEqual("inert encrypted-header content", await File.ReadAllTextAsync(done.Files.Single()));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task RealWrongHeaderPasswordWaitsWithoutExtractingOrChangingOriginal()
    {
        var (tool, file) = await Fixture();
        var original = await File.ReadAllBytesAsync(file);
        var result = await Execute(tool, file, "wrong-evidence-password");
        Assert.IsFalse(result.Completed);
        StringAssert.Contains(result.Message!, "encrypted header");
        Assert.IsFalse(result.Message.Contains("wrong-evidence-password", StringComparison.Ordinal));
        Assert.AreEqual("extract", result.StepId);
        Assert.IsFalse(Directory.GetFiles(_input).Any(p => Path.GetFileName(p) == "payload.txt"));
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file));
    }

    [TestMethod]
    public async Task DiscoveryAndBroadImageSelectorsNeverReceiveStepPasswords()
    {
        var files = new[] {Path.Combine(_input, "one.jpg"), Path.Combine(_input, "two.jpg")};
        foreach (var f in files) await File.WriteAllTextAsync(f, "ordinary image fixture");
        var probe = new NoCalls();
        Assert.AreEqual(0, (await AuthorizedArchiveDetection.DetectAsync(files, true, default, probe,
            exactExtractionSelection: true, authorizedPasswords: [Password])).Count);
        var result = await new FileProcessingPlanExecutor(probe).ExecuteAsync(new() {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive", Selector = "*.jpg", Password = Password}]},
            _input, files, Path.Combine(_root, "state"));
        Assert.IsFalse(result.Completed);
    }

    [TestMethod]
    public async Task ResumingOneArchiveUsesTheSameBoundManualAnswerForFormatAndPasswordProbes()
    {
        var file = Path.Combine(_input, "selected.exe"); await File.WriteAllTextAsync(file, "inert fixture");
        var probe = new AnswerProbe();
        var plan = new FileProcessingPlan {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive", Selector = "selected.exe", Password = "source-password"}]};
        var executor = new FileProcessingPlanExecutor(probe);
        var state = Path.Combine(_root, "state");
        Assert.IsTrue((await executor.ExecuteAsync(plan, _input, [file], state)).NeedsPassword);
        var result = await executor.ExecuteAsync(plan, _input, [file], state, password: "manual-answer");
        Assert.IsTrue(result.Completed, result.Message);
        Assert.AreEqual(2, probe.Formats.Count);
        CollectionAssert.AreEqual(new[] {"source-password"}, probe.Formats[0]);
        CollectionAssert.AreEqual(new[] {"manual-answer", "source-password"}, probe.Formats[1]);
        CollectionAssert.AreEqual(probe.Formats[1], probe.Passwords[1]);
    }

    private Task<FileProcessingPlanResult> Execute(IArchiveExtractionService tool, string file, string password) =>
        new FileProcessingPlanExecutor(tool).ExecuteAsync(new() {Requirement = "required", Steps =
            [new() {Id = "extract", Op = "extractArchive", Selector = "selected.exe", Password = password}]},
            _input, [file], Path.Combine(_root, "state"));

    private async Task<(ArchiveExtractionService Tool, string File)> Fixture()
    {
        var executable = Environment.GetEnvironmentVariable("BAKABASE_TEST_7Z_PATH");
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable))
            Assert.Inconclusive("Set BAKABASE_TEST_7Z_PATH for the installed archive tool fixture.");
        var payload = Path.Combine(_root, "payload.txt");
        await File.WriteAllTextAsync(payload, "inert encrypted-header content");
        var archive = Path.Combine(_root, "encrypted.7z");
        var output = new StringBuilder();
        await Cli.Wrap(executable!).WithArguments(["a", "-t7z", "-mhe=on", "-p" + Password, "--", archive, payload], true)
            .WithStandardOutputPipe(PipeTarget.ToStringBuilder(output)).ExecuteAsync();
        // Only the installed 7-Zip is invoked. This inert prefix is input, never a program.
        var file = Path.Combine(_input, "selected.exe");
        await File.WriteAllBytesAsync(file, "MZinert nonexecutable prefix"u8.ToArray().Concat(await File.ReadAllBytesAsync(archive)).ToArray());
        return (new(new TestableCompressedFileService(executable!)), file);
    }

    private sealed class NoCalls : IArchiveExtractionService
    {
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string entryFile,
            IReadOnlyList<string?> passwords, CancellationToken ct) => throw new AssertFailedException("Unauthorized deep probe");
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct) => throw new AssertFailedException("Unauthorized password test");
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? progress, CancellationToken ct) => throw new AssertFailedException("Unauthorized extraction");
    }

    private sealed class AnswerProbe : IArchiveExtractionService
    {
        public List<string?[]> Formats { get; } = [];
        public List<string?[]> Passwords { get; } = [];
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string file,
            IReadOnlyList<string?> passwords, CancellationToken ct)
        {
            Formats.Add(passwords.ToArray());
            return Task.FromResult<ArchiveFormatProbeResult?>(new(".7z"));
        }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> candidates,
            Action<string>? output, CancellationToken ct)
        {
            Passwords.Add(candidates.ToArray());
            var succeeded = candidates.Contains("manual-answer");
            return Task.FromResult(new ArchivePasswordProbe(succeeded, succeeded ? "manual-answer" : null, [], "", ""));
        }
        public async Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request,
            Action<int>? progress, CancellationToken ct)
        {
            Assert.AreEqual("manual-answer", request.Password);
            await File.WriteAllTextAsync(Path.Combine(request.Directory, "payload.txt"), "inert payload", ct);
            return new(true, request.Directory, "");
        }
    }
}
