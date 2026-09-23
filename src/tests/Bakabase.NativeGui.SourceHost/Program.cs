using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using Avalonia;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.Gui;
using Bakabase.Infrastructures.Components.SystemService;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Service.Components;
using Bakabase.Shell.Components;
using Microsoft.Extensions.FileProviders;
using ShellApp = Bakabase.Shell.App;

namespace Bakabase.NativeGui.SourceHost;

// This is a visibly named CI source fixture, not a second installed Bakabase.
// Only this entry point selects a private mutex and AppData anchor. Neither the
// production single-instance policy nor the federation same-origin gate changes.
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var options = FixtureOptions.Parse(args); // Hosted guard precedes disk/native initialization.
        using var shellDiagnostics = ShellFailureDiagnostics.Install(options, typeof(ShellApp).Assembly);
        Environment.SetEnvironmentVariable(FixtureOptions.DataVariable, options.DataDirectory);
        Environment.SetEnvironmentVariable("Analytics__Sentry__BackendDsn", "");
        Environment.SetEnvironmentVariable("Analytics__Sentry__ClientDsn", "");
        AppDataAnchor.Use(new AppDataPathProfile(FixtureOptions.DataVariable,
            "Bakabase.NativeGui.SourceHost", "Bakabase.NativeGui.SourceHost"));
        _ = AppService.DefaultAppDataDirectory;
        AppBuilder.Configure(() => new ShellApp((gui, system) =>
                new SourceShellHost(new SourceHost(gui, system, options))))
            .UsePlatformDetect().LogToTrace().StartWithClassicDesktopLifetime([]);
    }
}

internal sealed record FixtureOptions(string DataDirectory, string WebRoot, int Port, string Token)
{
    internal const string DataVariable = "BAKABASE_NATIVE_GUI_SOURCE_DATA_DIR";
    internal string InstanceId => "Bakabase.NativeGui.SourceHost." + Token;

    internal static FixtureOptions Parse(string[] args)
    {
        if (Environment.GetEnvironmentVariable("GITHUB_ACTIONS") != "true" ||
            Environment.GetEnvironmentVariable("RUNNER_ENVIRONMENT") != "github-hosted" ||
            !(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()))
            throw new InvalidOperationException("Source fixture requires a disposable native GitHub-hosted runner.");
        var temporary = Environment.GetEnvironmentVariable("RUNNER_TEMP");
        if (string.IsNullOrEmpty(temporary) || !Path.IsPathFullyQualified(temporary))
            throw new ArgumentException("RUNNER_TEMP must be absolute.");
        if (args.Length != 10 || args[0] != "--data-directory" || args[2] != "--web-root" ||
            args[4] != "--port" || args[6] != "--run-token" || args[8] != "--rid")
            throw new ArgumentException("Expected the exact source-fixture argument contract.");
        var rid = OperatingSystem.IsWindows() && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "win-x64" :
            OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.X64 ? "osx-x64" :
            OperatingSystem.IsMacOS() && RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "osx-arm64" : null;
        if (rid == null || args[9] != rid || !int.TryParse(args[5], out var port) || port is < 1024 or > 65535 ||
            !Regex.IsMatch(args[7], "\\A[0-9a-f]{32}\\z") ||
            !Path.IsPathFullyQualified(args[1]) || !Path.IsPathFullyQualified(args[3]))
            throw new ArgumentException("Invalid native source fixture parameters.");
        var data = Path.GetFullPath(args[1]);
        var web = Path.GetFullPath(args[3]);
        var relative = Path.GetRelativePath(Path.GetFullPath(temporary), data);
        if (relative == "." || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar) ||
            Path.IsPathFullyQualified(relative) || !Directory.Exists(data) || !File.Exists(Path.Combine(web, "index.html")))
            throw new ArgumentException("Source data must be an existing private RUNNER_TEMP directory with an audited web root.");
        foreach (var path in new[] {data, web})
            for (var current = new DirectoryInfo(path); current != null; current = current.Parent)
                if ((current.Attributes & FileAttributes.ReparsePoint) != 0)
                    throw new ArgumentException("Source paths must not traverse links.");
        var root = Directory.GetParent(data)?.FullName ?? throw new ArgumentException("Source root missing.");
        var proof = new FileInfo(Path.Combine(root, "source-owner.json"));
        if (!proof.Exists || proof.Length > 4096 || (proof.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new ArgumentException("Source fixture ownership proof is missing or invalid.");
        using var marker = JsonDocument.Parse(File.ReadAllText(proof.FullName));
        if (marker.RootElement.GetProperty("token").GetString() != args[7] ||
            marker.RootElement.GetProperty("scope").GetString() != "native-gui-source-fixture")
            throw new ArgumentException("Source fixture ownership proof differs.");
        return new FixtureOptions(data, web, port, args[7]);
    }
}

internal sealed class SourceHost(IGuiAdapter gui, ISystemService system, FixtureOptions options)
    : BakabaseHost(gui, system)
{
    protected override string? SingleInstanceId => options.InstanceId;
    protected override string DisplayName => "Bakabase native GUI source fixture";
    protected override string ListeningInterface => "127.0.0.1";
    protected override IReadOnlyList<int>? OverrideListeningPorts() => [options.Port];
    // Also makes Debug builds use the audited same-origin web root, never port 3000.
    protected override string OverrideFeAddress(string _) => $"http://127.0.0.1:{options.Port}";
    protected override IHostBuilder CreateHostBuilder(params string[] args) => base.CreateHostBuilder(args)
        .ConfigureServices(services => services.AddSingleton<IStartupFilter>(new SourceStaticFiles(options.WebRoot)));

    protected override async Task ExecuteCustomProgress(IServiceProvider services)
    {
        await base.ExecuteCustomProgress(services);
        // Do not enable sharing/browsing, create grants, or accept terms here.
        // Resource seeding is a separately recorded production-API fixture step.
        var identity = await services.GetRequiredService<INodeIdentityProvider>().GetAsync();
        var marker = new {pid = Environment.ProcessId, options.Port, dataDirectory = options.DataDirectory,
            privateInstanceId = options.InstanceId, identity.NodeId, identity.LibraryEpoch};
        var target = Path.Combine(options.DataDirectory, "native-source-ready.json");
        await File.WriteAllTextAsync(target, JsonSerializer.Serialize(marker));
    }
}

internal sealed class SourceShellHost(SourceHost inner) : IShellHost
{
    public IHost? Host => inner.Host;
    public Task<bool> Start(string[] args) => inner.Start(args);
    public void Dispose() => inner.Dispose();
}

internal sealed class SourceStaticFiles(string directory) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        var provider = new PhysicalFileProvider(directory);
        app.UseDefaultFiles(new DefaultFilesOptions {FileProvider = provider});
        app.UseStaticFiles(new StaticFileOptions {FileProvider = provider});
        next(app);
    };
}
