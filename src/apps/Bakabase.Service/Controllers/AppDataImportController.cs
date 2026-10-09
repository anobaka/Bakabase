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

[Route("~/app/data-path/import")]
public class AppDataImportController(AppService appService, IHostApplicationLifetime lifetime) : Controller
{
    private string CurrentPath => appService.AppDataDirectory;

    public sealed class ImportRequest
    {
        public string SourcePath { get; set; } = "";
        public string? OriginalDataPath { get; set; }
    }

    [HttpGet]
    public SingletonResponse<object> Status() => new(BuildStatus());

    [HttpPost("setup-session")]
    public async Task<BaseResponse> CreateSetupSession()
    {
        if (SetupChildConnection.Current is { } child)
        {
            try { return new SingletonResponse<object>(JsonSerializer.Deserialize<Dictionary<string, string>>(await child.CreateSetupSessionAsync("import"))); }
            catch (Exception e) when (e is IOException or InvalidOperationException or TimeoutException)
            { return BaseResponseBuilder.BuildBadRequest(e.Message); }
        }
        return Execute(() =>
    {
        if (ServerAppDataImport.ReadPending(CurrentPath) != null ||
            ServerAppDataRelocation.ReadPending(AppDataLocator.ResolveAnchor()) != null ||
            ImportProgressStore.Current?.Read()?.Phase == "starting")
            throw new IOException("Finish or cancel the current import before configuring another one.");
        ServerSetupSession.Current = ServerSetupSession.ForImport(CurrentPath, ImportProgressStore.Current!, AppDataLocator.ResolveAnchor());
    }, includeSetupSession: true);
    }

    private object BuildStatus()
    {
        if (SetupChildConnection.Current != null) ImportProgressStore.Current?.RefreshFromDisk(CurrentPath);
        var pending = ServerAppDataImport.ReadPending(CurrentPath);
        var placement = ServerAppDataRelocation.ReadPending(AppDataLocator.ResolveAnchor());
        if (placement?.ImportSourcePath == null) placement = null;
        var monitoring = ImportProgressStore.Current;
        if (SetupChildConnection.Current == null && pending != null) monitoring?.EnsureQueued(pending);
        if (SetupChildConnection.Current == null && placement != null) monitoring?.EnsureRelocation(placement);
        var progress = monitoring?.Read();
        if (progress?.Operation != "import") progress = null;
        return new
        {
            supported = true, sourcePath = pending?.SourcePath ?? placement?.ImportSourcePath,
            originalDataPath = pending?.OriginalDataPath ?? placement?.OriginalDataPath, targetPath = placement?.TargetPath,
            currentPath = CurrentPath, progress, monitorToken = progress == null ? null : monitoring?.Token,
            automaticMaintenance = SetupChildConnection.Current != null, monitorUrl = SetupChildConnection.Current?.MonitorUrl
        };
    }

    [HttpPost("validate")]
    public SingletonResponse<ServerAppDataImport.Validation> Validate([FromBody] ImportRequest request) => new(
        ServerAppDataImport.Validate(request.SourcePath, CurrentPath, request.OriginalDataPath));

    [HttpPost]
    public BaseResponse Queue([FromBody] ImportRequest request) => Execute(() =>
    {
        if (SetupChildConnection.Current != null) throw new InvalidOperationException("Use the Setup application to plan and apply an import.");
        using var session = ServerSetupSession.ForImport(CurrentPath, ImportProgressStore.Current!, AppDataLocator.ResolveAnchor());
        session.Commit(new ServerSetupSession.SetupRequest
        { SourcePath = request.SourcePath, OriginalDataPath = request.OriginalDataPath }, session.Token);
        if (SetupChildConnection.Current is { } child)
            Response.OnCompleted(() => { child.RequestMaintenance(); return Task.CompletedTask; });
    }, includeStatus: true);

    [HttpDelete]
    public BaseResponse Cancel() => Execute(() =>
    {
        if (SetupChildConnection.Current != null) throw new InvalidOperationException("Setup is applying this plan automatically. Stop Setup before changing a saved operation.");
        if (ServerAppDataImport.ReadPending(CurrentPath) != null) ServerAppDataImport.Cancel(CurrentPath);
        else if (ServerAppDataRelocation.ReadPending(AppDataLocator.ResolveAnchor())?.ImportSourcePath != null)
            ServerAppDataRelocation.Cancel(AppDataLocator.ResolveAnchor());
        else return;
        ImportProgressStore.Current?.Cancel();
    });

    private BaseResponse Execute(Action action, bool includeStatus = false, bool includeSetupSession = false)
    {
        try
        {
            lock (ServerSetupSession.OperationGate)
            {
                // Already initialized desktop instances do not need a monitor until
                // the user opens a maintenance workflow.
                if (ImportProgressStore.Current == null)
                {
                    var store = new ImportProgressStore(CurrentPath);
                    lifetime.ApplicationStopped.Register(store.Dispose);
                    ImportProgressStore.Current = store;
                }
                action();
                if (includeSetupSession) return new SingletonResponse<object>(new { setupToken = ServerSetupSession.Current!.Token });
                return includeStatus ? new SingletonResponse<object>(BuildStatus()) : BaseResponseBuilder.Ok;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            return BaseResponseBuilder.BuildBadRequest(e.Message);
        }
    }
}
