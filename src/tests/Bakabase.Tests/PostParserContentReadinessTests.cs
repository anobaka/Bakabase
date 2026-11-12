using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.PostParser.Extensions;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using System.Text.Json;
using Content = Bakabase.Modules.PostParser.Models.Domain.PostContent;

namespace Bakabase.Tests;

[TestClass]
public sealed class PostParserContentReadinessTests
{
    [TestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("   ")]
    [DataRow("Optional note from an older client")]
    public void ConfirmationInputAcceptsMissingOrOptionalLegacyReason(string? reason)
    {
        var input = new ConfirmPostParserContentReadinessInput
        {
            ExpectedContentFingerprint = new string('A', 64), NotRequiredBlockIds = ["proof"], Reason = reason
        };
        var errors = new List<ValidationResult>();
        Assert.IsTrue(Validator.TryValidateObject(input, new ValidationContext(input), errors, true),
            string.Join("; ", errors.Select(error => error.ErrorMessage)));
    }

    [TestMethod]
    [DataRow(1000, true)]
    [DataRow(1001, false)]
    public void OptionalLegacyReasonRetainsItsLengthLimit(int length, bool expectedValid)
    {
        var input = new ConfirmPostParserContentReadinessInput
        {
            ExpectedContentFingerprint = new string('A', 64), NotRequiredBlockIds = ["proof"], Reason = new string('x', length)
        };
        Assert.AreEqual(expectedValid, Validator.TryValidateObject(input, new ValidationContext(input), [], true));
    }

    [TestMethod]
    public void OptionalBlocksNeverResolveConflictingPricesForARequiredSharedPaymentTarget()
    {
        var content = new Content {MainHtml = "Download and proof", Balance = 2000, CurrencyCode = "SP", CurrencyName = "SP币",
            Locks = [new("https://example.test/pay", 1, false) {BlockId = "download", ContextText = "download password"},
                new("https://example.test/pay", 999999999, false) {BlockId = "proof", ContextText = "purchase proof"}]};
        content = PostContentReadinessPolicy.Apply(content, PostContentReadinessPolicy.FromAnalysis(content,
            [new() {BlockId = "proof", Relevance = "optional", Purpose = "purchaseProof", Evidence = ["purchase proof"]}]));
        var quote = PostParserPurchaseQuote.Create(content, 10, 1000);
        Assert.AreEqual(0, quote.EligibleLockUrls.Count);
        Assert.AreEqual(1, quote.UnknownPriceCount);
        Assert.AreEqual("SP", quote.CurrencyCode);
        Assert.AreEqual("0", quote.EligibleTotalText);
    }

    [TestMethod]
    public void EntirelyOptionalPaymentTargetsAreExcludedWithoutMarkingThemBought()
    {
        var content = new Content {Locks = [new("https://example.test/pay", 999, false)
            {BlockId = "proof", ContextText = "purchase proof"}]};
        content = PostContentReadinessPolicy.Apply(content, PostContentReadinessPolicy.FromAnalysis(content,
            [new() {BlockId = "proof", Relevance = "optional", Purpose = "purchaseProof", Evidence = ["purchase proof"]}]));
        var quote = PostParserPurchaseQuote.Create(content, 10, 1000);
        Assert.AreEqual(0, quote.ExcludedCount);
        Assert.AreEqual(0, quote.EligibleLockUrls.Count);
        Assert.IsFalse(content.Locks.Single().IsBought);
    }

    [TestMethod]
    public void PersistedManualProofCannotBeForgedInSnapshotAndBecomesStaleWhenContentChanges()
    {
        var content = new Content {MainHtml = "download details", Locks = [new("https://example.test/pay", 5, false) {BlockId = "proof"}]};
        var proof = new PostContentReadinessAssessment {Basis = "manual", PolicyVersion = PostContentReadinessPolicy.Version,
            Fingerprint = PostContentReadinessPolicy.Fingerprint(content), Decisions = [new() {BlockId = "proof", Relevance = "optional"}]};
        var row = new PostParserTaskDbModel {Link = "https://example.test/post", ContentSnapshotJson = JsonSerializer.Serialize(content with {Readiness = proof}, WorkflowJson.Options)};
        Assert.IsNull(row.ToDomainModel().ContentSnapshot!.Readiness);
        var state = new PostParserContentReadinessState {History = [new() {Version = 1, ContentFingerprint = proof.Fingerprint, BlockIds = ["proof"]}]};
        row.ContentReadinessJson = JsonSerializer.Serialize(state, WorkflowJson.Options);
        Assert.AreEqual("manual", row.ToDomainModel().ContentSnapshot!.Readiness!.Basis);
        row.ContentSnapshotJson = JsonSerializer.Serialize(content with {MainHtml = "changed details", Readiness = proof}, WorkflowJson.Options);
        Assert.IsNull(row.ToDomainModel().ContentSnapshot!.Readiness);
        Assert.AreEqual(1, PostParserContentReadinessState.Read(row.ContentReadinessJson).History.Count);
    }
    [TestMethod]
    public void RevokedProofStopsFutureConsumersWithoutRewritingSavedResult()
    {
        var content = new Content {MainHtml = "visible resource", Locks = [new("https://example.test/pay", 5, false) {BlockId = "proof"}]};
        var state = new PostParserContentReadinessState {History = [new() {Version = 1,
            ContentFingerprint = PostContentReadinessPolicy.Fingerprint(content), BlockIds = ["proof"]}]};
        content = state.Apply(content);
        var row = new PostParserTaskDbModel {ParsingState = "complete", Link = "https://example.test/post",
            ContentSnapshotJson = JsonSerializer.Serialize(content, WorkflowJson.Options),
            ContentReadinessJson = JsonSerializer.Serialize(state, WorkflowJson.Options),
            Results = JsonSerializer.Serialize(new System.Collections.Generic.Dictionary<string, PostDownloadInfo> {{"DownloadInfo", new PostDownloadInfo {IsComplete = true,
                ContentReadiness = content.Readiness, Resources = [new() {Link = "https://example.test/file"}]}}}, WorkflowJson.Options)};
        var results = row.Results;
        Assert.IsTrue(Bakabase.InsideWorld.Business.Components.PostParser.Services.PostParserSavedResults.HasCompletedDownloadResult(row));
        row.ContentReadinessJson = JsonSerializer.Serialize(state with {History = state.History.Append(new PostParserContentReadinessEvent
            {Version = 2, Action = "revoke", DecisionId = state.History[0].Id}).ToList()}, WorkflowJson.Options);
        Assert.IsFalse(Bakabase.InsideWorld.Business.Components.PostParser.Services.PostParserSavedResults.HasCompletedDownloadResult(row));
        Assert.AreEqual(results, row.Results);
        var displayed = row.ToDomainModel();
        Assert.IsNull(displayed.ContentSnapshot!.Readiness);
        Assert.IsNull(displayed.Results!.Values.Single()!["contentReadiness"]);
    }

}
