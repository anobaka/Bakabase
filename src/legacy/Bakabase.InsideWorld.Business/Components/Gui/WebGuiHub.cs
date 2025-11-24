using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Infrastructures.Components.App.Upgrade;
using Bakabase.Infrastructures.Components.App.Upgrade.Abstractions;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Extensions;
using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.Downloader.Models.Db;
using Bakabase.InsideWorld.Business.Components.Downloader.Services;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Business.Components.FileExplorer.Information;
using Bakabase.InsideWorld.Business.Components.FileMover;
using Bakabase.InsideWorld.Business.Components.FileMover.Models;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.BulkModification.Components;
using Bakabase.Modules.ThirdParty.Services;
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
        Task DeleteData(string key, object id);
        Task DeleteAllData(string key);
        Task OptionsChanged(string optionsName, object options);
        Task GetResponse(BaseResponse rsp);
        Task IwFsEntriesChange(List<IwFsEntryChangeEvent> events, CancellationToken ct);
        Task GetAppUpdaterState(UpdaterState state);
        Task UpdateThirdPartyRequestStatistics(ThirdPartyRequestStatistics[] statistics);
        Task OnNotification(AppNotificationMessageViewModel notification);
    }

    public class WebGuiHub : Hub<IWebGuiClient>
    {
        private readonly DownloadTaskService _downloadTaskService;
        private readonly BakabaseOptionsManagerPool _optionsManagerPool;
        private readonly ILogger<WebGuiHub> _logger;
        private readonly IEnumerable<IDependentComponentService> _dependentComponentServices;
        private readonly IFileMover _fileMover;
        private readonly AppUpdater _appUpdater;
        private readonly AppContext _appContext;
        private readonly BTaskManager _bTaskManager;
        private readonly IPostParserTaskService _postParserTaskService;
        private readonly IThirdPartyService _thirdPartyService;

        public WebGuiHub(
            DownloadTaskService downloadTaskService,
            BakabaseOptionsManagerPool optionsManagerPool, ILogger<WebGuiHub> logger,
            IEnumerable<IDependentComponentService> dependentComponentServices, IFileMover fileMover,
            AppUpdater appUpdater, AppContext appContext, BTaskManager bTaskManager, IPostParserTaskService postParserTaskService, IThirdPartyService thirdPartyService)
        {
            _downloadTaskService = downloadTaskService;
            _optionsManagerPool = optionsManagerPool;
            _logger = logger;
            _dependentComponentServices = dependentComponentServices;
            _fileMover = fileMover;
            _appUpdater = appUpdater;
            _appContext = appContext;
            _bTaskManager = bTaskManager;
            _postParserTaskService = postParserTaskService;
            _thirdPartyService = thirdPartyService;
        }

        public async Task GetInitialData()
        {
            await Clients.Caller.GetData(nameof(DownloadTask), await _downloadTaskService.GetAllDto());

            foreach (var (optionsType, optionsManagerObj) in _optionsManagerPool.AllOptionsManagers)
            {
                var genericType = typeof(IOptions<>).MakeGenericType(optionsType);
                var valueGetter = genericType.GetProperties()
                    .FirstOrDefault(a => a.Name == nameof(IOptions<AppOptions>.Value));
                var options = valueGetter!.GetMethod!.Invoke(optionsManagerObj, null)!;
                await Clients.Caller.OptionsChanged(optionsType.Name.Camelize(), options);
            }

            var componentContexts = _dependentComponentServices.Select(a => a.BuildContextDto()).ToList();
            await Clients.Caller.GetData(nameof(DependentComponentContext), componentContexts);

            await Clients.Caller.GetData(nameof(FileMovingProgress), _fileMover.Progresses);

            await Clients.Caller.GetAppUpdaterState(_appUpdater.State);

            // await Clients.Caller.GetData(nameof(BulkModificationConfiguration),
                // BulkModificationService.GetConfiguration());

            await Clients.Caller.GetData(nameof(AppContext), _appContext);

            await Clients.Caller.GetData(nameof(BulkModificationInternals), new BulkModificationInternalsViewModel());

            await Clients.Caller.GetData("BTask", _bTaskManager.GetTasksViewModel());

            await Clients.Caller.GetData(nameof(PostParserTask), await _postParserTaskService.GetAll());
            
            await Clients.Caller.GetData(nameof(ThirdPartyRequestStatistics), _thirdPartyService.GetAllThirdPartyRequestStatistics());
        }
    }
}