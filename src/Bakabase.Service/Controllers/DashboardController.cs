using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Components.ThirdParty.Services;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.Alias.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.ThirdParty.Abstractions.Http;
using Bootstrap.Components.Configuration.Abstractions;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/dashboard")]
    public class DashboardController : Controller
    {
        private readonly IResourceService _resourceService;
        private readonly ICategoryService _categoryService;
        private readonly DownloadTaskService _downloadTaskService;
        private readonly ThirdPartyHttpRequestLogger _thirdPartyHttpRequestLogger;
        private readonly IThirdPartyService _thirdPartyService;
        private readonly IBOptions<FileSystemOptions> _fsOptions;
        private readonly IAliasService _aliasService;
        private readonly ISpecialTextService _specialTextService;
        private readonly ComponentService _componentService;
        private readonly ComponentOptionsService _componentOptionsService;
        private readonly PasswordService _passwordService;
        private readonly ICustomPropertyService _customPropertyService;
        private readonly ICustomPropertyValueService _customPropertyValueService;
        private readonly IPropertyService _propertyService;

        public DashboardController(IResourceService resourceService, DownloadTaskService downloadTaskService,
            ThirdPartyHttpRequestLogger thirdPartyHttpRequestLogger, IThirdPartyService thirdPartyService,
            IBOptions<FileSystemOptions> fsOptions, IAliasService aliasService, ISpecialTextService specialTextService,
            ComponentService componentService, PasswordService passwordService,
            ComponentOptionsService componentOptionsService, ICategoryService categoryService,
            ICustomPropertyService customPropertyService, ICustomPropertyValueService customPropertyValueService,
            IPropertyService propertyService)
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
            _categoryService = categoryService;
            _customPropertyService = customPropertyService;
            _customPropertyValueService = customPropertyValueService;
            _propertyService = propertyService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetStatistics")]
        public async Task<SingletonResponse<DashboardStatistics>> GetStatistics()
        {
            var ds = new DashboardStatistics();

            // Resource
            var categories = (await _categoryService.GetAll()).ToDictionary(a => a.Id, a => a.Name);
            var allEntities = await _resourceService.GetAllDbModels();

            var totalCounts = allEntities.GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .ToList();

            ds.CategoryResourceCounts = totalCounts;

            var today = DateTime.Today;
            var todayCounts = allEntities.Where(a => a.CreateDt >= today).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .Where(a => a.Count > 0)
                .OrderByDescending(a => a.Count)
                .ToList();

            ds.TodayAddedCategoryResourceCounts = todayCounts;

            var weekdayDiff = (7 + (today.DayOfWeek - DayOfWeek.Monday)) % 7;
            var monday = today.AddDays(-1 * weekdayDiff);
            var thisWeekCounts = allEntities.Where(a => a.CreateDt >= monday).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .Where(a => a.Count > 0)
                .OrderByDescending(a => a.Count)
                .ToList();

            ds.ThisWeekAddedCategoryResourceCounts = thisWeekCounts;

            var thisMonth = today.GetFirstDayOfMonth();
            var thisMonthCounts = allEntities.Where(a => a.CreateDt >= thisMonth).GroupBy(a => a.CategoryId)
                .Select(a => new DashboardStatistics.TextAndCount(categories.GetValueOrDefault(a.Key), a.Count()))
                .Where(a => a.Count > 0)
                .OrderByDescending(a => a.Count)
                .ToList();

            ds.ThisMonthAddedCategoryResourceCounts = thisMonthCounts;

            // 12 weeks added counts trending
            {
                var total = allEntities.Count;
                for (var i = 0; i < 12; i++)
                {
                    var offset = -i * 7;
                    var weekStart = today.AddDays(offset - weekdayDiff);
                    var weekEnd = weekStart.AddDays(7);
                    var count = allEntities.Count(a => a.CreateDt >= weekStart && a.CreateDt < weekEnd);
                    ds.ResourceTrending.Add(new DashboardStatistics.WeekCount(-i, total));
                    total -= count;
                }

                ds.ResourceTrending.Reverse();
            }

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
                new("Saved passwords", await _passwordService.Count(null))
            });

            // Property value coverage
            var resources = await _resourceService.GetAll(null, ResourceAdditionalItem.All);
            var propertyValueExpectedCounts = new Dictionary<int, Dictionary<int, int>>();
            var propertyValueFilledCounts = new Dictionary<int, Dictionary<int, int>>();
            var propertyMap =
                (await _propertyService.GetProperties(PropertyPool.Reserved | PropertyPool.Custom)).ToMap();
            foreach (var r in resources.Where(x => x.Properties != null))
            {
                foreach (var (pt, pvs) in r.Properties!)
                {
                    var pp = (PropertyPool) pt;
                    if (pp is PropertyPool.Reserved or PropertyPool.Custom)
                    {
                        foreach (var (pId, pv) in pvs)
                        {
                            propertyValueExpectedCounts.GetOrAdd(pt, () => []).GetOrAdd(pId, () => 0);
                            propertyValueExpectedCounts[pt][pId]++;
                            if (pv.Values?.Any(x => x.Value != null) == true)
                            {
                                propertyValueFilledCounts.GetOrAdd(pt, () => []).GetOrAdd(pId, () => 0);
                                propertyValueFilledCounts[pt][pId]++;
                            }
                        }
                    }
                }
            }

            foreach (var (pt, pcs) in propertyValueExpectedCounts)
            {
                foreach (var (pId, expectedCount) in pcs)
                {
                    var property = propertyMap.GetProperty((PropertyPool) pt, pId);
                    if (property != null)
                    {
                        var filledCount = propertyValueFilledCounts.GetValueOrDefault(pt)?.GetValueOrDefault(pId) ?? 0;
                        ds.PropertyValueCoverages.Add(
                            new DashboardStatistics.PropertyValueCoverage(pt, pId, property.Name, filledCount,
                                expectedCount));
                    }
                }
            }


            ds.PropertyValueCoverages = ds.PropertyValueCoverages
                .OrderByDescending(x => (decimal) x.FilledCount / x.ExpectedCount).ToList();
            ds.TotalExpectedPropertyValueCount = ds.PropertyValueCoverages.Sum(x => x.ExpectedCount);
            ds.TotalFilledPropertyValueCount = ds.PropertyValueCoverages.Sum(x => x.FilledCount);

            return new SingletonResponse<DashboardStatistics>(ds);
        }
    }
}