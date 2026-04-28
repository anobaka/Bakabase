using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.HealthScore.Components;
using Bakabase.Modules.HealthScore.Models;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.HealthScore.Extensions;
using Bakabase.Modules.HealthScore.Models.Db;
using Bakabase.Modules.Search.Extensions;
using Bakabase.Modules.Search.Models.Db;
using DomainProperty = Bakabase.Abstractions.Models.Domain.Property;

namespace Bakabase.Modules.HealthScore.Extensions;

public static class HealthScoreProfileExtensions
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    public static HealthScoreProfileDbModel ToDbModel(
        this HealthScoreProfile p,
        IReadOnlyDictionary<(PropertyPool, int), DomainProperty> propertyMap)
    {
        var membershipDb = p.MembershipFilter?.ToDbModel();
        var ruleDbModels = p.Rules.Select(r => new HealthScoreRuleDbModel
        {
            Id = r.Id,
            Name = r.Name,
            Delta = r.Delta,
            Match = r.Match.ToDbModel(propertyMap),
        }).ToList();

        return new HealthScoreProfileDbModel
        {
            Id = p.Id,
            Name = p.Name,
            Enabled = p.Enabled,
            Priority = p.Priority,
            BaseScore = p.BaseScore,
            MembershipFilterJson = membershipDb is null
                ? null
                : JsonSerializer.Serialize(membershipDb, JsonOptions),
            RulesJson = JsonSerializer.Serialize(ruleDbModels, JsonOptions),
            CreatedAt = p.CreatedAt == default ? DateTime.Now : p.CreatedAt,
            UpdatedAt = p.UpdatedAt,
        };
    }

    public static async Task<HealthScoreProfile> ToDomainModelAsync(
        this HealthScoreProfileDbModel db,
        IPropertyService propertyService)
    {
        var allProperties = await propertyService.GetProperties(PropertyPool.All);
        var propertyMap = allProperties.ToDictionary(p => (p.Pool, p.Id), p => p);

        ResourceSearchFilterGroup? membership = null;
        if (!string.IsNullOrEmpty(db.MembershipFilterJson))
        {
            var membershipDb = JsonSerializer.Deserialize<ResourceSearchFilterGroupDbModel>(
                db.MembershipFilterJson, JsonOptions);
            if (membershipDb != null)
            {
                var membershipMap = allProperties
                    .GroupBy(p => p.Pool)
                    .ToDictionary(g => g.Key, g => g.ToDictionary(x => x.Id, x => x));
                membership = membershipDb.ToDomainModel(membershipMap);
            }
        }

        List<HealthScoreRule> rules;
        if (string.IsNullOrEmpty(db.RulesJson))
        {
            rules = new();
        }
        else
        {
            var ruleDbs = JsonSerializer.Deserialize<List<HealthScoreRuleDbModel>>(db.RulesJson, JsonOptions)
                          ?? new();
            rules = ruleDbs.Select(d => new HealthScoreRule
            {
                Id = d.Id,
                Name = d.Name,
                Delta = d.Delta,
                Match = d.Match.ToDomainModel(propertyMap),
            }).ToList();
        }

        return new HealthScoreProfile
        {
            Id = db.Id,
            Name = db.Name,
            Enabled = db.Enabled,
            Priority = db.Priority,
            BaseScore = db.BaseScore,
            MembershipFilter = membership,
            Rules = rules,
            CreatedAt = db.CreatedAt,
            UpdatedAt = db.UpdatedAt,
            ProfileHash = HealthScoreProfileHasher.Compute(db),
        };
    }
}
