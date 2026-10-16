using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Profiles;
using Bakabase.Infrastructures.Components.App.SingleInstance;
using Bakabase.Infrastructures.Components.Configurations.App;

// Usage:
//   hold  <dir>  DataDirectoryLock.TryAcquire; prints "LOCK <status>". When acquired, keeps the
//                lock until killed or until standard input closes.
//   enter <dir>  SingleInstanceGuard.Enter; prints "ENTRY <result>". When entered, keeps the
//                directory (and answers its activation channel) until killed or stdin closes.
//   launch       As "enter", on the directory the entry point would work out for itself: the
//                anchor from BAKABASE_DATA_DIR, then any redirect in it (AppDataLocator). The
//                variable must be set — without it this would be the real user's data. When
//                entered, the second line is "APPJSON <path>": where AppOptionsManager reads
//                app.json, which is only asked after the guard has let the launch in.
//
// The first line of output is the answer; the tests wait for it before acting.

if (args.Length < 1)
{
    return Usage();
}

var verb = args[0];

switch (verb)
{
    case "desktop" when args.Length == 2:
    {
        var registry = new DesktopProfileRegistry(args[1], Path.Combine(args[1], "legacy"));
        using var session = DesktopWorkspaceSession.TryAcquire(registry.RootDirectory);
        var requested = Environment.GetEnvironmentVariable("BAKABASE_TEST_PROFILE_ID");
        if (session == null)
        {
            var activated = await DesktopWorkspaceSession.ActivateAsync(registry.RootDirectory,
                requested == null ? ActivationChannel.ShowMessage : DesktopWorkspaceSession.SwitchPrefix + requested);
            Console.Out.WriteLine(activated ? "DESKTOP Activated" : "DESKTOP Unavailable");
            return activated ? 0 : 1;
        }
        DesktopProfileSelection.Initialize(requested == null ? [] : [DesktopProfileSelection.ArgumentPrefix + requested], registry);
        using var activation = ActivationServer.Start(DesktopWorkspaceSession.Channel(registry.RootDirectory), message =>
        { Console.Out.WriteLine("ACTIVATED " + message); Console.Out.Flush(); });
        Console.Out.WriteLine("DESKTOP Entered");
        Console.Out.WriteLine("PROFILE " + DesktopProfileSelection.CurrentId);
        Console.Out.Flush();
        WaitForStdinToClose();
        return 0;
    }
    case "profile" when args.Length == 2:
    {
        var registry = new DesktopProfileRegistry(args[1], Path.Combine(args[1], "legacy"));
        var id = Environment.GetEnvironmentVariable("BAKABASE_TEST_PROFILE_ID") ?? "default";
        DesktopProfileSelection.Initialize([DesktopProfileSelection.ArgumentPrefix + id], registry);
        return Enter(AppDataLocator.ResolveEffectiveDataDirectory(),
            () => Console.Out.WriteLine($"APPJSON {AppOptionsManager.GetAppOptionsFilePath()}"));
    }
    case "installation" when args.Length == 2:
    {
        using var lease = new DesktopInstallationInstances(args[1]).Register(Environment.ProcessId);
        Console.Out.WriteLine($"INSTALLATION {Environment.ProcessId}");
        Console.Out.Flush();
        WaitForStdinToClose();
        return 0;
    }
    case "hold" when args.Length == 2:
    {
        var attempt = DataDirectoryLock.TryAcquire(args[1]);
        Console.Out.WriteLine($"LOCK {attempt.Status}");
        Console.Out.Flush();
        if (attempt.Lock is { } held)
        {
            WaitForStdinToClose();
            held.Dispose();
        }

        return 0;
    }
    case "enter" when args.Length == 2:
        return Enter(args[1]);
    case "launch" when args.Length == 1:
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(DefaultAppDataPathResolver.EnvVarName)))
        {
            Console.Error.WriteLine($"launch needs {DefaultAppDataPathResolver.EnvVarName}");
            return 2;
        }

        return Enter(AppDataLocator.ResolveEffectiveDataDirectory(),
            () => Console.Out.WriteLine($"APPJSON {AppOptionsManager.GetAppOptionsFilePath()}"));
    }
    default:
        return Usage();
}

static int Enter(string directory, Action? afterEntering = null)
{
    var entry = SingleInstanceGuard.Enter(directory);
    Console.Out.WriteLine($"ENTRY {entry}");
    Console.Out.Flush();
    if (entry == SingleInstanceEntry.Entered)
    {
        afterEntering?.Invoke();
        Console.Out.Flush();
        SingleInstanceGuard.SetActivationHandler(() =>
        {
            Console.Out.WriteLine("ACTIVATED");
            Console.Out.Flush();
        });
        WaitForStdinToClose();
        SingleInstanceGuard.ReleaseAll();
    }

    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("usage: (hold|enter) <data directory> | launch");
    return 2;
}

static void WaitForStdinToClose()
{
    while (Console.In.ReadLine() != null)
    {
    }
}
