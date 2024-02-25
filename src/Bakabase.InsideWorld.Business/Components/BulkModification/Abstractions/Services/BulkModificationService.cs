using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.Caching;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Dtos;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.BmVolumeProcessor;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Orm;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using MathNet.Numerics;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Services
{
    public class BulkModificationService : ResourceService<InsideWorldDbContext, Models.BulkModification, int>
    {
        protected ResourceService ResourceService => GetRequiredService<ResourceService>();

        protected BulkModificationDiffService BulkModificationDiffService =>
            GetRequiredService<BulkModificationDiffService>();

        protected BulkModificationTempDataService BulkModificationTempDataService =>
            GetRequiredService<BulkModificationTempDataService>();

        public BulkModificationService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public static BulkModificationConfiguration GetConfiguration()
        {
            var bmc = new BulkModificationConfiguration();
            foreach (BulkModificationProperty property in Enum.GetValues(typeof(BulkModificationProperty)))
            {
                var attr = SpecificTypeUtils<BulkModificationProperty>.Type.GetField(property.ToString())!
                    .GetCustomAttributes<BulkModificationPropertyFilterAttribute>(false).FirstOrDefault()!;
                var options = new BulkModificationConfiguration.PropertyOptions
                {
                    Property = property,
                    AvailableOperations = new List<BulkModificationFilterOperation>()
                };
                bmc.Properties.Add(options);
            }

            return bmc;
        }

        public async Task<List<BulkModificationDto>> GetAllDto()
        {
            var data = await GetAll();
            var tempData = (await BulkModificationTempDataService.GetAll()).ToDictionary(x => x.BulkModificationId, x => x);
            return data.Select(d => d.ToDto(tempData.GetValueOrDefault(d.Id))).ToList();
        }

        public async Task<BulkModificationDto> GetDto(int id)
        {
            var d = await GetByKey(id);
            var tempData = await BulkModificationTempDataService.GetByKey(id);
            return d.ToDto(tempData);
        }

        private ConcurrentDictionary<BulkModificationProperty, IBulkModificationProcessor> PrepareProcessors()
        {
            return new(
                new Dictionary<BulkModificationProperty, IBulkModificationProcessor>
                {
                    {BulkModificationProperty.Category, GetRequiredService<BmCategoryProcessor>()},
                    {BulkModificationProperty.MediaLibrary, GetRequiredService<BmMediaLibraryProcessor>()},
                    {BulkModificationProperty.ReleaseDt, GetRequiredService<BmReleaseDtProcessor>()},
                    {BulkModificationProperty.Publisher, GetRequiredService<BmPublishersProcessor>()},
                    {BulkModificationProperty.Name, GetRequiredService<BmNameProcessor>()},
                    {BulkModificationProperty.Language, GetRequiredService<BmLanguageProcessor>()},
                    {BulkModificationProperty.Volume, GetRequiredService<BmVolumeProcessor>()},
                    {BulkModificationProperty.Original, GetRequiredService<BmOriginalProcessor>()},
                    {BulkModificationProperty.Series, GetRequiredService<BmSeriesProcessor>()},
                    {BulkModificationProperty.Tag, GetRequiredService<BmTagProcessor>()},
                    {BulkModificationProperty.Introduction, GetRequiredService<BmIntroductionProcessor>()},
                    {BulkModificationProperty.Rate, GetRequiredService<BmRateProcessor>()},
                    {BulkModificationProperty.CustomProperty, GetRequiredService<BmCustomPropertiesProcessor>()},
                });
        }

        public async Task<List<int>> PerformFiltering(int id)
        {
            var bulkModification = await GetDto(id);
            var rootFilter = bulkModification.Filter;
            if (rootFilter != null)
            {
                var allResources = await ResourceService.GetAll(ResourceAdditionalItem.All);
                var exp = rootFilter.BuildExpression();
                var filteredResources = allResources.Where(exp.Compile()).ToList();
                var ids = filteredResources.Select(r => r.Id).ToList();
                await BulkModificationTempDataService.UpdateResourceIds(id, ids);
                return ids;
            }

            return new List<int>();
        }

        public async Task<List<(ResourceDto Current, ResourceDto New, List<BulkModificationDiff> Diffs)>>
            Preview(int id)
        {
            var bm = await GetDto(id);
            var tempData = await BulkModificationTempDataService.GetByKey(id);
            var resourceIds = tempData.GetResourceIds();
            var resources = await ResourceService.GetByKeys(resourceIds.ToArray());

            var processors = PrepareProcessors();
            var data = new List<(ResourceDto Current, ResourceDto New, List<BulkModificationDiff> Diffs)>();

            foreach (var resource in resources)
            {
                var variables = new Dictionary<string, string?>();
                foreach (var variable in bm.Variables)
                {
                    var sourceValue =
                        variable.Source switch
                        {
                            BulkModificationVariableSource.Name => resource.Name,
                            BulkModificationVariableSource.None => null,
                            BulkModificationVariableSource.FileName => resource.RawName,
                            BulkModificationVariableSource.FileNameWithoutExtension => Path.GetFileNameWithoutExtension(
                                resource.RawName),
                            BulkModificationVariableSource.FullPath => resource.RawFullname,
                            BulkModificationVariableSource.DirectoryName => resource.Directory,
                            _ => throw new ArgumentOutOfRangeException()
                        };

                    string? value;
                    if (!string.IsNullOrEmpty(variable.Find) && !string.IsNullOrEmpty(sourceValue))
                    {
                        value = Regex.Replace(sourceValue, variable.Find, variable.Value);
                    }
                    else
                    {
                        value = variable.Value;
                    }

                    if (!string.IsNullOrEmpty(value))
                    {
                        variables[variable.Key] = value;
                    }
                }

                var copy = resource.Clone();
                foreach (var process in bm.Processes)
                {
                    var processor = processors[process.Property];
                    try
                    {
                        await processor.Process(process, copy, variables);
                    }
                    catch (Exception e)
                    {
                        throw new Exception(
                            $"An error occurred during applying processor [{processor.GetType()}] on process {process}: {e.Message}",
                            e);
                    }
                }

                var diffs = resource.Compare(copy)
                    .Select(d => d.ToBulkModificationDiff(id, resource.Id, resource.RawFullname)).ToList();
                data.Add((resource, copy, diffs));
            }

            var allDiffs = data.SelectMany(d => d.Diffs).ToList();
            await BulkModificationDiffService.UpdateAll(id, allDiffs);

            return data;
        }

        public async Task Apply(int id)
        {
            var data = await Preview(id);
            var resources = data.Select(d => d.New).ToList();
            await ResourceService.AddOrUpdateRange(resources);
        }
    }
}