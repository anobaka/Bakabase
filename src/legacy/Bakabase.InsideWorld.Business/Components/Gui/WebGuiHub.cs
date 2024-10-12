using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Extensions;
using Bakabase.InsideWorld.Business.Components.Dependency.Models.Dto;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Components.FileMover.Models;
using Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Business.Configurations;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Humanizer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.InsideWorld.Business.Components.Gui
{
    public interface IWebGuiClient
    {
        Task GetData(string key, object data);
        Task GetIncrementalData(string key, object data);
        Task OptionsChanged(string optionsName, object options);
        Task GetResponse(BaseResponse rsp);
        Task GetIwFsEntryTask(string path, IwFsTaskInfo task);
        Task GetResourceTask(int id, ResourceTaskInfo? task);
        Task IwFsEntriesChange(List<IwFsEntryChangeEvent> events, CancellationToken ct);
        Task GetAppUpdaterState(UpdaterState state);
    }

    public class WebGuiHub : Hub<IWebGuiClient>
    {
        private readonly BackgroundTaskManager _backgroundTaskManager;
        private readonly IwFsEntryTaskManager _iwFsEntryTaskManager;
        private readonly ResourceTaskManager _resourceTaskManager;
        private readonly DownloadTaskService _downloadTaskService;
        private readonly InsideWorldOptionsManagerPool _optionsManagerPool;
        private readonly ILogger<WebGuiHub> _logger;
        private readonly IEnumerable<IDependentComponentService> _dependentComponentServices;
        private readonly IFileMover _fileMover;
        private readonly AppUpdater _appUpdater;
        private readonly AppContext _appContext;

        public WebGuiHub(
            BackgroundTaskManager backgroundTaskManager, IwFsEntryTaskManager iwFsEntryTaskManager,
            ResourceTaskManager resourceTaskManager, DownloadTaskService downloadTaskService,
            InsideWorldOptionsManagerPool optionsManagerPool, ILogger<WebGuiHub> logger,
            IEnumerable<IDependentComponentService> dependentComponentServices, IFileMover fileMover,
            AppUpdater appUpdater, AppContext appContext)
        {
            _backgroundTaskManager = backgroundTaskManager;
            _iwFsEntryTaskManager = iwFsEntryTaskManager;
            _resourceTaskManager = resourceTaskManager;
            _downloadTaskService = downloadTaskService;
            _optionsManagerPool = optionsManagerPool;
            _logger = logger;
            _dependentComponentServices = dependentComponentServices;
            _fileMover = fileMover;
            _appUpdater = appUpdater;
            _appContext = appContext;
        }

        public async Task GetInitialData()
        {
            await Clients.Caller.GetData(nameof(BackgroundTask), _backgroundTaskManager.Tasks);
            await Clients.Caller.GetData(nameof(DownloadTask), await _downloadTaskService.GetAllDto());

            foreach (var (optionsType, optionsManagerObj) in _optionsManagerPool.AllOptionsManagers)
            {
                var genericType = typeof(IOptions<>).MakeGenericType(optionsType);
                var valueGetter = genericType.GetProperties()
                    .FirstOrDefault(a => a.Name == nameof(IOptions<AppOptions>.Value));
                var options = valueGetter!.GetMethod!.Invoke(optionsManagerObj, null);
                await Clients.Caller.OptionsChanged(optionsType.Name.Camelize(), options);
            }

            var componentContexts = _dependentComponentServices.Select(a => a.BuildContextDto()).ToList();
            await Clients.Caller.GetData(nameof(DependentComponentContext), componentContexts);

            await Clients.Caller.GetData(nameof(FileMovingProgress), _fileMover.Progresses);

            await Clients.Caller.GetAppUpdaterState(_appUpdater.State);

            await Clients.Caller.GetData(nameof(BulkModificationConfiguration),
                BulkModificationService.GetConfiguration());

            await Clients.Caller.GetData(nameof(AppContext), _appContext);
        }
    }
}