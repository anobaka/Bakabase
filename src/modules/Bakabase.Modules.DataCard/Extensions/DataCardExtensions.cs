using System.Text.Json;
using Bakabase.Modules.DataCard.Abstractions.Models.Db;
using Bakabase.Modules.DataCard.Abstractions.Models.Domain;

namespace Bakabase.Modules.DataCard.Extensions;

public static class DataCardExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public static DataCardType ToDomainModel(this DataCardTypeDbModel db)
    {
        return new DataCardType
        {
            Id = db.Id,
            Name = db.Name,
            PropertyIds = string.IsNullOrEmpty(db.PropertyIds)
                ? null
                : JsonSerializer.Deserialize<List<int>>(db.PropertyIds, JsonOptions),
            IdentityPropertyIds = string.IsNullOrEmpty(db.IdentityPropertyIds)
                ? null
                : JsonSerializer.Deserialize<List<int>>(db.IdentityPropertyIds, JsonOptions),
            NameTemplate = db.NameTemplate,
            DisplayTemplate = string.IsNullOrEmpty(db.DisplayTemplate)
                ? null
                : JsonSerializer.Deserialize<DataCardDisplayTemplate>(db.DisplayTemplate, JsonOptions),
            MatchRules = string.IsNullOrEmpty(db.MatchRules)
                ? null
                : JsonSerializer.Deserialize<DataCardMatchRules>(db.MatchRules, JsonOptions),
            Order = db.Order,
            CreatedAt = db.CreatedAt,
            UpdatedAt = db.UpdatedAt
        };
    }

    public static DataCardTypeDbModel ToDbModel(this DataCardType domain)
    {
        return new DataCardTypeDbModel
        {
            Id = domain.Id,
            Name = domain.Name,
            PropertyIds = domain.PropertyIds == null
                ? null
                : JsonSerializer.Serialize(domain.PropertyIds, JsonOptions),
            IdentityPropertyIds = domain.IdentityPropertyIds == null
                ? null
                : JsonSerializer.Serialize(domain.IdentityPropertyIds, JsonOptions),
            NameTemplate = domain.NameTemplate,
            DisplayTemplate = domain.DisplayTemplate == null
                ? null
                : JsonSerializer.Serialize(domain.DisplayTemplate, JsonOptions),
            MatchRules = domain.MatchRules == null
                ? null
                : JsonSerializer.Serialize(domain.MatchRules, JsonOptions),
            Order = domain.Order,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt
        };
    }

    public static Abstractions.Models.Domain.DataCard ToDomainModel(this DataCardDbModel db)
    {
        return new Abstractions.Models.Domain.DataCard
        {
            Id = db.Id,
            TypeId = db.TypeId,
            CreatedAt = db.CreatedAt,
            UpdatedAt = db.UpdatedAt
        };
    }

    public static DataCardDbModel ToDbModel(this Abstractions.Models.Domain.DataCard domain)
    {
        return new DataCardDbModel
        {
            Id = domain.Id,
            TypeId = domain.TypeId,
            CreatedAt = domain.CreatedAt,
            UpdatedAt = domain.UpdatedAt
        };
    }

    public static DataCardPropertyValue ToDomainModel(this DataCardPropertyValueDbModel db)
    {
        return new DataCardPropertyValue
        {
            Id = db.Id,
            CardId = db.CardId,
            PropertyId = db.PropertyId,
            Value = db.Value,
            Scope = db.Scope
        };
    }

    public static DataCardPropertyValueDbModel ToDbModel(this DataCardPropertyValue domain)
    {
        return new DataCardPropertyValueDbModel
        {
            Id = domain.Id,
            CardId = domain.CardId,
            PropertyId = domain.PropertyId,
            Value = domain.Value,
            Scope = domain.Scope
        };
    }
}
