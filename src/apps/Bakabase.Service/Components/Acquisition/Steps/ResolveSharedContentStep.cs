using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Identity;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>
/// Turns "someone shared this here" into links, codes and a title.
/// <para>
/// It is the post parser, made into a step. The parser already knew how to read a forum thread and
/// have a model pull download links out of it; what it did not have was anywhere for the result to
/// go, or a way to stop and ask when the author is charging for the part that matters. Both of
/// those are what a step is.
/// </para>
/// </summary>
public class ResolveSharedContentStep : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.ResolveSharedContent;
    public string DisplayName => "Read the shared page";
    public string Description => "Read a sharing page or text and extract links, access codes and passwords with the configured post-parsing AI model. This step does not download the shared files.";
    public string DescriptionKey => "workflow.activity.acquisition.resolveSharedContent.description";
    public IReadOnlyList<AcquisitionLeadKind>? AcceptedLeadKinds =>
        [AcquisitionLeadKind.SharedPage, AcquisitionLeadKind.SharedDocument];
    public Type? ConfigType => typeof(Config);

    public async Task<IReadOnlyList<AcquisitionValidationIssue>> ValidateConfigurationAsync(
        AcquisitionValidationContext context, CancellationToken ct)
    {
        if (context.IsExecution && context.InitialLinks is {Count: > 0}) return [];
        var features = context.Services.GetService<IAiFeatureService>();
        var providers = context.Services.GetService<IAiProviderService>();
        var config = features == null ? null : await features.GetConfigAsync(AiFeature.PostParser, ct);
        if (features != null && (config == null || config.UseDefault))
            config = await features.GetConfigAsync(AiFeature.Default, ct);
        if (config?.ProviderConfigId == null || string.IsNullOrWhiteSpace(config.ModelId) || providers == null)
            return [new("acquisition.ai.missing", "Configure an AI provider and model for post parsing or the default AI feature.",
                "workflow.validation.acquisition.aiMissing", DependsOnPayload: true)];
        var provider = await providers.GetAsync(config.ProviderConfigId.Value, ct);
        if (provider == null || !provider.IsEnabled || !provider.LlmEnabled)
            return [new("acquisition.ai.disabled", "The configured post-parsing AI provider is missing or disabled.",
                "workflow.validation.acquisition.aiDisabled", DependsOnPayload: true)];
        return [];
    }

    public record Config
    {
        /// <summary>
        /// Never buy unattended, whatever the global limit says. For a recipe the user wants to
        /// keep free even though they are happy to spend elsewhere.
        /// </summary>
        public bool NeverBuy { get; init; }
    }

    /// <summary>What the interface shows while the run waits for a purchase to be approved.</summary>
    public record PurchasePrompt(IReadOnlyList<LockedPart> Locked, decimal Limit, string Where,
        decimal MinimumRemainingCoins = 0, decimal? Balance = null,
        PostAvailabilityAssessment? Availability = null, string? Message = null);

    public record LockedPart(string Url, decimal? Price);

    /// <summary>The answer: approve the purchase, or do not.</summary>
    public record PurchaseSignal(bool Approved);

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        if (item.Links.Count > 0) return new AcquisitionStepOutcome.Continue(item);
        var reference = item.LeadValue;

        if (string.IsNullOrWhiteSpace(reference))
        {
            return new AcquisitionStepOutcome.Fail("There is no page or text to read.");
        }

        var reader = ctx.ServiceProvider.GetRequiredService<IPostContentService>();

        if (!reader.CanRead(reference))
        {
            return new AcquisitionStepOutcome.Fail(
                $"Nothing here knows how to read \"{Shorten(reference)}\".");
        }

        PostContent content;
        try
        {
            await ctx.ReportProgress(10, BTaskText.Localize(
                ctx.ServiceProvider.GetRequiredService<IBakabaseLocalizer>(), "BTask_Process_ReadingSharedContent"));
            content = await reader.ReadAsync(reference, ct: ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail($"Could not read the shared content: {ex.Message}", ex);
        }

        item = SaveSnapshot(item, content);
        var (limit, reserve) = PurchaseLimits(ctx, content);
        if (content.Locks.Any(l => !l.IsBought))
        {
            PostAvailabilityAssessment assessment;
            try
            {
                assessment = await ctx.ServiceProvider.GetRequiredService<IPostAvailabilityAnalyzer>().AnalyzeAsync(content, ct);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { assessment = new() {Reason = $"Could not assess expiry reports: {ex.Message}"}; }
            item = WithVariable(item, "postParser.availability", JsonSerializer.Serialize(assessment, Json));
            var warnings = new List<string>();
            if (ctx.GetConfig<Config>()?.NeverBuy != true && assessment.Status is "noExpiryReported" or "restored")
            {
                try
                {
                    var result = await ctx.ServiceProvider.GetRequiredService<SharedContentPurchasePolicy>()
                        .PurchaseAsync(reference, content.SourceHint, limit, reserve, ct: ct);
                    content = result.Content;
                    warnings.AddRange(result.Warnings);
                    item = SaveSnapshot(item, content);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { warnings.Add($"The purchase could not be verified. Refresh before retrying: {ex.Message}"); }
            }
            if (content.Locks.Any(l => !l.IsBought))
                return await SuspendAsync(ctx, item, content, limit, reserve, assessment,
                    string.Join("\n", warnings.Prepend(assessment.Reason).Where(w => !string.IsNullOrWhiteSpace(w))), ct);
        }
        return await ExtractAsync(ctx, item, reader, reference, content, ct);
    }

    public async Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, AcquisitionResumeSignal signal, CancellationToken ct)
    {
        PurchaseSignal? answer;
        try { answer = JsonSerializer.Deserialize<PurchaseSignal>(signal.PayloadJson ?? "{}", Json); }
        catch (JsonException ex) { return new AcquisitionStepOutcome.Fail($"The answer was not readable: {ex.Message}"); }
        var reference = item.LeadValue;
        var reader = ctx.ServiceProvider.GetRequiredService<IPostContentService>();
        if (!reader.CanRead(reference))
            return new AcquisitionStepOutcome.Fail($"Nothing here knows how to read \"{Shorten(reference)}\".");
        Dictionary<string, decimal?>? approved = null;
        if (item.Variables.TryGetValue("postParser.purchaseQuote", out var quote))
        {
            try { approved = JsonSerializer.Deserialize<Dictionary<string, decimal?>>(quote, Json); }
            catch (JsonException) { /* A lost quote requires a fresh explicit review. */ }
        }
        var content = await reader.ReadAsync(reference, ct: ct);
        var (limit, reserve) = PurchaseLimits(ctx, content);
        item = SaveSnapshot(item, content);
        var warnings = new List<string>();
        if (answer?.Approved == true && approved is {Count: > 0})
        {
            try
            {
                var result = await ctx.ServiceProvider.GetRequiredService<SharedContentPurchasePolicy>()
                    .PurchaseAsync(reference, content.SourceHint, limit, reserve, approved, ct);
                content = result.Content;
                warnings.AddRange(result.Warnings);
                item = SaveSnapshot(item, content);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { warnings.Add($"The purchase could not be verified. Refresh before retrying: {ex.Message}"); }
        }
        else if (answer?.Approved == true)
            warnings.Add("The saved purchase quote is missing. Review the current prices before approving again.");
        else
            warnings.Add("Restricted content remains unpurchased. Unlock it on the source site or approve a purchase before continuing.");
        if (content.Locks.Any(l => !l.IsBought))
            return await SuspendAsync(ctx, item, content, limit, reserve, null, string.Join("\n", warnings), ct);
        return await ExtractAsync(ctx, item, reader, reference, content, ct);
    }

    private static (decimal Limit, decimal Reserve) PurchaseLimits(AcquisitionStepContext ctx, PostContent content)
    {
        if (content.SourceHint == nameof(PostParserSource.SoulPlus))
        {
            var options = ctx.ServiceProvider.GetRequiredService<IBOptions<SoulPlusOptions>>().Value;
            return (options.AutoBuyThreshold, options.MinimumRemainingCoins);
        }
        return (ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.AutoPurchaseLimit, 0);
    }

    private static AcquisitionWorkItem WithVariable(AcquisitionWorkItem item, string key, string value) => item with
    {
        Variables = new Dictionary<string, string>(item.Variables) {[key] = value}
    };

    private static AcquisitionWorkItem SaveSnapshot(AcquisitionWorkItem item, PostContent content) =>
        WithVariable(item, "postParser.contentSnapshot", JsonSerializer.Serialize(content, Json));

    private static async Task<AcquisitionStepOutcome> SuspendAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, PostContent content, decimal limit, decimal reserve,
        PostAvailabilityAssessment? assessment, string? message, CancellationToken ct)
    {
        var locked = content.Locks.Where(l => !l.IsBought).ToList();
        var quote = locked.Where(l => !string.IsNullOrEmpty(l.Url)).GroupBy(l => l.Url!)
            .ToDictionary(g => g.Key, g => g.First().Price);
        item = WithVariable(item, "postParser.purchaseQuote", JsonSerializer.Serialize(quote, Json));
        // A preview may miss a hidden code or inner-layer password; never expose it as executable links yet.
        try
        {
            var partial = await ctx.ServiceProvider.GetRequiredService<IPostDownloadInfoExtractor>().ExtractAsync(content, ct);
            partial = partial with {IsComplete = false, Availability = assessment};
            item = WithVariable(item, "postParser.partialResult", JsonSerializer.Serialize(partial, Json));
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { message = $"{message}\nCould not extract the partial content: {ex.Message}".Trim(); }
        return new AcquisitionStepOutcome.Suspend(AcquisitionWaitReason.PaidContent,
            JsonSerializer.Serialize(new PurchasePrompt(locked.Select(l => new LockedPart(l.Url ?? "", l.Price)).ToList(),
                limit, HostOf(item.LeadValue), reserve, content.Balance, assessment, message), Json), item);
    }

    /// <summary>
    /// Hands the content to the existing DownloadInfo extractor and writes what comes back onto the
    /// work item — plus, if the text carried a platform id, onto the resource itself.
    /// </summary>
    private async Task<AcquisitionStepOutcome> ExtractAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, IPostContentService reader, string reference, PostContent content,
        CancellationToken ct)
    {
        var localizer = ctx.ServiceProvider.GetRequiredService<IBakabaseLocalizer>();
        await ctx.ReportProgress(50, BTaskText.Localize(localizer, "BTask_Process_FindingDownloadLinks"));

        var extractor = ctx.ServiceProvider.GetRequiredService<IPostDownloadInfoExtractor>();
        PostDownloadInfo result;
        try
        {
            result = await extractor.ExtractAsync(content, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail($"Could not read download links out of it: {ex.Message}", ex);
        }

        var resources = result.Resources;
        var links = resources
            .Where(r => !string.IsNullOrWhiteSpace(r.Link))
            .Select(r => new AcquisitionLink(r.Link!.Trim(), Blank(r.Code), Blank(r.Password),
                AcquisitionDriveKinds.Infer(r.Link), r.Extraction == null ? null : JsonSerializer.Serialize(r.Extraction, Json)))
            .ToList();

        var title = result.Title ?? content.Title;

        await AttachIdentitiesAsync(ctx, item.ResourceId, content, reference);

        await ctx.ReportProgress(100, BTaskText.Localize(localizer,
            "BTask_Process_DownloadLinksFound", links.Count));

        return new AcquisitionStepOutcome.Continue(item with
        {
            Links = links,
            Title = string.IsNullOrWhiteSpace(item.Title) ? title : item.Title,
            // Only if nothing has named the folder yet: a text transform earlier in the recipe has
            // the last word over anything a model suggested.
            WorkingName = string.IsNullOrWhiteSpace(item.WorkingName) ? title ?? item.WorkingName : item.WorkingName,
        });
    }

    /// <summary>
    /// A thread that quotes a DLsite code is telling us what the work is. Recording that on the
    /// resource is free here and is what lets the same resource be recognised next time it turns up.
    /// </summary>
    private static async Task AttachIdentitiesAsync(AcquisitionStepContext ctx, int resourceId,
        PostContent content, string reference)
    {
        try
        {
            var text = string.Join("\n", new[] {content.Title, content.MainHtml}
                .Concat(content.CommentHtmlList ?? []));
            if (!ExternalIdentityParser.TryExtract(text, out var thirdPartyId, out var key)) return;

            if (thirdPartyId.ToResourceSource() is { } source)
            {
                await ctx.ServiceProvider.GetRequiredService<IResourceSourceLinkService>()
                    .EnsureLinks(resourceId, [new ResourceSourceLink {Source = source, SourceKey = key}]);
            }
            else
            {
                await ctx.ServiceProvider.GetRequiredService<IResourceExternalIdentityService>()
                    .EnsureIdentities(resourceId,
                        [new ResourceExternalIdentity {ThirdPartyId = thirdPartyId, ExternalId = key}]);
            }
        }
        catch (Exception ex)
        {
            // Nice to have, never worth stopping an acquisition for.
            ctx.Logger.LogDebug(ex, "[Acquisition] Could not attach an identity found in {Reference}", reference);
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Shorten(string s) => s.Length <= 80 ? s : s[..80] + "…";

    private static string HostOf(string reference) =>
        Uri.TryCreate(reference, UriKind.Absolute, out var uri) ? uri.Host : "the shared content";
}
