using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components.Legacy.Services;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Http;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Logging;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/dashboard")]
    public class DashboardController : Controller
    {
        private readonly IResourceService _resourceService;
        private readonly DownloadTaskService _downloadTaskService;
        private readonly ThirdPartyHttpRequestLogger _thirdPartyHttpRequestLogger;
        private readonly IThirdPartyService _thirdPartyService;
        private readonly IBOptions<FileSystemOptions> _fsOptions;
        private readonly IAliasService _aliasService;
        private readonly ISpecialTextService _specialTextService;
        private readonly ComponentService _componentService;
        private readonly ComponentOptionsService _componentOptionsService;
        private readonly PasswordService _passwordService;

        public DashboardController(IResourceService resourceService, DownloadTaskService downloadTaskService,
            ThirdPartyHttpRequestLogger thirdPartyHttpRequestLogger, IThirdPartyService thirdPartyService,
            IBOptions<FileSystemOptions> fsOptions, IAliasService aliasService, ISpecialTextService specialTextService,
            ComponentService componentService, PasswordService passwordService, ComponentOptionsService componentOptionsService)
        {
            _resourceService = resourceService;
            _downloadTaskService = downloadTaskService;
            _thirdPartyHttpRequestLogger = thirdPartyHttpRequestLogger;
            _thirdPartyService = thirdPartyService;
            _fsOptions = fsOptions;
            _aliasService = aliasService;
            _specialTextService = specialTextService;
            _componentService = componentService;
            _passwordService = passwordService;
            _componentOptionsService = componentOptionsService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetStatistics")]
        public async Task<SingletonResponse<DashboardStatistics>> GetStatistics()
        {
            var ds = new DashboardStatistics();

            // Resource & Properties
            // await _resourceService.PopulateStatistics(ds);

            // Downloader
            var allDownloadTasks = await _downloadTaskService.GetAll();
            ds.DownloaderDataCounts = allDownloadTasks.GroupBy(a => a.ThirdPartyId).Select(a =>
                new DashboardStatistics.DownloaderTaskCount(a.Key,
                    a.GroupBy(b => b.Status).ToDictionary(b => (int) b.Key, b => b.Count()))).ToList();

            // Third party
            var requests = _thirdPartyService.GetAllThirdPartyRequestStatistics();
            ds.ThirdPartyRequestCounts = requests.SelectMany(a =>
                a.Counts.Select(b => new DashboardStatistics.ThirdPartyRequestCount(a.Id, b.Key, b.Value))).ToList();

            // File Mover
            var fileMoverTargets = _fsOptions.Value.FileMover?.Targets;
            if (fileMoverTargets != null)
            {
                ds.FileMover = new DashboardStatistics.FileMoverInfo(fileMoverTargets.Sum(t => t.Sources.Count),
                    fileMoverTargets.Count);
            }

            // Alias, Special Text
            var aliasCount = await _aliasService.Count();
            var stCount = await _specialTextService.Count();
            ds.OtherCounts.Add([
                new("Aliases", aliasCount),
                new("SpecialTexts", stCount)
            ]);
            // Players, PlayableFileSelectors, Enhancers
            var descriptors = await _componentOptionsService.GetAll();
            ds.OtherCounts.Add(descriptors.GroupBy(a => a.ComponentType)
                .Select(d => new DashboardStatistics.TextAndCount(d.Key.ToString(), d.Count())).ToList());
            // Passwords
            ds.OtherCounts.Add(new List<DashboardStatistics.TextAndCount>
            {
                new("Passwords", await _passwordService.Count(null))
            });

            return new SingletonResponse<DashboardStatistics>(ds);
        }
    }
}