using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

/// <summary>
/// One global recurring task that iterates every enabled subscription and runs its check.
/// Per-Kind / per-subscription interval splitting is a later enhancement — for now everyone
/// shares the same 30-minute cadence.
/// </summary>
public class SubscriptionCheckTask : AbstractPredefinedBTaskBuilder
{
    public SubscriptionCheckTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "SubscriptionCheck";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(30);

    // Don't clash with the existing third-party syncs that hit the same hosts.
    public override HashSet<string>? ConflictKeys =>
    [
        Id,
        "SyncExHentai",
        "SyncPixiv",
    ];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SubscriptionCheckTask>>();

        var enabled = await svc.SearchAsync(new() { EnabledOnly = true });
        if (enabled.Count == 0)
        {
            await args.UpdateTask(t =>
            {
                t.Percentage = 100;
                t.Process = "0/0";
            });
            return;
        }

        var done = 0;
        foreach (var sub in enabled)
        {
            await args.YieldAsync();
            try
            {
                await svc.RunCheckAsync(sub.Id, args.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Service is already recording LastError; just log here for diagnostics.
                logger.LogWarning(ex, "Subscription {Id} ({Kind}) check failed", sub.Id, sub.Kind);
            }

            done++;
            await args.UpdateTask(t =>
            {
                t.Percentage = done * 100 / enabled.Count;
                t.Process = $"{done}/{enabled.Count}";
            });
        }
    }
}
