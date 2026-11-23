using System.Text.Json;
using Bakabase.InsideWorld.Business.Components.Compression;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Service.Components.CloudDownloads;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bakabase.Tests;

[TestClass]
public sealed class FileProcessingResumeActionTests
{
    private const string LegacyTimeout = "Archive format discovery did not finish within its time budget. Narrow the selector or revise the plan.";
    private const string LegacyMediaProbe = "No supported archive was recognized inside the selected media-prefixed file. Revise the extraction step.";
    private static WorkflowExecutionContext Context(string? prompt, int definition = 1) => new()
    {
        RunId = 1, WorkflowDefinitionId = definition, TriggerKind = "fixture", Payload = new object(),
        ActivityConfigJson = "{}", ResumePromptJson = prompt, Services = new ServiceCollection().BuildServiceProvider(),
        Logger = NullLogger.Instance
    };
    private static string Prompt(string reason = "ArchiveProbeTimedOut", string? message = null,
        string target = "part", string? step = "s2") => JsonSerializer.Serialize(new
        {targetId = target, knownStepId = step, reason, message});
    private static CloudFileProcessingSignal Retry() => new() {TargetId = "part", Action = "retryCurrentStep"};

    [DataTestMethod]
    [DataRow("ArchiveProbeTimedOut")]
    [DataRow("ArchiveProbeToolFailed")]
    [DataRow("ArchiveProbePrefixLimit")]
    [DataRow("ExtractionPlanUnknown")]
    public void Retry_requires_the_matching_server_saved_wait_and_keeps_frozen_step(string reason)
    {
        var target = new CloudDownloadTarget {Id = "part", ProcessingWaitReason = reason};
        FileProcessingResumeActions.Validate(Context(Prompt(reason, LegacyTimeout)), target, Retry(), "s2");
        Assert.ThrowsExactly<ArgumentException>(() =>
            FileProcessingResumeActions.Validate(Context(Prompt(reason, LegacyTimeout)), target, Retry(), "s1"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            FileProcessingResumeActions.Validate(Context(Prompt(reason, LegacyTimeout)), target, Retry(), null));
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("null")]
    [DataRow("[]")]
    [DataRow("bad json")]
    [DataRow("{}")]
    [DataRow("{\"targetId\":\"other\",\"knownStepId\":\"s2\",\"reason\":\"ArchiveProbeTimedOut\"}")]
    [DataRow("{\"targetId\":\"part\",\"knownStepId\":\"s2\",\"reason\":\"ArchiveProbeTimedOut\",\"allowedActions\":[]}")]
    public void Missing_stale_or_revoked_prompts_cannot_authorize_retry(string? prompt)
    {
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.Validate(Context(prompt),
            new CloudDownloadTarget {Id = "part", ProcessingWaitReason = "ArchiveProbeTimedOut"}, Retry(), "s2"));
    }

