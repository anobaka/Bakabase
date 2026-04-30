using System;
using System.Linq;
using Bakabase.Demos.Demos;

namespace Bakabase.Demos;

/// <summary>
/// Entry point for the Avalonia control preview project. Each demo is a self-contained
/// subcommand that spins up Avalonia, shows the relevant control with synthetic state, and
/// auto-closes when its scripted sequence finishes (or when the user closes the window
/// where applicable).
///
/// Run from the repo root:
/// <code>
/// dotnet run --project src/tests/Bakabase.Demos -- splash
/// dotnet run --project src/tests/Bakabase.Demos -- splash --lang en-US --mode merge
/// </code>
/// </summary>
public static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            return PrintUsage();
        }

        var name = args[0];
        var rest = args.Skip(1).ToArray();

        return name.ToLowerInvariant() switch
        {
            "splash" or "relocation-splash" => RelocationSplashDemo.Run(rest),
            "init" or "initialization" => InitializationWindowDemo.Run(rest),
            "list" or "--help" or "-h" => PrintUsage(),
            _ => UnknownDemo(name),
        };
    }

    private static int UnknownDemo(string name)
    {
        Console.Error.WriteLine($"Unknown demo: {name}");
        Console.Error.WriteLine();
        return PrintUsage();
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Bakabase.Demos — visual previews for Avalonia controls.");
        Console.WriteLine();
        Console.WriteLine("Usage:");
        Console.WriteLine("  dotnet run --project src/tests/Bakabase.Demos -- <demo> [args...]");
        Console.WriteLine();
        Console.WriteLine("Available demos:");
        Console.WriteLine("  splash    Show RelocationSplashWindow with synthetic copy progress.");
        Console.WriteLine("            Flags:");
        Console.WriteLine("              --lang zh-CN | en-US     (default: zh-CN)");
        Console.WriteLine("              --mode merge | use       (default: merge)");
        Console.WriteLine("              --files N                (synthetic file count, default 200)");
        Console.WriteLine("  init      Show InitializationWindow walking through the boot phases");
        Console.WriteLine("            (Initializing → Making backups → Migrating → Finishing up).");
        return 0;
    }
}
