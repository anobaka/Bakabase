using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Services;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using DotNext.Collections.Generic;
using Microsoft.Extensions.Logging;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Domain.ReservedPropertyValue;

namespace Bakabase.InsideWorld.Business.Components.ReservedProperty;

public class ReservedPropertyValueService(
    FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.ReservedPropertyValue, int> orm,
    IStandardValueService standardValueService,
    ILogger<ReservedPropertyValueService> logger)
    : IReservedPropertyValueService
{
    public async Task<ReservedPropertyValue?> GetFirst(
        Expression<Func<Abstractions.Models.Db.ReservedPropertyValue, bool>> selector)
    {
        return (await orm.GetFirstOrDefault(selector))?.ToDomainModel();
    }

    public async Task<List<ReservedPropertyValue>> GetAll(
        Expression<Func<Abstractions.Models.Db.ReservedPropertyValue, bool>>? selector = null, bool asNoTracking = true)
    {
        var data = await orm.GetAll(selector, asNoTracking);
        return data.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<SingletonResponse<ReservedPropertyValue>> Add(ReservedPropertyValue resource)
    {
        return new SingletonResponse<ReservedPropertyValue>((await orm.Add(resource.ToDbModel())).Data.ToDomainModel());
    }

    public async Task<ListResponse<ReservedPropertyValue>> AddRange(List<ReservedPropertyValue> resources)
    {
        var data = await orm.AddRange(resources.Select(x => x.ToDbModel()).ToList());
        return new ListResponse<ReservedPropertyValue>(data.Data.Select(d => d.ToDomainModel()));
    }

    public async Task<BaseResponse> Update(ReservedPropertyValue resource)
    {
        return await orm.Update(resource.ToDbModel());
    }

    public async Task<BaseResponse> UpdateRange(IReadOnlyCollection<ReservedPropertyValue> resources)
    {
        return await orm.UpdateRange(resources.Select(x => x.ToDbModel()).ToArray());
    }

    public async Task<BaseResponse> PutByResources(List<Abstractions.Models.Domain.Resource> resources)
    {
        var resourceIds = resources.Select(r => r.Id).ToList();
        // ResourceId - Scope - Data
        var dbData = (await GetAll(x => resourceIds.Contains(x.ResourceId))).GroupBy(d => d.ResourceId)
            .ToDictionary(d => d.Key, d => d.GroupBy(c => c.Scope).ToDictionary(e => e.Key, e => e.First()));

        var changed = new Dictionary<int, Dictionary<int, ReservedPropertyValue>>();

        foreach (var r in resources)
        {
            var reservedProperties = r.Properties?.GetValueOrDefault((int) PropertyPool.Reserved);
            if (reservedProperties == null)
            {
                continue;
            }

            foreach (var rp in SpecificEnumUtils<Abstractions.Models.Domain.Constants.ReservedProperty>.Values)
            {
                var propertyValues = reservedProperties.GetValueOrDefault((int) rp);
                if (propertyValues?.Values == null)
                {
                    continue;
                }

                var pd = PropertyInternals.ReservedPropertyMap.GetValueOrDefault(rp);
                if (pd == null)
                {
                    logger.LogError(
                        $"Couldn't find property descriptor for reserved property: [{(int) rp}]{rp.ToString()}");
                    continue;
                }

                foreach (var pv in propertyValues.Values)
                {
                    var stdValue = await standardValueService.Convert(pv.BizValue, pd.Type.GetBizValueType(),
                        pd.Type.GetDbValueType());

                    var data = dbData.GetValueOrDefault(r.Id)?.GetValueOrDefault(pv.Scope) ??
                               changed.GetValueOrDefault(r.Id)?.GetValueOrDefault(pv.Scope) ?? new ReservedPropertyValue
                                   {ResourceId = r.Id, Scope = pv.Scope};

                    var dataIsChanged = false;

                    switch (rp)
                    {
                        case Abstractions.Models.Domain.Constants.ReservedProperty.Introduction:
                        {
                            var intro = stdValue as string;
                            if (intro != data.Introduction)
                            {
                                data.Introduction = intro;
                                dataIsChanged = true;
                            }

                            break;
                        }
                        case Abstractions.Models.Domain.Constants.ReservedProperty.Rating:
                        {
                            var rating = stdValue as decimal?;
                            if (rating != data.Rating)
                            {
                                data.Rating = rating;
                                dataIsChanged = true;
                            }

                            break;
                        }
                        case Abstractions.Models.Domain.Constants.ReservedProperty.Cover:
                        {
                            var newCoverPaths = stdValue as List<string> ?? [];
                            var coverPaths = data.CoverPaths ?? [];
                            if (newCoverPaths.Count != coverPaths.Count || !newCoverPaths.SequenceEqual(coverPaths))
                            {
                                data.CoverPaths = newCoverPaths;
                                dataIsChanged = true;
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }

                    if (dataIsChanged)
                    {
                        changed.GetOrAdd(r.Id, _ => new Dictionary<int, ReservedPropertyValue>())[pv.Scope] = data;
                    }
                }
            }
        }

        var allChangedData = changed.SelectMany(c => c.Value.Values).Select(d => d.ToDbModel()).ToList();
        var dataToBeAdded = allChangedData.Where(d => d.Id == 0).ToList();
        var dataToBeUpdated = allChangedData.Except(dataToBeAdded).ToList();

        await orm.AddRange(dataToBeAdded);
        await orm.UpdateRange(dataToBeUpdated);

        return BaseResponseBuilder.Ok;
    }
}