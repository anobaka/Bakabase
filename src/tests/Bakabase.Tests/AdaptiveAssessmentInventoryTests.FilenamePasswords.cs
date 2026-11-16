using System.Text.Json;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.AI;

namespace Bakabase.Tests;

public sealed partial class AdaptiveAssessmentInventoryTests
{
    private static AdaptiveFileProcessingDecision FilenamePasswordDecision(string[] paths, int inputId,
        string password, string? context = null)
    {
        var response = JsonSerializer.Serialize(new
        {
            status = "continue",
            operations = new[] {new {type = "extract", inputId, password}}
        });
        return AdaptiveFileProcessingAssessmentService.ParseAndValidate(
            new ChatResponse(new ChatMessage(ChatRole.Assistant, response)), Input(paths), paths, false, context);
    }

    [DataTestMethod]
    [DataRow("解压码1008", "1008")]
    [DataRow("archive [密码：AbC].7z", "AbC")]
    [DataRow("archive-password=Case-1.zip", "Case-1")]
    [DataRow("archive [Password=Case-1].RAR", "Case-1")]
    [DataRow("archive [解壓密碼：字面值].zip", "字面值")]
    [DataRow("archive [password=' With Spaces '].zip", " With Spaces ")]
    [DataRow("archive [password=\"literal.zip\"].zip", "literal.zip")]
    public void Explicit_basename_password_is_bound_to_its_visible_input_and_preserved(string path, string password)
    {
        var paths = new[] {"folder/" + path};
        var decision = FilenamePasswordDecision(paths, 1, password);
        Assert.IsTrue(decision.ShouldContinue, decision.ValidationError);
        var step = AdaptiveFileProcessingAssessmentService.BuildPlan(decision)!.Steps.Single();
        Assert.AreEqual(paths[0], step.Selector);
        Assert.AreEqual(password, step.Password);

        using var evidence = JsonDocument.Parse(AdaptiveFileProcessingAssessmentService.DescribeFilenamePasswordEvidence(paths));
        var item = evidence.RootElement.EnumerateArray().Single();
        Assert.AreEqual(1, item.GetProperty("inputId").GetInt32());
        Assert.AreEqual(password, item.GetProperty("password").GetString());
    }

    [TestMethod]
    public void Filename_literal_is_not_borrowed_from_a_parent_directory_another_file_or_task_title()
    {
        Assert.IsTrue(FilenamePasswordDecision(["密码:literal/archive.zip"], 1, "literal").NeedsHumanReview);
        var paths = new[] {"archive.zip", "[password=literal].zip"};
        Assert.IsTrue(FilenamePasswordDecision(paths, 1, "literal").NeedsHumanReview);
        Assert.IsTrue(FilenamePasswordDecision(paths, 2, "literal").ShouldContinue);
        var titleOnly = AdaptiveFileProcessingAssessmentService.DescribeContext("password=literal", null, null, null);
        Assert.IsTrue(FilenamePasswordDecision(["archive.zip"], 1, "literal", titleOnly).NeedsHumanReview);
    }

    [TestMethod]
    public void Recognizing_an_ascii_colon_password_marker_does_not_relax_plan_path_protection()
    {
        const string path = "archive [密码:AbC].7z";
        Assert.AreEqual("AbC", AdaptiveFileProcessingAssessmentService.ExplicitFilenamePassword(path));
        var decision = FilenamePasswordDecision([path], 1, "AbC");
        // Evidence recognition and executable path validation are separate contracts. The
        // existing compiler must still reject a colon in a file-processing selector.
        Assert.ThrowsException<InvalidOperationException>(() => AdaptiveFileProcessingAssessmentService.BuildPlan(decision));
    }

    [DataTestMethod]
    [DataRow("release1008.zip", "1008")]
    [DataRow("password123.zip", "123")]
    [DataRow("archive use identifier.zip", "identifier")]
    [DataRow("archive [密码:Case].zip", "case")]
    [DataRow("archive [密码:Case].zip", "Cas")]
    [DataRow("archive [密码:one] [密码:two].zip", "one")]
    [DataRow("archive [密码:one] [密码:one].zip", "one")]
    [DataRow("archive [密码:one] or two.zip", "one")]
    [DataRow("archive [密码:one,two].zip", "one")]
    [DataRow("archive [password='missing closing quote].zip", "missing closing quote")]
    public void Ambiguous_unlabeled_or_modified_filename_values_are_rejected(string path, string password)
    {
        var decision = FilenamePasswordDecision([path], 1, password);
        Assert.IsTrue(decision.NeedsHumanReview);
        Assert.AreEqual("LLM_DECISION_INVALID", decision.DiagnosticCode);
        Assert.AreEqual(0, decision.Operations.Count);
        StringAssert.Contains(decision.ValidationError, ".password:");
    }

    [TestMethod]
    public void Evidence_table_uses_the_same_bounded_parser_as_validation()
    {
        var paths = new[] {"plain.zip", "folder/[密码:Case].zip", "[密码:one] [密码:two].zip",
            "密码:parent/archive.zip", "密码:" + new string('x', 129)};
        using var evidence = JsonDocument.Parse(AdaptiveFileProcessingAssessmentService.DescribeFilenamePasswordEvidence(paths));
        var row = evidence.RootElement.EnumerateArray().Single();
        Assert.AreEqual(2, row.GetProperty("inputId").GetInt32());
        Assert.AreEqual("Case", row.GetProperty("password").GetString());
        Assert.IsTrue(FilenamePasswordDecision(paths, 2, row.GetProperty("password").GetString()!).ShouldContinue);
        Assert.IsNull(AdaptiveFileProcessingAssessmentService.ExplicitFilenamePassword(new string('x', 513) + " [密码:Case]"));
        Assert.IsNull(AdaptiveFileProcessingAssessmentService.ExplicitFilenamePassword("[密码:one\nline]"));
    }

    [TestMethod]
    public void Filename_password_requires_a_real_selected_manifest_entry()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, PasswordResponse("literal")));
        var decision = AdaptiveFileProcessingAssessmentService.ParseAndValidate(response,
            Input("plain.zip"), ["[password=literal].zip"], false);
        Assert.IsTrue(decision.NeedsHumanReview);
    }

    [TestMethod]
    public void Existing_context_literals_remain_supported_and_a_stale_general_literal_does_not_block_the_file_marker()
    {
        var context = AdaptiveFileProcessingAssessmentService.DescribeContext(null, null,
            "Confirmed extraction password: user-confirmed", null);
        Assert.IsTrue(FilenamePasswordDecision(["plain.zip"], 1, "user-confirmed", context).ShouldContinue);
        Assert.IsTrue(FilenamePasswordDecision(["[密码:ambiguous] [密码:other].zip"], 1, "user-confirmed", context).ShouldContinue);
        Assert.IsTrue(FilenamePasswordDecision(["[密码:current].zip"], 1, "current", context).ShouldContinue);
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "filenamePasswordEvidence");
        StringAssert.Contains(AdaptiveFileProcessingAssessmentService.SystemPrompt, "not your own interpretation of other filename text");
    }

    [TestMethod]
    public void Filename_candidates_do_not_authorize_passwords_on_non_extraction_operations()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, PasswordResponse("literal", "rename")));
        var paths = new[] {"[password=literal].zip"};
        Assert.IsTrue(AdaptiveFileProcessingAssessmentService.ParseAndValidate(response, Input(paths), paths, false).NeedsHumanReview);
    }
}
