#if DEBUG
using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;

namespace Bakabase.Service.Components.Tasks.Dev;

/// <summary>
/// Manual-testing playground for the BTask lifecycle. Both classes below
/// are wrapped in <c>#if DEBUG</c> so they don't ship in Release builds at
/// all — using <c>IWebHostEnvironment.IsDevelopment()</c> alone wasn't
/// enough because the desktop launcher doesn't set
/// <c>ASPNETCORE_ENVIRONMENT</c>, so a debug-built desktop run would still
/// report Production and skip these tasks.
///
/// Why two of them: a long-running persistent task exercises the "recurring
/// + pause / resume / stop" path including the auto-restart behavior; a
/// one-time task exercises the "one-shot + cancel" path and deliberately
/// spends part of its body in a non-yielding section so authors can see
/// the Pausing / Cancelling transitional chips light up.
/// </summary>
public class DevTestPersistentTask : AbstractPredefinedBTaskBuilder
{
    public DevTestPersistentTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "DevTestPersistent";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(1);

    public override string GetName() => "[Dev] Persistent Test Task";

    public override string? GetDescription() =>
        "Recurring dev-only task — ~60s body, then waits 1 minute before the next run. " +
        "Includes a deliberately non-yielding 3s section midway so you can watch the " +
        "Pausing / Cancelling chips linger while the body is stuck between yield points.";

    public override string? GetMessageOnInterruption() =>
        "This is a dev-only test task. Stopping is safe.";

    public override async Task RunAsync(BTaskArgs args)
    {
        const int totalSteps = 60;
        for (var i = 0; i < totalSteps; i++)
        {
            await args.YieldAsync();

            // Halfway through, do a 3-second non-yielding sleep so the user
            // can see the Pausing / Cancelling chip linger if they click
            // during this window — the whole point of the transitional
            // states. Real task code should not do this; we do it on
            // purpose here as a demo.
            if (i == totalSteps / 2)
            {
                await Task.Delay(3000); // intentionally ignores ct
            }

            await args.UpdateTask(t =>
            {
                t.Percentage = (i + 1) * 100 / totalSteps;
                t.Process = $"step {i + 1}/{totalSteps}";
            });

            await Task.Delay(TimeSpan.FromSeconds(1), args.CancellationToken);
        }
    }
}

public class DevTestOneOffTask : AbstractPredefinedBTaskBuilder
{
    public DevTestOneOffTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "DevTestOneOff";

    public override bool IsEnabled() => true;

    public override TimeSpan? GetInterval() => null; // one-time at startup

    public override bool IsPersistent => false;

    public override string GetName() => "[Dev] One-off Test Task";

    public override string? GetDescription() =>
        "One-shot dev-only task — ~30s body, runs once at startup. Fully cooperative " +
        "(every iteration yields), so pause / resume / stop should respond within a " +
        "few hundred ms.";

    public override string? GetMessageOnInterruption() =>
        "This is a dev-only test task. Stopping is safe.";

    public override async Task RunAsync(BTaskArgs args)
    {
        const int totalSteps = 30;
        for (var i = 0; i < totalSteps; i++)
        {
            await args.YieldAsync();

            await args.UpdateTask(t =>
            {
                t.Percentage = (i + 1) * 100 / totalSteps;
                t.Process = $"step {i + 1}/{totalSteps}";
            });

            await Task.Delay(TimeSpan.FromSeconds(1), args.CancellationToken);
        }
    }
}
#endif
