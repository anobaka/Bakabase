using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using ReservedPropertyValue = Bakabase.Abstractions.Models.Domain.ReservedPropertyValue;

namespace Bakabase.InsideWorld.Business.Components.ReservedProperty;

public class ReservedPropertyValueService(
    FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.ReservedPropertyValue, int> orm)
    : IReservedPropertyValueService
{
    public async Task<ReservedPropertyValue?> GetFirst(
        Expression<Func<Abstractions.Models.Db.ReservedPropertyValue, bool>> selector)
    {
        return (await orm.GetFirst(selector))?.ToDomainModel();
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
}