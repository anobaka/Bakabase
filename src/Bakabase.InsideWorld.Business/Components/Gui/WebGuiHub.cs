using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Extensions;
using Bakabase.InsideWorld.Business.Components.Dependency.Models.Dto;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
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

namespace Bakabase.InsideWorld.Business.Components.Gui
{
    public interface IWebGuiClient
    {
        Task GetData(string key, object data);
        Task GetIncrementalData(string key, object data);
        Task OptionsChanged(string optionsName, object options);
        Task GetResponse(BaseResponse rsp);
        Task GetIwFsEntryTask(string path, IwFsTaskInfo task);
        Task GetResourceTask(int id, ResourceTaskInfo task);
        Task IwFsEntriesChange(List<IwFsEntryChangeEvent> events, CancellationToken ct);
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

        public WebGuiHub(
            BackgroundTaskManager backgroundTaskManager, IwFsEntryTaskManager iwFsEntryTaskManager,
            ResourceTaskManager resourceTaskManager, DownloadTaskService downloadTaskService,
            InsideWorldOptionsManagerPool optionsManagerPool, ILogger<WebGuiHub> logger,
            IEnumerable<IDependentComponentService> dependentComponentServices)
        {
            _backgroundTaskManager = backgroundTaskManager;
            _iwFsEntryTaskManager = iwFsEntryTaskManager;
            _resourceTaskManager = resourceTaskManager;
            _downloadTaskService = downloadTaskService;
            _optionsManagerPool = optionsManagerPool;
            _logger = logger;
            _dependentComponentServices = dependentComponentServices;
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
        }
    }
}