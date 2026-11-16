using System.Text.Json;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    private static string PasswordResponse(string? password, string type = "extract") => JsonSerializer.Serialize(new
    {
        status = "continue", reason = "Use the supplied literal for this archive.",
        operations = new[] {new {type, inputId = 1, destination = type == "rename" ? "renamed.zip" : null, password}}
    });

    private static AdaptiveFileProcessingDecision PasswordDecision(string? password, string? context, string type = "extract")
    {
        var input = Input("archive.zip");
        return AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, PasswordResponse(password, type))), input, ["archive.zip"], false, context);
    }

    [TestMethod]
    public async Task Supplied_literal_survives_assessment_and_compilation_without_normalizing_password_characters()
    {
        const string password = " 大写Pass \"引号\"\\tail ";
        var context = AdaptiveFileProcessingAssessmentService.DescribeContext("unrelated title", null,
            "Exact extraction password: [" + password + "]", null);
        var model = new PasswordModel(PasswordResponse(password));
        var service = new AdaptiveFileProcessingAssessmentService(model, NullLogger<AdaptiveFileProcessingAssessmentService>.Instance);
        var decision = await service.AssessAsync(Input("archive.zip"), context, 0);
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        var step = AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single();
        Assert.AreEqual(password, step.Password);
        Assert.AreEqual("archive.zip", step.Selector);
        Assert.AreEqual("extractArchive", step.Op);
        Assert.IsNotNull(model.Parameters!.JsonResponseSchema);
        var schema = model.Parameters.JsonResponseSchema.Value.GetProperty("properties").GetProperty("operations").GetProperty("items");
        Assert.IsTrue(schema.GetProperty("required").EnumerateArray().Any(p => p.GetString() == "password"));
        Assert.AreEqual(2048, schema.GetProperty("properties").GetProperty("password").GetProperty("maxLength").GetInt32());
        StringAssert.Contains(model.SystemMessage, "Assessment contract version: 2");
    }

    [TestMethod]
    public void Saved_source_json_is_decoded_but_keys_and_title_do_not_supply_password_evidence()
    {
        const string password = "pA\\ss\"word_汉字";
        var evidence = JsonSerializer.Serialize(new {password = password});
        var context = AdaptiveFileProcessingAssessmentService.DescribeContext(null, null, null, evidence);
        Assert.IsTrue(PasswordDecision(password, context).ShouldContinue);
        var onlyKey = JsonSerializer.Serialize(new Dictionary<string, string> {[password] = "unrelated value"});
        var absent = AdaptiveFileProcessingAssessmentService.DescribeContext(password, null, null, onlyKey);
        var rejected = PasswordDecision(password, absent);
        Assert.IsTrue(rejected.NeedsHumanReview);
        StringAssert.Contains(rejected.ValidationError, ".password:");
        Assert.IsFalse(rejected.ValidationError!.Contains(password, StringComparison.Ordinal));
        Assert.IsTrue(PasswordDecision("userInstructions", absent).NeedsHumanReview);
    }

    [DataTestMethod]
    [DataRow("提取码：2012", false)]
    [DataRow("https://example.test/s/share?pwd=2012", false)]
    [DataRow("{\"accessCode\":\"2012\"}", false)]
    [DataRow("提取码：2012，解压密码：2012", true)]
    [DataRow("{\"accessCode\":\"2012\",\"archivePassword\":\"2012\"}", true)]
    public void Share_code_requires_separate_archive_password_evidence(string source, bool accepted)
    {
        var context = AdaptiveFileProcessingAssessmentService.DescribeContext(null, null, null, source);
        Assert.AreEqual(accepted, PasswordDecision("2012", context).ShouldContinue);
        var overrideContext = AdaptiveFileProcessingAssessmentService.DescribeContext(null, null, "解压密码：2012", source);
        Assert.IsTrue(PasswordDecision("2012", overrideContext).ShouldContinue);
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("another literal")]
    [DataRow("Exact password: CASE")]
    public void Invented_or_case_changed_password_is_rejected(string? context)
    {
        var decision = PasswordDecision("Case", context);
        Assert.IsTrue(decision.NeedsHumanReview);
        Assert.AreEqual("LLM_DECISION_INVALID", decision.DiagnosticCode);
        Assert.AreEqual(0, decision.Operations.Count);
    }

    [DataTestMethod]
    [DataRow("rename")]
    [DataRow("move")]
    [DataRow("retain")]
    [DataRow("rebaseDirectory")]
    public void Non_extraction_operations_cannot_carry_passwords(string type)
    {
        Assert.IsTrue(PasswordDecision("confirmed", "password: confirmed", type).NeedsHumanReview);
        var direct = new AdaptiveFileProcessingDecision("continue", null, null,
            [new(type, "archive.zip", "renamed.zip", Password: "confirmed")], [], []);
        Assert.ThrowsException<InvalidOperationException>(() => AdaptiveFileProcessingAssessmentService.BuildPlan(direct));
    }

    [TestMethod]
    public void Empty_control_overlong_wrong_type_and_duplicate_password_fields_are_rejected()
    {
        foreach (var password in new[] {"", "bad\nline", "bad\0value", new string('x', 2049)})
            Assert.IsTrue(PasswordDecision(password, "password: " + password).NeedsHumanReview);
        var input = Input("archive.zip");
        foreach (var fields in new[] {"\"password\":123", "\"password\":[\"literal\"]", "\"password\":\"literal\",\"password\":null"})
        {
            var raw = "{\"status\":\"continue\",\"operations\":[{\"type\":\"extract\",\"inputId\":1," + fields + "}]}";
            var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(
                new ChatResponse(new ChatMessage(ChatRole.Assistant, raw)), input, ["archive.zip"], false, "password: literal");
            Assert.IsTrue(decision.NeedsHumanReview, fields);
        }
    }

    [TestMethod]
    public void Omitted_or_null_password_preserves_existing_bounded_executor_password_resolution()
    {
        foreach (var decision in new[] {PasswordDecision(null, null), Decide(Input("archive.zip"),
                     """{"status":"continue","operations":[{"type":"extract","inputId":1}]}""")})
        {
            Assert.IsTrue(decision.ShouldContinue);
            Assert.IsNull(AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single().Password);
        }
    }

    [TestMethod]
    public void Password_change_changes_the_operation_decision_identity_and_does_not_weaken_input_validation()
    {
        const string context = "Previously attempted: first. Verified literal password: second.";
        var first = PasswordDecision("first", context);
        var second = PasswordDecision("second", context);
        Assert.AreNotEqual(first.DecisionHash, second.DecisionHash);
        var input = Input("archive.zip");
        var forgedInput = PasswordResponse("second").Replace("\"inputId\":1", "\"inputId\":99");
        Assert.IsTrue(AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, forgedInput)), input, ["archive.zip"], false, context).NeedsHumanReview);
    }

    private sealed class PasswordModel(string response) : ILlmService
    {
        public LlmModelParameters? Parameters;
        public string SystemMessage = "";
        public Task<ChatResponse> CompleteForFeatureAsync(AiFeature feature, IEnumerable<ChatMessage> messages,
            LlmModelParameters? parametersOverride = null, CancellationToken ct = default)
        {
            Parameters = parametersOverride;
            SystemMessage = messages.Single(m => m.Role == ChatRole.System).Text;
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, response)));
        }
        public Task<ChatResponse> CompleteAsync(int id, string model, IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<ChatResponse> CompleteWithDefaultAsync(IEnumerable<ChatMessage> messages, LlmModelParameters? p = null, AiFeature? f = null, CancellationToken ct = default) => throw new NotSupportedException();
        public IAsyncEnumerable<ChatResponseUpdate> CompleteStreamingForFeatureAsync(AiFeature f, IList<ChatMessage> m, ChatOptions? o = null, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
