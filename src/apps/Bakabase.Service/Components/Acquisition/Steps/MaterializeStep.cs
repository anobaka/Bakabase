using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Components;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Notification.Abstractions.Models.Input;
using Bakabase.Modules.Notification.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Acquisition.Steps;

/// <summary>
/// Points the resource at the files, which is the moment the whole thing was for.
/// <para>
/// Everything before this was preparation on disk; this is where the library learns about it. It is
/// one call, because gaining local files is a single operation with one implementation — the
/// materialization service — rather than something each caller reassembles.
/// </para>
/// </summary>
public class MaterializeStep : IAcquisitionStep
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public string Kind => AcquisitionStepKinds.Materialize;
    public string DisplayName => "Point the resource at the files";
    public Type? ConfigType => typeof(Config);

    public record Config
    {
        /// <summary>
        /// Delete the run's working directory afterwards. On by default: what is left there is
        /// either nothing or the leavings of an archive that has been unpacked and moved.
        /// </summary>
        public bool CleanWorkingDirectory { get; init; } = true;

        /// <summary>Say so when it lands. On by default — this is the end of something the user asked for.</summary>
        public bool Notify { get; init; } = true;
    }

    public async Task<AcquisitionStepOutcome> ExecuteAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        var config = ctx.GetConfig<Config>() ?? new Config();
        var target = item.TargetDirectory;

        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            return new AcquisitionStepOutcome.Fail(
                "Nothing was filed away, so there is nothing to point the resource at.");
        }

        try
        {
            var materialization = ctx.ServiceProvider.GetRequiredService<IResourceMaterializationService>();
            var result = await materialization.MaterializeAsync(item.ResourceId, target,
                MaterializationOptions.Default, ct);

            // The name may still be the placeholder the user typed before anything was known about
            // it; what the shared content called it is better, and it is what the folder is called.
            await UpdateNameIfPlaceholderAsync(ctx, item, ct);

            // The library folder is under a path mark, so a sync is what turns the new folder into
            // properties. Queued rather than awaited: it is a background concern.
            await ctx.ServiceProvider.GetRequiredService<IPathMarkSyncService>().EnqueueSync();

            if (config.Notify) await NotifyAsync(ctx, item, result.ResourceId, ct);

            if (config.CleanWorkingDirectory) CleanUp(ctx);

            await ctx.ReportProgress(100, target);

            return new AcquisitionStepOutcome.Continue(item);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new AcquisitionStepOutcome.Fail(
                $"The files are in place but the resource could not be pointed at them: {ex.Message}", ex);
        }
    }

    private static async Task UpdateNameIfPlaceholderAsync(AcquisitionStepContext ctx,
        AcquisitionWorkItem item, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(item.Title)) return;

        try
        {
            var values = ctx.ServiceProvider.GetRequiredService<IReservedPropertyValueService>();
            var all = await values.GetAll(v => v.ResourceId == item.ResourceId);

            // Only when it has no name of its own. A name the user typed themselves is theirs.
            if (all.Any(v => !string.IsNullOrEmpty(v.Name))) return;

            var scoped = all.FirstOrDefault(v => v.Scope == (int) PropertyValueScope.Manual);

            if (scoped == null)
            {
                await values.Add(new Bakabase.Abstractions.Models.Domain.ReservedPropertyValue
                {
                    ResourceId = item.ResourceId,
                    Scope = (int) PropertyValueScope.Manual,
                    Name = item.Title,
                });
            }
            else
            {
                scoped.Name = item.Title;
                await values.Update(scoped);
            }
        }
        catch (Exception ex)
        {
            ctx.Logger.LogWarning(ex, "[Acquisition] Could not name resource {ResourceId}", item.ResourceId);
        }
    }

    private static async Task NotifyAsync(AcquisitionStepContext ctx, AcquisitionWorkItem item,
        int resourceId, CancellationToken ct)
    {
        try
        {
            await ctx.ServiceProvider.GetRequiredService<INotificationService>().CreateAsync(
                new NotificationCreationInputModel
                {
                    Source = $"acquisition:{resourceId}",
                    Title = item.Title ?? item.WorkingName,
                    Body = item.TargetDirectory,
                    // The route is what makes the notification worth clicking: it opens the thing
                    // that just arrived rather than a list to go looking in.
                    PayloadJson = JsonSerializer.Serialize(new
                    {
                        route = $"/resource?ids={resourceId}",
                        resourceId,
                    }, Json),
                    Severity = AppNotificationSeverity.Success,
                });
        }
        catch (Exception ex)
        {
            ctx.Logger.LogWarning(ex, "[Acquisition] Could not send the arrival notification");
        }
    }

    private static void CleanUp(AcquisitionStepContext ctx)
    {
        try
        {
            if (Directory.Exists(ctx.WorkingDirectory)) Directory.Delete(ctx.WorkingDirectory, true);
        }
        catch (IOException ex)
        {
            // Something is still holding a file. The directory is named after the task and will be
            // reused if it is retried, so leaving it is harmless.
            ctx.Logger.LogInformation(ex, "[Acquisition] Could not clear {Directory}", ctx.WorkingDirectory);
        }
    }
}
