using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition.Planning;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

[TestClass]
public sealed class AcquisitionArchivePasswordEvidenceTests
{
    private const string Share = "https://example.test/s/resource";
    private const string Example = "Literal123";

    [TestMethod]
    public async Task UnlabelledPublisherTrailerIsRankedWithoutSendingItsValueOrHtml()
    {
        var html = $"<blockquote>&#38142;&#25509;：<a href='{Share}?pwd=abcd'>{Share}?pwd=abcd</a> 提取码: abcd<br>--来自网盘会员的分享<br>{Example}</blockquote>";
        var literals = AcquisitionArchivePasswordEvidence.Inspect(html, [Share], new HashSet<string>(), true);
        Assert.AreEqual(1, literals.Count); Assert.IsTrue(literals[0].AfterShareFooter);
        var llm = new RankingModel();
        var selected = await AcquisitionArchivePasswordEvidence.SelectAsync(literals, llm, default);
        CollectionAssert.AreEqual(new[] {Example}, selected.ToArray());
        Assert.IsFalse(llm.LastMessage.Contains(Example)); Assert.IsFalse(llm.LastMessage.Contains(Share));
        Assert.IsFalse(llm.LastMessage.Contains("abcd")); Assert.AreEqual(AiFeature.FileProcessor, llm.Feature);
        foreach (var wrong in new[] {"""{"id":0}""", """{"id":2}""", """{"id":1,"password":"invented"}"""})
        {
            llm.Answer = wrong;
            Assert.AreEqual(0, (await AcquisitionArchivePasswordEvidence.SelectAsync(literals, llm, default)).Count);
        }
    }

    [TestMethod]
    public async Task ExplicitLabelsUseOnlyVisibleSourceEvidenceAndNeverProbeAnAccessCode()
    {
        var html = $"<a href='{Share}'>链接</a><br>提取码：Code123<br>解压密码：Code123" +
                   $"<br>解压密码：{Example}<div style='display:none'>解压密码：Hidden123</div><script>Script123</script>" +
                   "<div>https://elsewhere.test/s/other<br>解压密码：Other123</div>";
        var literals = AcquisitionArchivePasswordEvidence.Inspect(html, [Share], new HashSet<string>(), false);
        var llm = new RankingModel();
        CollectionAssert.AreEqual(new[] {Example}, (await AcquisitionArchivePasswordEvidence.SelectAsync(literals, llm, default)).ToArray());
        Assert.IsNull(llm.Feature);
        Assert.AreEqual(0, AcquisitionArchivePasswordEvidence.Inspect("解压密码：Different123", [Share], new HashSet<string>(), false).Count);
    }

    [DataTestMethod]
    [DataRow(true, true)]
    [DataRow(true, false)]
    [DataRow(false, false)]
    public async Task OnlyVerifiedPasswordExtractsAndRetryDoesNotReadEvidenceAgain(bool encrypted, bool candidateCorrect)
    {
        var root = Directory.CreateTempSubdirectory("evidence-unpack-");
        try
        {
            var input = Directory.CreateDirectory(Path.Combine(root.FullName, "input")).FullName;
            var file = Path.Combine(input, "part.zip");
            await File.WriteAllBytesAsync(file, [0x50, 0x4b, 3, 4, 0, 1]);
            var state = Path.Combine(root.FullName, "state"); var archives = new ArchiveFixture(encrypted);
            var executor = new FileProcessingPlanExecutor(archives);
            var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "unpack", Op = "extractArchive"}]};
            var lookups = 0;
            Task<IReadOnlyList<string>> Evidence(CancellationToken _) {lookups++; return Task.FromResult<IReadOnlyList<string>>([candidateCorrect ? Example : "WrongChoice"]); }
            var first = await executor.ExecuteAsync(plan, input, [file], state, passwordEvidence: Evidence);
            var again = await executor.ExecuteAsync(plan, input, [file], state, passwordEvidence: Evidence);
            Assert.AreEqual(candidateCorrect, first.Completed); Assert.AreEqual(candidateCorrect, again.Completed);
            Assert.AreEqual(encrypted ? 1 : 0, lookups); Assert.AreEqual(candidateCorrect ? 1 : 0, archives.Extractions);
            Assert.IsTrue(File.Exists(file));
        }
        finally {root.Delete(true);}
    }

    [TestMethod]
    public async Task EncryptedFormatDiscoveryRequiresAFullTestBeforeItUnlocksTheArchive()
    {
        var root = Directory.CreateTempSubdirectory("evidence-discovery-");
        try
        {
            var input = Directory.CreateDirectory(Path.Combine(root.FullName, "input")).FullName;
            var file = Path.Combine(input, "outer.exe"); await File.WriteAllBytesAsync(file, [0x4d, 0x5a, 0, 0]);
            var fixture = new ArchiveFixture(true);
            var plan = new FileProcessingPlan {Requirement = "required", Steps = [new() {Id = "extract", Op = "extractArchive", Selector = "outer.exe"}]};
            var reads = 0;
            Task<IReadOnlyList<string>> Candidates(CancellationToken _) {reads++; return Task.FromResult<IReadOnlyList<string>>([Example]);}
            var result = await new FileProcessingPlanExecutor(fixture).ExecuteAsync(plan, input, [file], Path.Combine(root.FullName, "state"), passwordEvidence: Candidates);
            Assert.IsTrue(result.Completed, result.Message); Assert.AreEqual(1, reads); Assert.AreEqual(1, fixture.Extractions);
        }
        finally {root.Delete(true);}
    }

    private sealed class RankingModel : ILlmService
    {
        public string Answer = """{"id":1}""";
        public string LastMessage = "";
        public AiFeature? Feature;
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages, LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Feature = feature; LastMessage = string.Join("\n", messages.Select(m => m.Text));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, Answer)));
        }
        public Task<ChatResponse> CompleteAsync(int id, string model, IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature f, IList<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) => throw new NotSupportedException();
    }

    private sealed class ArchiveFixture(bool encrypted) : IArchiveExtractionService
    {
        internal int Extractions;
        public Task<ArchiveFormatProbeResult?> ProbeAuthorizedFormatAsync(string path, IReadOnlyList<string?> passwords, CancellationToken ct)
        {
            if (!passwords.Contains(Example)) throw new ArchiveFormatProbeIncompleteException("encrypted", ArchiveFormatProbeFailureCodes.PasswordRequired);
            return Task.FromResult<ArchiveFormatProbeResult?>(new(".7z"));
        }
        public Task<ArchivePasswordProbe> ProbePasswordAsync(string file, IReadOnlyList<string?> passwords, Action<string>? output, CancellationToken ct)
        {
            var pass = encrypted && passwords.Contains(Example);
            return Task.FromResult(new ArchivePasswordProbe(pass, pass ? Example : null, [], "", "") {PasswordRejected = encrypted && !pass});
        }
        public Task<ArchiveExtractionResult> ExtractAsync(ArchiveExtractionRequest request, Action<int>? progress, CancellationToken ct)
        {
            Assert.AreEqual(Example, request.Password); Extractions++;
            Directory.CreateDirectory(request.Directory);
            File.WriteAllText(Path.Combine(request.Directory, "verified.txt"), "fixture");
            return Task.FromResult(new ArchiveExtractionResult(true, request.Directory, ""));
        }
    }
}
