using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Xml.Linq;
using Velopack;
using Velopack.Locators;
using Velopack.Logging;

// Test-only .NET startup hook; never referenced or shipped by an application project.
// Keeps the real Program/AppUpdater/controllers while containing Velopack's default
// user-cache/log writes. The private setter is pinned to Velopack 1.2.0 and fails
// before application startup if that contract changes.
public static class StartupHook
{
    public static void Initialize()
    {
        var root = Path.GetFullPath(Environment.GetEnvironmentVariable("BAKABASE_UPGRADE_TEST_ROOT")
                                    ?? throw new InvalidOperationException("Missing isolated upgrade root."));
        if (!File.Exists(Path.Combine(root, ".bakabase-upgrade-test")))
            throw new InvalidOperationException("Not an owned upgrade fixture.");
        var app = Path.Combine(root, "installed", "Bakabase.app");
        var expectedExe = Path.Combine(app, "Contents", "MacOS", "Bakabase");
        var actualExe = System.Environment.ProcessPath;
        if (actualExe != expectedExe || Environment.GetEnvironmentVariable("BAKABASE_DATA_DIR") != Path.Combine(root, "appdata"))
            throw new InvalidOperationException("The hook may only run the owned application and AppData.");
        if (VelopackLocator.IsCurrentSet)
            throw new InvalidOperationException("Velopack was initialized before the isolation hook.");
        var locator = new IsolatedLocator(root, app);
        var setter = typeof(VelopackLocator).GetMethod("SetCurrentLocator", BindingFlags.Static | BindingFlags.NonPublic)
                     ?? throw new MissingMethodException("Velopack 1.2.0 locator setter was not found.");
        setter.Invoke(null, [locator]);
        File.AppendAllText(Path.Combine(root, "locator-events.jsonl"), JsonSerializer.Serialize(new
        {
            pid = Environment.ProcessId, executable = actualExe, app = locator.RootAppDir,
            packages = locator.PackagesDir, version = locator.CurrentlyInstalledVersion!.ToString(),
            appdata = Environment.GetEnvironmentVariable("BAKABASE_DATA_DIR")
        }) + "\n");
    }
}

sealed class IsolatedLocator : VelopackLocator
{
    private readonly IVelopackLogger _log;
    public IsolatedLocator(string root, string app)
    {
        RootAppDir = app;
        AppContentDir = Path.Combine(app, "Contents", "MacOS");
        UpdateExePath = Path.Combine(AppContentDir, "UpdateMac");
        var manifest = XDocument.Load(Path.Combine(AppContentDir, "sq.version"));
        string Value(string name) => manifest.Descendants().Single(x => x.Name.LocalName == name).Value;
        AppId = Value("id");
        if (AppId != "Bakabase" || Value("mainExe") != "Bakabase" || !File.Exists(UpdateExePath))
            throw new InvalidOperationException("Unexpected actual package identity.");
        CurrentlyInstalledVersion = SemanticVersion.Parse(Value("version"));
        Channel = Value("channel");
        PackagesDir = Path.Combine(root, "packages");
        Directory.CreateDirectory(PackagesDir);
        _log = new IsolatedLogger(Path.Combine(root, "velopack-managed.log"));
        Process = new IsolatedProcess(root, UpdateExePath, _log);
    }
    public override string AppId { get; }
    public override string RootAppDir { get; }
    public override string AppContentDir { get; }
    public override string UpdateExePath { get; }
    public override string PackagesDir { get; }
    public override string AppTempDir => Path.Combine(PackagesDir, "VelopackTemp");
    public override SemanticVersion CurrentlyInstalledVersion { get; }
    public override string Channel { get; }
    public override bool IsPortable => true;
    public override string ThisExeRelativePath => "Bakabase";
    public override IVelopackLogger Log => _log;
    public override IProcessImpl Process { get; }
}

sealed class IsolatedLogger(string path) : IVelopackLogger
{
    private readonly object _sync = new();
    public void Log(VelopackLogLevel level, string? message, Exception? exception)
    {
        lock (_sync)
            File.AppendAllText(path, $"{DateTimeOffset.UtcNow:O} {level} {message} {exception}\n");
    }
}

sealed class IsolatedProcess(string root, string updater, IVelopackLogger logger) : IProcessImpl
{
    private readonly DefaultProcessImpl _inner = new(logger);
    public string GetCurrentProcessPath() => _inner.GetCurrentProcessPath();
    public uint GetCurrentProcessId() => _inner.GetCurrentProcessId();
    public void Exit(int exitCode) => _inner.Exit(exitCode);
    public void StartProcess(string exePath, IEnumerable<string> arguments, string? workDir, bool showWindow)
    {
        var args = arguments.ToList();
        if (exePath != updater || !args.Contains("apply"))
            throw new InvalidOperationException("Only the owned UpdateMac apply command is permitted.");
        if (args.Contains("--") || args.Contains("--restart"))
            throw new InvalidOperationException("Unexpected explicit restart arguments in pinned Velopack apply contract.");
        // The actual updater replaces the app; the runner then starts that app with
        // its explicit isolated environment. LaunchServices restart is not tested.
        if (!args.Contains("--norestart")) args.Add("--norestart");
        if (!args.Contains("--silent")) args.Add("--silent");
        args.Add("--log");
        args.Add(Path.Combine(root, "velopack-native.log"));
        var info = new ProcessStartInfo(exePath) {UseShellExecute = false, WorkingDirectory = workDir ?? root};
        foreach (var arg in args) info.ArgumentList.Add(arg);
        var process = System.Diagnostics.Process.Start(info) ?? throw new InvalidOperationException("UpdateMac did not start.");
        File.AppendAllText(Path.Combine(root, "updater-processes.jsonl"),
            JsonSerializer.Serialize(new {pid = process.Id, executable = exePath, arguments = args}) + "\n");
    }
}
