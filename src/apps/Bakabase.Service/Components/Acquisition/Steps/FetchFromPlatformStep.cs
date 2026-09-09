using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>
/// Asks the platform that holds the resource to hand it over.
/// <para>
/// The other half of getting things: a work bought on DLsite, a gallery on ExHentai, a game on
/// Steam needs no forum post and no cloud drive — the user already has a right to it, and the
/// platform has a way of giving it to them. Each platform's way is its own, which is what the
/// connector is for; what this step knows is only that a fetch either finishes or is under way.
/// </para>
/// </summary>
public class FetchFromPlatformStep : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.FetchFromPlatform;
    public string DisplayName => "Fetch from the platform";
    public Type? ConfigType => null;

    /// <summary>What the interface tells the user while the platform is working.</summary>
    public record Prompt(string Source, string SourceKey, string? Note, DateTime WaitingSince);

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        if (item.LeadKind != AcquisitionLeadKind.PlatformHolding)
        {
            return new AcquisitionStepOutcome.Skip(
                "This is not being fetched from a platform.", item);
        }

        if (!TryReadLead(item.LeadValue, out var source, out var sourceKey))
        {
            return new AcquisitionStepOutcome.Fail(
                $"\"{item.LeadValue}\" does not name a platform and a key.");
        }

        var connector = Connector(ctx, source);

        if (connector == null)
        {
            return new AcquisitionStepOutcome.Fail($"Nothing here knows how to fetch from {source}.");
        }

        if (!connector.CanFetch)
        {
            return new AcquisitionStepOutcome.Fail(
                $"{source} says what exists but does not hand files over.");
        }

        // Already here — from a previous run, or because the user fetched it themselves while
        // this was queued. Steps re-run after a restart, so noticing is not optional.
        if (await connector.DetectLocalPathAsync(sourceKey, ct) is {Length: > 0} existing)
        {
            return Arrived(item, existing);
        }

        var outcome = await connector.FetchAsync(sourceKey, ctx.WorkingDirectory, ctx.ReportProgress, ct);

        return outcome switch
        {
            PlatformFetchOutcome.Done done => Arrived(item, done.Directory),
            PlatformFetchOutcome.Refused refused => new AcquisitionStepOutcome.Fail(refused.Why),
            PlatformFetchOutcome.Started started => new AcquisitionStepOutcome.Suspend(
                AcquisitionWaitReason.PlatformFetch,
                JsonSerializer.Serialize(
                    new Prompt(source.ToString(), sourceKey, started.Note, DateTime.Now), Json),
                item),
            _ => new AcquisitionStepOutcome.Fail($"{source} answered in a way this does not understand."),
        };
    }

    /// <summary>
    /// Resumed when the platform has finished. Nothing is taken on trust from the signal: the
    /// files either exist or they do not, and the connector is what knows.
    /// </summary>
    public async Task<AcquisitionStepOutcome> ResumeAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, AcquisitionResumeSignal signal, CancellationToken ct)
    {
        if (!TryReadLead(item.LeadValue, out var source, out var sourceKey))
        {
            return new AcquisitionStepOutcome.Fail(
                $"\"{item.LeadValue}\" does not name a platform and a key.");
        }

        var connector = Connector(ctx, source);

        if (connector == null)
        {
            return new AcquisitionStepOutcome.Fail($"Nothing here knows how to fetch from {source}.");
        }

        if (await connector.DetectLocalPathAsync(sourceKey, ct) is {Length: > 0} path)
        {
            return Arrived(item, path);
        }

        // Woken too early — the watcher checks on a timer and a download can finish between two
        // of its ticks. Going back to waiting is right; failing would lose a fetch that is fine.
        return new AcquisitionStepOutcome.Suspend(
            AcquisitionWaitReason.PlatformFetch,
            JsonSerializer.Serialize(
                new Prompt(source.ToString(), sourceKey, null, DateTime.Now), Json),
            item);
    }

    /// <summary>
    /// The files are where the platform put them, which is rarely the run's working directory.
    /// Recording it as the extracted directory lets placement move it like anything else.
    /// </summary>
    private static AcquisitionStepOutcome Arrived(AcquisitionWorkItem item, string directory) =>
        new AcquisitionStepOutcome.Continue(item with
        {
            ExtractedDirectory = directory,
            WorkingName = string.IsNullOrEmpty(item.WorkingName)
                ? System.IO.Path.GetFileName(directory.TrimEnd('/', '\\'))
                : item.WorkingName,
        });

    private static IPlatformConnector? Connector(AcquisitionStepContext ctx, ResourceSource source) =>
        ctx.ServiceProvider.GetRequiredService<IPlatformConnectorRegistry>().Get(source);

    /// <summary>
    /// A platform lead is written the way a source link is: <c>Source:SourceKey</c>. An ExHentai
    /// key has a slash in it and a Steam key is a number, so only the first colon separates.
    /// </summary>
    public static bool TryReadLead(string leadValue, out ResourceSource source, out string sourceKey)
    {
        source = default;
        sourceKey = "";

        var separator = leadValue.IndexOf(':');

        if (separator <= 0 || separator == leadValue.Length - 1) return false;

        if (!Enum.TryParse(leadValue[..separator], true, out source)) return false;

        sourceKey = leadValue[(separator + 1)..];

        return true;
    }
}
