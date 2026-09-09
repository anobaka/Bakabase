using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Identity;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;
using Bakabase.InsideWorld.Business.Components.PostParser.Handlers;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Domain;
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
    public Type? ConfigType => typeof(Config);

    public record Config
    {
        /// <summary>
        /// Never buy unattended, whatever the global limit says. For a recipe the user wants to
        /// keep free even though they are happy to spend elsewhere.
        /// </summary>
        public bool NeverBuy { get; init; }
    }

    /// <summary>What the interface shows while the run waits for a purchase to be approved.</summary>
    public record PurchasePrompt(IReadOnlyList<LockedPart> Locked, decimal Limit, string Where);

    public record LockedPart(string Url, decimal? Price);

    /// <summary>The answer: approve the purchase, or do not.</summary>
    public record PurchaseSignal(bool Approved);

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        var reference = item.LeadValue;

        if (string.IsNullOrWhiteSpace(reference))
        {
            return new AcquisitionStepOutcome.Fail("There is no page or text to read.");
        }

        var resolver = ctx.ServiceProvider.GetRequiredService<SharedContentReaderResolver>();
        var reader = resolver.Resolve(reference);

        if (reader == null)
        {
            return new AcquisitionStepOutcome.Fail(
                $"Nothing here knows how to read \"{Shorten(reference)}\".");
        }

        PostContent content;
        try
        {
            await ctx.ReportProgress(10, "Reading the shared content");
            content = await reader.ReadAsync(reference, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail($"Could not read the shared content: {ex.Message}", ex);
        }

        var locked = content.Locks.Unbought().ToList();

        if (locked.Count > 0)
        {
            var limit = ctx.GetConfig<Config>()?.NeverBuy == true
                ? 0m
                : ctx.ServiceProvider.GetRequiredService<IBOptions<AcquisitionOptions>>().Value.AutoPurchaseLimit;
            var affordable = locked.Where(l => l.IsWithin(limit) && limit > 0).ToList();
            var tooDear = locked.Except(affordable).ToList();

            if (tooDear.Count > 0)
            {
                // Spending money is the one thing in this pipeline that must never happen quietly.
                return new AcquisitionStepOutcome.Suspend(
                    AcquisitionWaitReason.PaidContent,
                    JsonSerializer.Serialize(new PurchasePrompt(
                        tooDear.Select(l => new LockedPart(l.Url!, l.Price)).ToList(),
                        limit, HostOf(reference)), Json),
                    item);
            }

            content = await BuyAndReread(ctx, reader, reference, affordable, content, ct);
        }

        return await ExtractAsync(ctx, item, reader, reference, content, ct);
    }

    public async Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, AcquisitionResumeSignal signal, CancellationToken ct)
    {
        PurchaseSignal? answer = null;

        if (!string.IsNullOrWhiteSpace(signal.PayloadJson))
        {
            try { answer = JsonSerializer.Deserialize<PurchaseSignal>(signal.PayloadJson, Json); }
            catch (JsonException ex)
            {
                return new AcquisitionStepOutcome.Fail($"The answer was not readable: {ex.Message}");
            }
        }

        var reference = item.LeadValue;
        var reader = ctx.ServiceProvider.GetRequiredService<SharedContentReaderResolver>().Resolve(reference);

        if (reader == null)
        {
            return new AcquisitionStepOutcome.Fail($"Nothing here knows how to read \"{Shorten(reference)}\".");
        }

        var content = await reader.ReadAsync(reference, ct);

        if (answer?.Approved == true)
        {
            content = await BuyAndReread(ctx, reader, reference, content.Locks.Unbought().ToList(), content, ct);
        }
        // Declining is not a failure: the free part of a post often has everything needed, and if
        // it does not, the link step will say so in its own words.

        return await ExtractAsync(ctx, item, reader, reference, content, ct);
    }

    private static async Task<PostContent> BuyAndReread(AcquisitionStepContext ctx, ISharedContentReader reader,
        string reference, IReadOnlyList<SharedContentLock> toBuy, PostContent content, CancellationToken ct)
    {
        if (toBuy.Count == 0 || reader.Source is not { } source) return content;

        var purchaser = ctx.ServiceProvider.GetServices<ISharedContentPurchaser>()
            .FirstOrDefault(p => p.Source == source);

        if (purchaser == null) return content;

        foreach (var l in toBuy)
        {
            await purchaser.BuyAsync(l.Url!, ct);
            ctx.Logger.LogInformation("[Acquisition] Bought a locked part of {Reference} for {Price}",
                reference, l.Price);
        }

        return await reader.ReadAsync(reference, ct);
    }

    /// <summary>
    /// Hands the content to the existing DownloadInfo extractor and writes what comes back onto the
    /// work item — plus, if the text carried a platform id, onto the resource itself.
    /// </summary>
    private async Task<AcquisitionStepOutcome> ExtractAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, ISharedContentReader reader, string reference, PostContent content,
        CancellationToken ct)
    {
        await ctx.ReportProgress(50, "Looking for download links");

        var handler = ctx.ServiceProvider.GetServices<IPostParseTargetHandler>()
            .FirstOrDefault(h => h.Target == PostParseTarget.DownloadInfo);

        if (handler == null)
        {
            return new AcquisitionStepOutcome.Fail("The download-info extractor is not available.");
        }

        PostParseHandlerResult result;
        try
        {
            result = await handler.HandleAsync(content, ct);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail($"Could not read download links out of it: {ex.Message}", ex);
        }

        var resources = ReadResources(result.Data);
        var links = resources
            .Where(r => !string.IsNullOrWhiteSpace(r.Link))
            .Select(r => new AcquisitionLink(r.Link!.Trim(), Blank(r.Code), Blank(r.Password),
                r.DriveKind == AcquisitionDriveKind.Unknown ? AcquisitionDriveKinds.Infer(r.Link) : r.DriveKind))
            .ToList();

        var title = result.OptimizedTitle ?? content.Title;

        await AttachIdentitiesAsync(ctx, item.ResourceId, content, reference);

        await ctx.ReportProgress(100, links.Count == 0 ? "No links found" : $"{links.Count} links found");

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
            var found = ExternalIdentityParser.TryExtract(text, out var source, out var key) && key != null
                ? new ResourceSourceLink {Source = source, SourceKey = key}
                : null;

            if (found == null) return;

            await ctx.ServiceProvider.GetRequiredService<IResourceSourceLinkService>()
                .EnsureLinks(resourceId, [found]);
        }
        catch (Exception ex)
        {
            // Nice to have, never worth stopping an acquisition for.
            ctx.Logger.LogDebug(ex, "[Acquisition] Could not attach an identity found in {Reference}", reference);
        }
    }

    private static List<DownloadInfoResource> ReadResources(object data)
    {
        try
        {
            var json = JsonSerializer.Serialize(data, Json);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("resources", out var resources) ||
                resources.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            return resources.Deserialize<List<DownloadInfoResource>>(Json) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string? Blank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string Shorten(string s) => s.Length <= 80 ? s : s[..80] + "…";

    private static string HostOf(string reference) =>
        Uri.TryCreate(reference, UriKind.Absolute, out var uri) ? uri.Host : "the shared content";
}
