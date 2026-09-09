using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Subscription.Abstractions.Models.Domain;
using Bakabase.Modules.Subscription.Abstractions.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Tasks;

/// <summary>
/// Wakes often and checks the sources that are due.
/// <para>
/// A source's own interval decides when it is due. One shared cadence meant a forum board checked
/// every thirty minutes and a circle's back catalogue checked every thirty minutes, which is either
/// too often for one or too rarely for the other.
/// </para>
/// </summary>
public class SubscriptionCheckTask : AbstractPredefinedBTaskBuilder
{
    public SubscriptionCheckTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "SubscriptionCheck";

    public override bool IsEnabled() => true;

    /// <summary>
    /// How often the task looks, not how often a source is checked. A source due every hour needs
    /// something to notice the hour has passed.
    /// </summary>
    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(5);

    /// <summary>Used when a subscription names no interval of its own.</summary>
    private static readonly TimeSpan DefaultInterval = TimeSpan.FromMinutes(30);

    /// <summary>
    /// A check creates and updates resources, so it must not run beside the syncs that do the same.
    /// </summary>
    public override HashSet<string>? ConflictKeys =>
    [
        Id,
        "SyncExHentai",
        "SyncResources",
    ];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var svc = scope.ServiceProvider.GetRequiredService<ISubscriptionService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<SubscriptionCheckTask>>();

        var now = DateTime.Now;
        var enabled = (await svc.SearchAsync(new() {EnabledOnly = true}))
            .Where(s => IsDue(s, now))
            .ToList();

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

    /// <summary>
    /// Never checked is always due — a source the user has just added should say what it holds
    /// rather than wait out its first interval.
    /// </summary>
    private static bool IsDue(SubscriptionRecord subscription, DateTime now)
    {
        if (subscription.LastCheckedAt is not { } last) return true;

        var interval = subscription.IntervalMinutes is > 0
            ? TimeSpan.FromMinutes(subscription.IntervalMinutes.Value)
            : DefaultInterval;

        return now - last >= interval;
    }
}