    [DataTestMethod]
    [DataRow("password")]
    [DataRow("plan")]
    [DataRow("archive")]
    [DataRow("policy")]
    [DataRow("recursive")]
    [DataRow("target")]
    [DataRow("action")]
    public void Retry_is_not_a_way_to_replace_instructions_or_target(string field)
    {
        var signal = Retry();
        signal = field switch
        {
            "password" => signal with {Password = "new"},
            "plan" => signal with {ExtractionPlanJson = "{}"},
            "archive" => signal with {ArchiveId = "other"},
            "policy" => signal with {ArchivePolicy = "keep"},
            "recursive" => signal with {RecursiveOptions = new()},
            "target" => signal with {TargetId = "other"},
            "action" => signal with {Action = "retryEverything"},
            _ => signal
        };
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.Validate(Context(Prompt()),
            new CloudDownloadTarget {Id = "part", ProcessingWaitReason = "ArchiveProbeTimedOut"}, signal, "s2"));
    }

    [DataTestMethod]
    [DataRow("ArchiveDetectionBudgetExceeded")]
    [DataRow("ArchiveProbeOutputLimit")]
    [DataRow("ArchiveProbeIncomplete")]
    [DataRow("ProcessingInputNotMatched")]
    [DataRow("PasswordUnknown")]
    [DataRow("ArchiveInputRequired")]
    public void Manual_review_causes_do_not_gain_retry_from_diagnostic_text(string reason)
    {
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.Validate(Context(Prompt(reason, LegacyTimeout)),
            new CloudDownloadTarget {Id = "part", ProcessingWaitReason = reason}, Retry(), "s2"));
    }

    [TestMethod]
    public void Archive_group_consent_requires_the_persisted_offer_and_exact_frozen_input()
    {
        var token = new string('a', 64);
        var source = new CloudDownloadTarget {Id = "part", ProcessingWaitReason = "MissingVolumes"};
        var inspect = new CloudFileProcessingSignal {TargetId = source.Id, ArtifactId = "input", Fingerprint = "fingerprint",
            StepId = "s2", Action = FileProcessingResumeActions.InspectGroup};
        var retain = inspect with {Action = FileProcessingResumeActions.RetainGroup, ArchiveGroupToken = token};
        string GroupPrompt(bool enriched, string message) => JsonSerializer.Serialize(new {targetId = source.Id, artifactId = "input",
            fingerprint = "fingerprint", knownStepId = "s2", reason = "MissingVolumes", message,
            allowedActions = enriched ? new[]{FileProcessingResumeActions.RetainGroup} : [], archiveGroupInspected = enriched,
            incompleteArchiveGroup = enriched ? new {token} : null});
        var legacy = GroupPrompt(false, FileProcessingResumeActions.LegacyGroupMissingVolumes);
        var current = GroupPrompt(true, "A required archive volume is missing.");
        FileProcessingResumeActions.ValidateGroup(Context(legacy), source, inspect, "s2");
        FileProcessingResumeActions.ValidateGroup(Context(current), source, retain, "s2");
        Assert.IsTrue(FileProcessingResumeActions.IsLegacyGroupProbeWait(legacy));
        Assert.IsFalse(FileProcessingResumeActions.IsLegacyGroupProbeWait(GroupPrompt(false, "A split family is incomplete.")));
        foreach (var wrong in new[]{retain with {ArchiveGroupToken = new string('b',64)}, retain with {StepId="s1"},
                     retain with {TargetId="other"}, retain with {ArtifactId="different"}, retain with {Fingerprint="different"},
                     retain with {ArchivePolicy="keep"}, retain with {Password="answer"}, inspect})
            Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.ValidateGroup(Context(current),source,wrong,"s2"));
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.ValidateGroup(Context(legacy),source,retain,"s2"));
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.ValidateGroup(Context(null),source,retain,"s2"));
    }

    [TestMethod]
    public void SourcePasswordCheckOnlyResumesAnUnreviewedOriginalAcquisitionArchive()
    {
        var prompt = """{"reason":"PasswordUnknown","targetId":"p","archiveId":"nested"}""";
        var signal = new CloudFileProcessingSignal {TargetId = "p", ArchiveId = "nested", Action = FileProcessingResumeActions.TryPostPasswordEvidence};
        void Check(string json, CloudFileProcessingSignal answer, int workflow = 0) =>
            FileProcessingResumeActions.Validate(Context(json, workflow), "p", "PasswordUnknown", answer, allowSourceEvidence: true);
        Check(prompt, signal);
        Assert.ThrowsExactly<ArgumentException>(() => Check(prompt, signal, 1));
        Assert.ThrowsExactly<ArgumentException>(() => Check(prompt, signal with {ArchiveId = null}));
        Assert.ThrowsExactly<ArgumentException>(() => Check(prompt, signal with {Password = "untrusted"}));
        Assert.ThrowsExactly<ArgumentException>(() => Check(prompt, signal with {TargetId = "other"}));
        Assert.ThrowsExactly<ArgumentException>(() => Check(prompt.Replace("PasswordUnknown", "ArchiveInputRequired"), signal));
        Assert.ThrowsExactly<ArgumentException>(() => Check(prompt.Replace("{", """{"sourcePasswordEvidenceChecked":true,"""), signal));
    }

    [TestMethod]
    public void Legacy_compatibility_accepts_only_the_exact_emitted_timeout()
    {
        Assert.AreEqual(ArchiveFormatProbeFailureCodes.TimedOut,
            FileProcessingResumeActions.NormalizeReason("ExtractionPlanUnknown", LegacyTimeout));
        Assert.AreEqual("ExtractionPlanUnknown",
            FileProcessingResumeActions.NormalizeReason("ExtractionPlanUnknown", LegacyTimeout + " Other failure."));
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.Validate(Context(Prompt("ExtractionPlanUnknown", "Unknown plan")),
            new CloudDownloadTarget {Id = "part", ProcessingWaitReason = "ExtractionPlanUnknown"}, Retry(), "s2"));
    }

    [TestMethod]
    public void Legacy_media_probe_can_recheck_only_the_frozen_saved_step()
    {
        const string reason = "ExtractionPlanUnknown";
        var target = new CloudDownloadTarget {Id = "part", ProcessingWaitReason = reason};
        var context = Context(Prompt(reason, LegacyMediaProbe));
        Assert.AreEqual(ArchiveFormatProbeFailureCodes.UpgradeRequired,
            FileProcessingResumeActions.NormalizeReason(reason, LegacyMediaProbe));
        FileProcessingResumeActions.Validate(context, target, Retry(), "s2");
        Assert.ThrowsExactly<ArgumentException>(() =>
            FileProcessingResumeActions.Validate(context, target, Retry(), "s1"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            FileProcessingResumeActions.Validate(context, target, Retry() with {ExtractionPlanJson = "{}"}, "s2"));
        Assert.ThrowsExactly<ArgumentException>(() =>
            FileProcessingResumeActions.Validate(context, target with {ProcessingWaitReason = "ArchiveProbeIncomplete"}, Retry(), "s2"));
    }

    [DataTestMethod]
    [DataRow("ArchiveProbeIncomplete", LegacyMediaProbe)]
    [DataRow("ArchiveInputRequired", LegacyMediaProbe)]
    [DataRow("ExtractionPlanUnknown", LegacyMediaProbe + " Other failure.")]
    [DataRow("ExtractionPlanUnknown", "No supported archive matched this step.")]
    public void Current_or_unrelated_probe_failures_do_not_gain_legacy_retry(string reason, string message)
    {
        Assert.IsFalse(FileProcessingResumeActions.CanRetry(FileProcessingResumeActions.NormalizeReason(reason, message)));
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.Validate(Context(Prompt(reason, message)),
            new CloudDownloadTarget {Id = "part", ProcessingWaitReason = reason}, Retry(), "s2"));
    }

    [TestMethod]
    public void Explicit_retry_revocation_also_applies_to_legacy_media_probe()
    {
        var prompt = JsonSerializer.Serialize(new {targetId = "part", knownStepId = "s2",
            reason = "ExtractionPlanUnknown", message = LegacyMediaProbe, allowedActions = Array.Empty<string>()});
        Assert.ThrowsExactly<ArgumentException>(() => FileProcessingResumeActions.Validate(Context(prompt),
            new CloudDownloadTarget {Id = "part", ProcessingWaitReason = "ExtractionPlanUnknown"}, Retry(), "s2"));
    }

    [TestMethod]
    public void Existing_password_answers_do_not_require_new_action_metadata()
    {
        FileProcessingResumeActions.Validate(Context(null), new CloudDownloadTarget {Id = "part"},
            new() {TargetId = "part", Password = "correct"}, "s2");
    }

    [TestMethod]
    public void Old_prefix_limit_empty_actions_can_only_recheck_the_saved_frozen_step()
    {
        const string reason="ArchiveProbePrefixLimit";
        var target=new CloudDownloadTarget{Id="part",ProcessingWaitReason=reason};
        var context=Context(JsonSerializer.Serialize(new{targetId="part",knownStepId="s2",reason,allowedActions=Array.Empty<string>()}));
        FileProcessingResumeActions.Validate(context,target,Retry(),"s2");
        Assert.ThrowsExactly<ArgumentException>(()=>FileProcessingResumeActions.Validate(context,target,Retry(),"s3"));
        Assert.ThrowsExactly<ArgumentException>(()=>FileProcessingResumeActions.Validate(context,target,Retry() with {Password="new"},"s2"));
        Assert.ThrowsExactly<ArgumentException>(()=>FileProcessingResumeActions.Validate(context,target,Retry() with {ExtractionPlanJson="{}"},"s2"));
        Assert.ThrowsExactly<ArgumentException>(()=>FileProcessingResumeActions.Validate(context,target with{ProcessingWaitReason="ArchiveProbeIncomplete"},Retry(),"s2"));
        var differentAction=Context(JsonSerializer.Serialize(new{targetId="part",knownStepId="s2",reason,allowedActions=new[]{"manualOnly"}}));
        Assert.ThrowsExactly<ArgumentException>(()=>FileProcessingResumeActions.Validate(differentAction,target,Retry(),"s2"));
    }
}
