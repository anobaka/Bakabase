using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Bakabase.Windows;

namespace Bakabase.Demos.Demos;

/// <summary>
/// Visual preview for <see cref="InitializationWindow"/>. Spins up Avalonia, shows the boot
/// splash, and walks it through the same phase sequence the real host produces, with
/// synthetic detail lines and an optional determinate fraction so we exercise both modes
/// of the progress bar.
/// </summary>
public static class InitializationWindowDemo
{
    public static int Run(string[] args)
    {
        AppBuilder.Configure<InitDemoApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }
}

internal class InitDemoApp : Application
{
    public override void Initialize()
    {
        Styles.Add(new Avalonia.Themes.Fluent.FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnLastWindowClose;

            var window = new InitializationWindow();
            window.Show();

            _ = RunScriptAsync(window);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunScriptAsync(InitializationWindow window)
    {
        // Mirrors the real boot sequence in AppHost.RunAsync but with synthetic timing /
        // detail, so we can eyeball how the splash reads under each phase.
        await Set(window, "Initializing", null, null);
        await Task.Delay(1200);

        await Set(window, "Making backups", null, null);
        await Task.Delay(800);
        for (var i = 1; i <= 6; i++)
        {
            await Set(window, "Making backups", $"backups/2.3.0-beta.{60 + i}", i / 6d);
            await Task.Delay(250);
        }

        await Set(window, "Migrating", null, null);
        await Task.Delay(600);
        var migrators = new[]
        {
            "PathsRelocationMigrator",
            "AppDataPathRelocationMigrator",
            "EnhancementMigrator",
            "ResourceProfileMigrator",
        };
        for (var i = 0; i < migrators.Length; i++)
        {
            await Set(window, "Migrating", migrators[i], (i + 1) / (double)migrators.Length);
            await Task.Delay(500);
        }

        await Set(window, "Finishing up", null, null);
        await Task.Delay(1500);

        await Dispatcher.UIThread.InvokeAsync(window.Close);
    }

    private static async Task Set(
        InitializationWindow window, string phase, string? detail, double? fraction)
    {
        await Dispatcher.UIThread.InvokeAsync(() => window.SetPhase(phase, detail, fraction));
    }
}
