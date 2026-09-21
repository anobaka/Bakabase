using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Domain.Options;
using Bakabase.Infrastructures.Components.App;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.Modules.Federation.Peers;
using Bakabase.Modules.Federation.Identity;
using Bakabase.Modules.RemoteAccess.Abstractions.Services;
using Bakabase.Service.Components;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.FileProviders;

// Each process owns its static AppService, independent SQLite database, options, keys and HTTP port.
// This executable is never packaged. It uses the production startup, routes, middleware and adapters.
if (args.Length < 2 || !int.TryParse(args[0], out var port) || !Path.IsPathFullyQualified(args[1]))
    throw new ArgumentException("Usage: Bakabase.Federation.TestHost <port> <absolute-empty-data-directory> [resource-count]");
var dataDirectory = args[1];
Environment.SetEnvironmentVariable("BAKABASE_FEDERATION_TEST_DATA_DIR", dataDirectory);
Environment.SetEnvironmentVariable("Analytics__Sentry__BackendDsn", "");
AppDataAnchor.Use(new AppDataPathProfile("BAKABASE_FEDERATION_TEST_DATA_DIR", "Bakabase.Federation.Test", "Bakabase.Federation.Test"));
var count = args.Length > 2 ? int.Parse(args[2]) : 257;
var host = new FederationTestHost(port, dataDirectory, count);
await host.Start([]);

sealed class FederationTestHost(int port, string dataDirectory, int count)
    : BakabaseHost(new NullGuiAdapter(), new NullSystemService())
{
    protected override string? SingleInstanceId => null;
    protected override IReadOnlyList<int>? OverrideListeningPorts() => [port];
    protected override string ListeningInterface => "127.0.0.1";

    protected override IHostBuilder CreateHostBuilder(params string[] args) => base.CreateHostBuilder(args)
        .ConfigureServices(services =>
        {
            // The fixture exercises media serving, never dependency installation or external downloads.
            services.RemoveAll<IDependentComponentService>();
            services.AddSingleton<IStartupFilter, FederationTestStaticFiles>();
        });

    protected override async Task ExecuteCustomProgress(IServiceProvider services)
    {
        await base.ExecuteCustomProgress(services);
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BakabaseDbContext>();
        if (!await db.ResourcesV2.AnyAsync())
        {
            var fixtureMedia = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_MEDIA_FILE");
            if (fixtureMedia != null && (!Path.IsPathFullyQualified(fixtureMedia) || !File.Exists(fixtureMedia)))
                throw new ArgumentException("The optional test media must be an existing absolute file path.");
            var mediaPath = Path.Combine(dataDirectory, "fixture" +
                (fixtureMedia == null ? ".wav" : Path.GetExtension(fixtureMedia)));
            if (fixtureMedia != null)
                File.Copy(fixtureMedia, mediaPath, overwrite: false);
            // A valid PCM wave with predictable bytes exercises browser audio and HTTP Range.
            else using (var output = new BinaryWriter(File.Create(mediaPath)))
            {
                var payload = 16000;
                output.Write("RIFF"u8); output.Write(payload + 36); output.Write("WAVEfmt "u8);
                output.Write(16); output.Write((short)1); output.Write((short)1); output.Write(8000);
                output.Write(16000); output.Write((short)2); output.Write((short)16);
                output.Write("data"u8); output.Write(payload); output.Write(new byte[payload]);
            }
            for (var id = 1; id <= count; id++)
            {
                db.ResourcesV2.Add(new ResourceDbModel
                {
                    Id = id, Path = id == 1 ? mediaPath : null, IsFile = id == 1,
                    Status = ResourceStatus.Active
                });
                db.ReservedPropertyValues.Add(new ReservedPropertyValue
                {
                    ResourceId = id, Scope = (int)PropertyValueScope.Manual,
                    Name = id == 1 ? "Shared title" : $"Title {id % 29:D2}"
                });
            }
            await db.SaveChangesAsync();
        }
        var node = await services.GetRequiredService<INodeIdentityProvider>().GetAsync();
        await services.GetRequiredService<IBOptionsManager<RemoteAccessOptions>>().SaveAsync(options =>
        {
            options.ServerId = node.NodeId;
            options.Mode = RemoteAccessMode.Enabled;
            options.RequirePairing = true;
        });
        await services.GetRequiredService<FederationPeerService>().SetSharingAsync(true);
        services.GetRequiredService<AppService>().NotAcceptTerms = false;
        File.WriteAllText(Path.Combine(dataDirectory, "ready"), port.ToString());
        Console.WriteLine($"FEDERATION_TEST_READY {port}");
    }
}

public sealed class FederationTestStaticFiles : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        var webRoot = Environment.GetEnvironmentVariable("BAKABASE_FEDERATION_TEST_WEB_ROOT");
        if (!string.IsNullOrEmpty(webRoot))
        {
            var files = new PhysicalFileProvider(Path.GetFullPath(webRoot));
            app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
            app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        }
        next(app);
    };
}
