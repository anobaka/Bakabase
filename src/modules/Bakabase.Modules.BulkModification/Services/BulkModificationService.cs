using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bakabase.Modules.BulkModification.Abstractions.Services;
using Bakabase.Modules.BulkModification.Components;
using Bakabase.Modules.BulkModification.Extensions;
using Bakabase.Modules.BulkModification.Models.Db;
using Bakabase.Modules.BulkModification.Models.Input;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Linq.Expressions;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.Property.Abstractions.Components;
using Bootstrap.Extensions;
using ResourceDiff = Bakabase.Abstractions.Models.Domain.ResourceDiff;

namespace Bakabase.Modules.BulkModification.Services
{
    public class BulkModificationService<TDbContext>(
        IServiceProvider serviceProvider,
        ResourceService<TDbContext, BulkModificationDiffDbModel, int> diffOrm,
        IBulkModificationLocalizer localizer,
        IPropertyLocalizer propertyLocalizer
        )
        : ResourceService<TDbContext, BulkModificationDbModel, int>(serviceProvider), IBulkModificationService
        where TDbContext : DbContext, IBulkModificationDbContext
    {
        protected IResourceService ResourceService => GetRequiredService<IResourceService>();
        protected IPropertyService PropertyService => GetRequiredService<IPropertyService>();

        public async Task<Abstractions.Models.BulkModification?> Get(int id)
        {
            var bm = await GetByKey(id);
            if (bm == null)
            {
                return null;
            }

            return await bm.ToDomainModel(GetRequiredService<IPropertyService>(),
                await diffOrm.Count(x => x.BulkModificationId == id));
        }

        public async Task Add(Abstractions.Models.BulkModification bm)
        {
            await Add(bm.ToDbModel());
        }

        public async Task Put(Abstractions.Models.BulkModification bm)
        {
            await Update(bm.ToDbModel());
        }

        public async Task Delete(int id)
        {
            await RemoveByKey(id);
        }

        public async Task Duplicate(int id)
        {
            var bm = await GetByKey(id);
            bm.Id = 0;
            bm.CreatedAt = DateTime.Now;
            await base.Add(bm);
        }

        public async Task Patch(int id, PatchBulkModification model)
        {
            var bm = await Get(id);
            if (bm != null)
            {
                bm.IsActive = model.IsActive ?? bm.IsActive;
                bm.Name = model.Name ?? bm.Name;
                bm.Filter = model.Filter ?? bm.Filter;
                bm.Variables = model.Variables ?? bm.Variables;
                bm.Processes = model.Processes ?? bm.Processes;
                await Put(bm);
            }
        }

        public async Task<List<Abstractions.Models.BulkModification>> GetAll()
        {
            var dbModels = await base.GetAll();
            var diffCounts = await DbContext.BulkModificationDiffs
                .GroupBy(d => d.BulkModificationId)
                .Select(x => new {BmId = x.Key, DiffsCount = x.Count()})
                .ToListAsync();
            var domainModels = await dbModels.ToDomainModels(GetRequiredService<IPropertyService>(),
                diffCounts.ToDictionary(d => d.BmId, d => d.DiffsCount));
            return domainModels.OrderByDescending(x => x.Id).ToList();
        }

        public async Task<int[]> Filter(int id)
        {
            var bm = await Get(id);
            if (bm != null)
            {
                var filter = bm.Filter;
                var resources = await ResourceService.Search(new ResourceSearch
                {
                    Group = filter,
                    PageSize = int.MaxValue
                });
                var resourceIds = resources.Data?.Select(r => r.Id).ToList() ?? [];
                bm.FilteredResourceIds = resourceIds;
                await Put(bm);
                return resourceIds.ToArray();
            }

            return [];
        }

        public async Task Preview(int id)
        {
            var bm = await Get(id);
            if (bm != null)
            {
                bm.FilteredResourceIds ??= (await Filter(id)).ToList();
                var allDiffs = new List<BulkModificationDiff>();
                if (bm.FilteredResourceIds!.Any() && bm.Processes?.Any() == true)
                {
                    var resources =
                        await ResourceService.GetByKeys(bm.FilteredResourceIds.ToArray(), ResourceAdditionalItem.All);

                    var propertyMap = (await PropertyService.GetProperties(PropertyPool.All)).ToMap();

                    foreach (var resource in resources)
                    {
                        var variableMap = new Dictionary<string, (StandardValueType Type, object? Value)>();
                        if (bm.Variables?.Any() == true)
                        {
                            foreach (var v in bm.Variables)
                            {
                                var rp = resource.Properties?.GetValueOrDefault((int) v.PropertyPool)
                                    ?.GetValueOrDefault(v.PropertyId);
                                var variableValue = rp?.Values?.FirstOrDefault(x => x.Scope == (int) v.Scope)?.BizValue;
                                if (v.Preprocesses?.Any() == true)
                                {
                                    foreach (var preprocess in v.Preprocesses)
                                    {
                                        var processor =
                                            BulkModificationInternals.ProcessorMap[v.Property.Type.GetBizValueType()];
                                        var options =
                                            preprocess.Options?.ConvertToProcessorOptions(variableMap, propertyMap,
                                                localizer);
                                        variableValue = processor.Process(variableValue, preprocess.Operation, options);
                                    }
                                }

                                variableMap[v.Key] = (v.Property.Type.GetBizValueType(), variableValue);
                            }
                        }

                        var copyProperties = resource.Properties?.Copy() ?? [];

                        foreach (var process in bm.Processes)
                        {
                            var processor =
                                BulkModificationInternals.ProcessorMap[process.Property.Type.GetBizValueType()];

                            var currentValue = resource.Properties?.GetPropertyBizValue(process.PropertyPool,
                                process.PropertyId, PropertyValueScope.Manual)?.Value;

                            var newValue = currentValue;
                            if (process.Steps != null)
                            {
                                foreach (var step in process.Steps)
                                {
                                    var options =
                                        step.Options?.ConvertToProcessorOptions(variableMap, propertyMap, localizer);
                                    newValue = processor.Process(newValue, step.Operation, options);
                                }
                            }

                            copyProperties.SetPropertyBizValue(process.Property, newValue, PropertyValueScope.Manual);
                        }

                        var diffs = resource.Properties.Compare(copyProperties);

                        if (diffs.Any())
                        {
                            allDiffs.Add(new BulkModificationDiff
                            {
                                ResourceId = resource.Id,
                                Diffs = diffs,
                                BulkModificationId = bm.Id,
                                ResourcePath = resource.Path
                            });
                        }
                    }
                }

                await diffOrm.RemoveAll(x => x.BulkModificationId == bm.Id);
                await diffOrm.AddRange(allDiffs.Select(d => d.ToDbModel()));
            }
        }

        public async Task Apply(int id)
        {
            await ApplyOrRevert(id, true);
        }

        public async Task Revert(int id)
        {
            await ApplyOrRevert(id, false);
        }

        public async Task<SearchResponse<BulkModificationDiff>> SearchDiffs(int bmId,
            BulkModificationResourceDiffsSearchInputModel model)
        {
            Expression<Func<BulkModificationDiffDbModel, bool>> exp = x => x.BulkModificationId == bmId;
            if (model.Path.IsNotEmpty())
            {
                exp = exp.And(x => x.ResourcePath.Contains(model.Path));
            }

            var result = await diffOrm.Search(exp, model.PageIndex, model.PageSize);

            var data = result.Data ?? [];

            var domainModels = await data.ToDomainModels(PropertyService);

            return model.BuildResponse(domainModels, result.TotalCount);
        }

        private async Task ApplyOrRevert(int id, bool apply)
        {
            var bm = await Get(id);
            if (bm == null)
            {
                return;
            }

            if (!bm.IsActive)
            {
                throw new Exception("Can't operate on a inactive bulk modification.");
            }

            if (!apply && !bm.AppliedAt.HasValue)
            {
                throw new Exception("Can't operate on a bulk modification which is not applied yet.");
            }

            if (bm.ResourceDiffCount == 0)
            {
                throw new Exception("No resource diff is found, please calculate diffs first.");
            }

            var dbBmDiffs = await diffOrm.GetAll(x => x.BulkModificationId == id);
            var bmDiffs = await dbBmDiffs.ToDomainModels(PropertyService);
            var resourceIds = dbBmDiffs.Select(r => r.ResourceId).ToList();
            var resourcesMap =
                (await ResourceService.GetByKeys(resourceIds.ToArray(), ResourceAdditionalItem.All)).ToDictionary(
                    d => d.Id, d => d);
            var propertyMap = await bmDiffs.PreparePropertyMap(PropertyService, true);

            if (bmDiffs.Count != resourcesMap.Count)
            {
                throw new Exception($"Some of resources do not exist anymore, please perform a preview again.");
            }

            foreach (var bmDiff in bmDiffs)
            {
                var resource = resourcesMap[bmDiff.ResourceId];
                if (bmDiff.Diffs.Any())
                {
                    resource.Properties ??= [];
                    foreach (var rDiff in bmDiff.Diffs)
                    {
                        var property = propertyMap[rDiff.PropertyPool][rDiff.PropertyId];
                        var currentValue = resource.Properties.GetPropertyBizValue(rDiff.PropertyPool, rDiff.PropertyId,
                            PropertyValueScope.Manual)?.Value;

                        var expectedValue = apply ? rDiff.Value1 : rDiff.Value2;

                        var stdHandler = StandardValueInternals.HandlerMap[property.Type.GetBizValueType()];
                        if (!stdHandler.Compare(currentValue, expectedValue))
                        {
                            throw new Exception(
                                $"Validation failed: Current value [{currentValue?.SerializeAsStandardValue(property.Type.GetBizValueType())}] of property {property.Name} of resource {resource.Path} does not match the expected value [{expectedValue?.SerializeAsStandardValue(property.Type.GetBizValueType())}]. Please perform a preview again.");
                        }

                        var targetValue = apply ? rDiff.Value2 : rDiff.Value1;

                        resource.Properties.SetPropertyBizValue(property, targetValue, PropertyValueScope.Manual);
                    }
                }
            }

            await ResourceService.AddOrPutRange(resourcesMap.Values.ToList());

            if (apply)
            {
                bm.AppliedAt = DateTime.Now;
            }
            else
            {
                bm.AppliedAt = null;
            }

            await Put(bm);
        }
    }
}