using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using Bakabase.Infrastructures.Components.App.Relocation;
using Bakabase.Windows;

namespace Bakabase.Demos.Demos;

/// <summary>
/// Spins up Avalonia, shows <see cref="RelocationSplashWindow"/>, and feeds it a scripted
/// sequence of <see cref="RelocationProgress"/> events covering every phase. Once the
/// sequence finishes (or the user closes the window) the app shuts down.
/// </summary>
public static class RelocationSplashDemo
{
    public static int Run(string[] args)
    {
        SplashDemoOptions.Language = ParseFlag(args, "--lang", "zh-CN");
        SplashDemoOptions.Mode = ParseFlag(args, "--mode", "merge");
        SplashDemoOptions.Files = int.TryParse(ParseFlag(args, "--files", "200"), out var f) ? f : 200;

        AppBuilder.Configure<SplashDemoApp>()
            .UsePlatformDetect()
            .StartWithClassicDesktopLifetime(args);
        return 0;
    }

    private static string ParseFlag(string[] args, string name, string @default)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (args[i] == name) return args[i + 1];
        }
        return @default;
    }
}

internal static class SplashDemoOptions
{
    public static string Language { get; set; } = "zh-CN";
    public static string Mode { get; set; } = "merge";
    public static int Files { get; set; } = 200;
}

/// <summary>
/// Minimal <see cref="Application"/> for the demo: just the Fluent theme and a window with a
/// scripted sequence of progress events. Intentionally separate from the real
/// <c>Bakabase.App</c> so we don't trigger pending-relocation discovery, host startup, etc.
/// </summary>
internal class SplashDemoApp : Application
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

            var splash = new RelocationSplashWindow(SplashDemoOptions.Language);
            splash.Show();

            _ = RunScriptAsync(splash, SplashDemoOptions.Mode, SplashDemoOptions.Files);
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static async Task RunScriptAsync(RelocationSplashWindow splash, string mode, int totalFiles)
    {
        // Sequence approximates a real merge-overwrite run on a moderately-sized AppData. The
        // numbers don't have to mean anything — the splash just renders whatever
        // RelocationProgress we hand it.
        await Post(splash, new RelocationProgress { Phase = RelocationPhase.Starting });
        await Task.Delay(800);

        if (mode is "merge" or "copy")
        {
            const long bytesPerFile = 4 * 1024 * 1024;
            for (var i = 1; i <= totalFiles; i++)
            {
                await Post(splash, new RelocationProgress
                {
                    Phase = RelocationPhase.Copying,
                    ProcessedFiles = i,
                    TotalFiles = totalFiles,
                    ProcessedBytes = i * bytesPerFile,
                    TotalBytes = totalFiles * bytesPerFile,
                    CurrentFile = $"data/covers/{i:D5}.jpg",
                });
                await Task.Delay(20);
            }

            await Post(splash, new RelocationProgress { Phase = RelocationPhase.Validating });
            await Task.Delay(900);

            await Post(splash, new RelocationProgress { Phase = RelocationPhase.Replacing });
            await Task.Delay(900);
        }

        await Post(splash, new RelocationProgress { Phase = RelocationPhase.Finalizing });
        await Task.Delay(900);

        await Post(splash, new RelocationProgress { Phase = RelocationPhase.Done });
        await Task.Delay(1000);

        await Dispatcher.UIThread.InvokeAsync(splash.Close);
    }

    private static async Task Post(RelocationSplashWindow splash, RelocationProgress progress)
    {
        await Dispatcher.UIThread.InvokeAsync(() => splash.UpdateProgress(progress));
    }
}
