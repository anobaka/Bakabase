using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Platform;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Service.Components.Acquisition.Steps;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition;

/// <summary>
/// Notices when a platform has finished fetching something.
/// <para>
/// A download queued with the ExHentai downloader and an install handed to Steam both end without
/// telling anyone: the run that asked for them is suspended, and nothing inside it can watch. This
/// looks, on a timer, for the files each waiting run is waiting for, and wakes the ones whose
/// files have arrived.
/// </para>
/// </summary>
public class PlatformFetchWatchTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
    : AbstractPredefinedBTaskBuilder(serviceProvider, localizer)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public override string Id => "PlatformFetchWatch";

    public override bool IsEnabled() => true;

    /// <summary>
    /// A download takes minutes and an install can take an hour; a minute late is nothing, and a
    /// tighter loop would ask three platforms the same question for no reason.
    /// </summary>
    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(1);

    /// <summary>Bookkeeping. A row saying "nothing arrived" every minute would bury the task list.</summary>
    public override bool IsPersistent => false;

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var acquisitions = scope.ServiceProvider.GetService<IAcquisitionService>();

        if (acquisitions == null) return;

        var waiting = (await acquisitions.SearchAsync(AcquisitionStatus.Waiting, ct: args.CancellationToken))
            .Where(t => t.WaitReason == AcquisitionWaitReason.PlatformFetch)
            .ToList();

        if (waiting.Count == 0) return;

        var connectors = scope.ServiceProvider.GetRequiredService<IPlatformConnectorRegistry>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<PlatformFetchWatchTask>>();

        foreach (var task in waiting)
        {
            await args.YieldAsync();

            // The prompt is the step's own shape, and it is where the platform and key are; the
            // task row does not carry them.
            FetchFromPlatformStep.Prompt? prompt = null;

            try
            {
                prompt = string.IsNullOrWhiteSpace(task.WaitPromptJson)
                    ? null
                    : JsonSerializer.Deserialize<FetchFromPlatformStep.Prompt>(task.WaitPromptJson, Json);
            }
            catch (JsonException)
            {
                // Written by an older build, or by a different step that used the same reason.
            }

            if (prompt == null || !Enum.TryParse<Bakabase.Abstractions.Models.Domain.Constants.ResourceSource>(
                    prompt.Source, true, out var source))
            {
                continue;
            }

            var connector = connectors.Get(source);

            if (connector == null) continue;

            try
            {
                if (await connector.DetectLocalPathAsync(prompt.SourceKey, args.CancellationToken)
                    is not {Length: > 0})
                {
                    continue;
                }

                // The step checks again for itself; this only says "worth looking".
                await acquisitions.ResumeAsync(task.Id, "{}", args.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "[Acquisition] Could not check on {Source} {SourceKey}",
                    prompt.Source, prompt.SourceKey);
            }
        }
    }
}
