using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Http;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Logging;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/dashboard")]
    public class DashboardController : Controller
    {
        private readonly ResourceService _resourceService;
        private readonly DownloadTaskService _downloadTaskService;
        private readonly ResourceTagMappingService _resourceTagMappingService;
        private readonly TagService _tagService;
        private readonly TagGroupService _tagGroupService;
        private readonly ThirdPartyHttpRequestLogger _thirdPartyHttpRequestLogger;
        private readonly ThirdPartyService _thirdPartyService;

        public DashboardController(ResourceService resourceService, DownloadTaskService downloadTaskService,
            ResourceTagMappingService resourceTagMappingService, TagService tagService, TagGroupService tagGroupService,
            ThirdPartyHttpRequestLogger thirdPartyHttpRequestLogger, ThirdPartyService thirdPartyService)
        {
            _resourceService = resourceService;
            _downloadTaskService = downloadTaskService;
            _resourceTagMappingService = resourceTagMappingService;
            _tagService = tagService;
            _tagGroupService = tagGroupService;
            _thirdPartyHttpRequestLogger = thirdPartyHttpRequestLogger;
            _thirdPartyService = thirdPartyService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetStatistics")]
        public async Task<SingletonResponse<DashboardStatistics>> GetStatistics()
        {
            var ds = new DashboardStatistics();

            await _resourceService.PopulateStatistics(ds);

            var allDownloadTasks = await _downloadTaskService.GetAllDto();
            ds.DownloadTaskStatusCounts = allDownloadTasks.GroupBy(a => a.Status).Select(a =>
                new DashboardStatistics.NameAndCount {Count = a.Count(), Name = a.Key.ToString()}).ToArray();

            var allTagMappings = await _resourceTagMappingService.GetAll(null, false);
            var top10TagMappings = allTagMappings.GroupBy(a => a.TagId).OrderByDescending(a => a.Count()).Take(10)
                .ToDictionary(a => a.Key, a => a.Count());
            var tagIds = top10TagMappings.Keys.ToArray();
            var tags = await _tagService.GetByKeys(tagIds, TagAdditionalItem.PreferredAlias);
            var groupIds = tags.Select(a => a.GroupId).ToHashSet().ToArray();
            var groups = await _tagGroupService.GetByKeys(groupIds, TagGroupAdditionalItem.PreferredAlias);
            var groupsMap = groups.ToDictionary(a => a.Id, a => a);
            foreach (var tag in tags)
            {
                if (groupsMap.TryGetValue(tag.GroupId, out var g))
                {
                    tag.GroupName = g.Name;
                    tag.GroupNamePreferredAlias = g.PreferredAlias;
                }
            }

            ds.Top10TagCounts =
                tags.Select(a => new DashboardStatistics.NameAndCount
                {
                    Name = $"{a.GroupNamePreferredAlias ?? a.GroupName}:{a.PreferredAlias ?? a.Name}",
                    Count = top10TagMappings.TryGetValue(a.Id, out var count) ? count : 0
                }).Where(a => a.Count > 0).OrderByDescending(a => a.Count).ToArray();

            // Third party
            ds.ThirdPartyRequestCounts = _thirdPartyService.GetAllThirdPartyRequestStatistics();

            // var rand = new Random(DateTime.Now.Millisecond);
            // ds.ThirdPartyRequestCounts = new[]
            //         {ReservedThirdPartyIds.Bilibili, ReservedThirdPartyIds.ExHentai, ReservedThirdPartyIds.Pixiv}
            //     .Select(a =>
            //     {
            //         var dict = new Dictionary<int, int>();
            //         foreach (var r in SpecificEnumUtils<ThirdPartyRequestResultType>.Values)
            //         {
            //             dict[(int) r] = rand.Next(1000);
            //         }
            //
            //         return new DashboardStatistics.ThirdPartyRequestCountsModel
            //         {
            //             Id = a,
            //             Counts = dict
            //         };
            //     }).ToArray();

            return new SingletonResponse<DashboardStatistics>(ds);
        }
    }
}