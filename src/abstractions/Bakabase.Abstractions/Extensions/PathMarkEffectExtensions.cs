using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Extensions;

public static class PathMarkEffectExtensions
{
    #region ResourceMarkEffect

    public static ResourceMarkEffect ToDomainModel(this ResourceMarkEffectDbModel db) => new()
    {
        Id = db.Id,
        MarkId = db.MarkId,
        Path = db.Path,
        CreatedAt = db.CreatedAt
    };

    public static ResourceMarkEffectDbModel ToDbModel(this ResourceMarkEffect domain) => new()
    {
        Id = domain.Id,
        MarkId = domain.MarkId,
        Path = domain.Path,
        CreatedAt = domain.CreatedAt
    };

    public static List<ResourceMarkEffect> ToDomainModels(this IEnumerable<ResourceMarkEffectDbModel> dbModels)
        => dbModels.Select(d => d.ToDomainModel()).ToList();

    public static List<ResourceMarkEffectDbModel> ToDbModels(this IEnumerable<ResourceMarkEffect> domains)
        => domains.Select(d => d.ToDbModel()).ToList();

    #endregion

    #region PropertyMarkEffect

    public static PropertyMarkEffect ToDomainModel(this PropertyMarkEffectDbModel db) => new()
    {
        Id = db.Id,
        MarkId = db.MarkId,
        PropertyPool = (PropertyPool)db.PropertyPool,
        PropertyId = db.PropertyId,
        ResourceId = db.ResourceId,
        Value = db.Value,
        Priority = db.Priority,
        CreatedAt = db.CreatedAt,
        UpdatedAt = db.UpdatedAt
    };

    public static PropertyMarkEffectDbModel ToDbModel(this PropertyMarkEffect domain) => new()
    {
        Id = domain.Id,
        MarkId = domain.MarkId,
        PropertyPool = (int)domain.PropertyPool,
        PropertyId = domain.PropertyId,
        ResourceId = domain.ResourceId,
        Value = domain.Value,
        Priority = domain.Priority,
        CreatedAt = domain.CreatedAt,
        UpdatedAt = domain.UpdatedAt
    };

    public static List<PropertyMarkEffect> ToDomainModels(this IEnumerable<PropertyMarkEffectDbModel> dbModels)
        => dbModels.Select(d => d.ToDomainModel()).ToList();

    public static List<PropertyMarkEffectDbModel> ToDbModels(this IEnumerable<PropertyMarkEffect> domains)
        => domains.Select(d => d.ToDbModel()).ToList();

    #endregion
}
