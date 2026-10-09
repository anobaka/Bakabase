using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Service.Components;
using Bakabase.Service.Components.Federation;
using Bakabase.Service.Components.ServerData;
using Bakabase.Infrastructures.Components.App;
using DotNetEnv;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service;

public class Program
{
    public static async Task<int> Main(string[] args)
    {
        Env.Load();
        AppDataAnchor.Use(new AppDataPathProfile("BAKABASE_DATA_DIR", "Bakabase.Server", "Bakabase.Server.AppData"));
        if (args.FirstOrDefault() == FederationCli.Command) return await FederationCli.RunAsync(args);
        try
        {
            await using var child = await SetupChildConnection.ConnectAsync(args);
            if (child?.Role == "worker") return await SetupMaintenanceWorker.RunAsync(child);
            if (child?.Role == "business") return await RunBusiness(args, child);
            using var stopped = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stopped.Cancel(); };
            Console.CancelKeyPress += cancel;
            using var sigterm = OperatingSystem.IsWindows() ? null : PosixSignalRegistration.Create(PosixSignal.SIGTERM,
                context => { context.Cancel = true; stopped.Cancel(); });
            try
            {
                return await SetupProcessCoordinator.RunAsync(new SetupCoordinatorOptions
                {
                    Arguments = args,
                    Addresses = BakabaseHost.ParseServerListeningPorts(Environment.GetEnvironmentVariable("API_LISTENING_PORTS"))
                        .Select(p => $"http://{BakabaseHost.ServerListeningInterface()}:{p}").ToArray()
                }, stopped.Token);
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        catch (Exception error) { Console.Error.WriteLine($"Setup failed: {error}"); return 1; }
    }

    private static async Task<int> RunBusiness(string[] args, SetupChildConnection child)
    {
        var anchor = AppDataLocator.ResolveAnchor();
        using var anchorLock = SetupProcessCoordinator.OwnData(anchor);
        var data = AppDataLocator.ResolveEffectiveDataDirectory(anchor);
        using var dataLock = ServerSetupSession.SameDirectory(anchor, data) ? null : SetupProcessCoordinator.OwnData(data);
        if (ServerAppDataImport.ReadPending(data) != null || ServerAppDataRelocation.ReadPending(anchor) != null ||
            ServerSetupSession.RequiresSetup(anchor, data)) throw new IOException("The business process cannot start before Setup completes.");
        using var monitoring = new ImportProgressStore(data);
        ImportProgressStore.Current = monitoring;
        if (monitoring.Applied && monitoring.Read()?.Phase is "starting" or "failed") monitoring.Starting();
        var gui = new NullGuiAdapter(onReady: () => child.Ready($"http://{BakabaseHost.ServerListeningInterface()}:{BakabaseHost.ParseServerListeningPorts(Environment.GetEnvironmentVariable("API_LISTENING_PORTS"))[0]}"),
            onFatalError: error => child.Fatal(error));
        var host = new BakabaseHost(gui, new NullSystemService());
        using var stop = child.Stopping.Register(() => host.Host?.Services.GetService<IHostApplicationLifetime>()?.StopApplication());
        try
        {
            var started = await host.Start(args.Where(a => !a.StartsWith(SetupChildConnection.RoleArgument, StringComparison.Ordinal) && a != "--federation-invite-on-start").ToArray());
            return started ? 0 : 1;
        }
        finally { host.Dispose(); ImportProgressStore.Current = null; }
    }
}
