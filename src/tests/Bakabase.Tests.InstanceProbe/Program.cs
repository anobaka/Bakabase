using Bakabase.Infrastructures.Components.App;
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
