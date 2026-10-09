using System;
using System.IO;
using System.Text.Json;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Service.Components.ServerData;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Controllers;

[Route("~/app/data-path/relocation")]
public sealed class AppDataRelocationController(AppService appService, IHostApplicationLifetime lifetime) : Controller
{
    private string Anchor => AppDataLocator.ResolveAnchor();
    private string CurrentPath => appService.AppDataDirectory;
    private bool Supported => !AppDataLocator.IsEnvironmentOverride && !ServerSetupSession.InContainer;

    [HttpGet]
    public SingletonResponse<object> Status()
    {
        if (SetupChildConnection.Current != null) ImportProgressStore.Current?.RefreshFromDisk(CurrentPath);
        var pending = ServerAppDataRelocation.ReadPending(Anchor);
        if (pending?.ImportSourcePath != null) pending = null;
        var monitor = ImportProgressStore.Current;
        var progress = monitor?.Read();
        if (progress?.Operation != "relocate") progress = null;
        return new(new { supported = Supported, currentPath = CurrentPath, targetPath = pending?.TargetPath,
            progress, monitorToken = progress == null ? null : monitor?.Token,
            automaticMaintenance = SetupChildConnection.Current != null, monitorUrl = SetupChildConnection.Current?.MonitorUrl });
    }

    [HttpPost("setup-session")]
    public async Task<BaseResponse> CreateSetupSession()
    {
        if (SetupChildConnection.Current is { } child)
        {
            try { return new SingletonResponse<object>(JsonSerializer.Deserialize<Dictionary<string, string>>(await child.CreateSetupSessionAsync("relocate"))); }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException)
            { return BaseResponseBuilder.BuildBadRequest(e.Message); }
        }
        return Execute(() =>
    {
        if (ServerAppDataRelocation.ReadPending(Anchor) != null ||
            ServerAppDataImport.ReadPending(CurrentPath) != null ||
            System.IO.File.Exists(Path.Combine(CurrentPath, ".pending_relocate")) ||
            ImportProgressStore.Current?.Read()?.Phase == "starting")
            throw new IOException("Finish or cancel the current operation before changing the data path.");
        if (ImportProgressStore.Current == null)
        {
            var store = new ImportProgressStore(CurrentPath);
            lifetime.ApplicationStopped.Register(store.Dispose);
            ImportProgressStore.Current = store;
        }
        ServerSetupSession.Current = ServerSetupSession.ForRelocation(Anchor, CurrentPath, ImportProgressStore.Current);
        return new SingletonResponse<object>(new { setupToken = ServerSetupSession.Current.Token });
    });
    }

    [HttpDelete]
    public BaseResponse Cancel() => Execute(() =>
    {
        if (SetupChildConnection.Current != null) throw new InvalidOperationException("Setup is applying this plan automatically. Stop Setup before changing a saved operation.");
        if (ServerAppDataRelocation.ReadPending(Anchor) != null)
        {
            ServerAppDataRelocation.Cancel(Anchor);
            ImportProgressStore.Current?.Cancel();
        }
        return BaseResponseBuilder.Ok;
    });

    private BaseResponse Execute(Func<BaseResponse> action)
    {
        if (!Supported) return BaseResponseBuilder.BuildBadRequest(
            "This data path is fixed by the deployment. Stop the server, move its AppData, and update the mount or BAKABASE_DATA_DIR.");
        try { lock (ServerSetupSession.OperationGate) return action(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        { return BaseResponseBuilder.BuildBadRequest(e.Message); }
    }
}
