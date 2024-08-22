using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using BuiltinPropertyValue = Bakabase.Abstractions.Models.Domain.BuiltinPropertyValue;

namespace Bakabase.InsideWorld.Business.Components.BuiltinProperty;

public class BuiltinPropertyValueService(
    FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.BuiltinPropertyValue, int> orm)
    : IBuiltinPropertyValueService
{
    public async Task<BuiltinPropertyValue?> GetFirst(
        Expression<Func<Abstractions.Models.Db.BuiltinPropertyValue, bool>> selector)
    {
        return (await orm.GetFirst(selector))?.ToDomainModel();
    }

    public async Task<List<BuiltinPropertyValue>> GetAll(
        Expression<Func<Abstractions.Models.Db.BuiltinPropertyValue, bool>>? selector = null, bool asNoTracking = true)
    {
        var data = await orm.GetAll(selector, asNoTracking);
        return data.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<SingletonResponse<BuiltinPropertyValue>> Add(BuiltinPropertyValue resource)
    {
        return new SingletonResponse<BuiltinPropertyValue>((await orm.Add(resource.ToDbModel())).Data.ToDomainModel());
    }

    public async Task<ListResponse<BuiltinPropertyValue>> AddRange(List<BuiltinPropertyValue> resources)
    {
        var data = await orm.AddRange(resources.Select(x => x.ToDbModel()).ToList());
        return new ListResponse<BuiltinPropertyValue>(data.Data.Select(d => d.ToDomainModel()));
    }

    public async Task<BaseResponse> Update(BuiltinPropertyValue resource)
    {
        return await orm.Update(resource.ToDbModel());
    }
}