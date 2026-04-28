using System.Linq.Expressions;
using System.Text.Json;
using Bakabase.Modules.DataCard.Abstractions.Models.Db;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;
using Bakabase.Modules.DataCard.Abstractions.Services;
using Bakabase.Modules.DataCard.Extensions;
using Bakabase.Modules.DataCard.Models.Input;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.DataCard.Services;

public class DataCardTypeService<TDbContext>(
    FullMemoryCacheResourceService<TDbContext, DataCardTypeDbModel, int> orm) : IDataCardTypeService
    where TDbContext : DbContext
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public async Task<List<DataCardType>> GetAll(
        Expression<Func<DataCardTypeDbModel, bool>>? selector = null)
    {
        var dbModels = await orm.GetAll(selector);
        return dbModels.Select(d => d.ToDomainModel()).ToList();
    }

    public async Task<DataCardType?> GetById(int id)
    {
        var db = await orm.GetByKey(id);
        return db?.ToDomainModel();
    }

    public async Task<SingletonResponse<DataCardType>> Add(DataCardTypeAddInputModel model)
    {
        var now = DateTime.Now;
        var db = new DataCardTypeDbModel
        {
            Name = model.Name,
            PropertyIds = model.PropertyIds == null
                ? null
                : JsonSerializer.Serialize(model.PropertyIds, JsonOptions),
            IdentityPropertyIds = model.IdentityPropertyIds == null
                ? null
                : JsonSerializer.Serialize(model.IdentityPropertyIds, JsonOptions),
            NameTemplate = model.NameTemplate,
            MatchRules = model.MatchRules == null
                ? null
                : JsonSerializer.Serialize(model.MatchRules, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };
        db = (await orm.Add(db)).Data;
        return new SingletonResponse<DataCardType>(db.ToDomainModel());
    }

    public async Task<BaseResponse> Update(int id, DataCardTypeUpdateInputModel model)
    {
        var db = await orm.GetByKey(id);
        if (db == null)
        {
            return BaseResponseBuilder.NotFound;
        }

        if (model.Name != null)
        {
            db.Name = model.Name;
        }

        if (model.PropertyIds != null)
        {
            db.PropertyIds = JsonSerializer.Serialize(model.PropertyIds, JsonOptions);
        }

        if (model.IdentityPropertyIds != null)
        {
            db.IdentityPropertyIds = JsonSerializer.Serialize(model.IdentityPropertyIds, JsonOptions);
        }

        if (model.NameTemplate != null)
        {
            db.NameTemplate = model.NameTemplate;
        }

        if (model.MatchRules != null)
        {
            db.MatchRules = JsonSerializer.Serialize(model.MatchRules, JsonOptions);
        }

        if (model.Order.HasValue)
        {
            db.Order = model.Order.Value;
        }

        db.UpdatedAt = DateTime.Now;
        await orm.Update(db);
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> UpdateDisplayTemplate(int id,
        DataCardDisplayTemplate template)
    {
        var db = await orm.GetByKey(id);
        if (db == null)
        {
            return BaseResponseBuilder.NotFound;
        }

        db.DisplayTemplate = JsonSerializer.Serialize(template, JsonOptions);
        db.UpdatedAt = DateTime.Now;
        await orm.Update(db);
        return BaseResponseBuilder.Ok;
    }

    public async Task<BaseResponse> Delete(int id)
    {
        return await orm.RemoveByKey(id);
    }
}
