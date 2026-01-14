using System.Text.Json;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Comparison.Models.Db;
using Bakabase.Modules.Comparison.Models.Domain;
using Bakabase.Modules.Comparison.Models.Domain.Constants;
using Bakabase.Modules.Comparison.Models.Input;
using Bakabase.Modules.Comparison.Models.View;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Search.Extensions;
using Bakabase.Modules.Search.Models.Db;
using Bootstrap.Extensions;

namespace Bakabase.Modules.Comparison.Extensions;

public static class ComparisonExtensions
{
    #region ComparisonPlan

    public static async Task<ComparisonPlan> ToDomainModel(this ComparisonPlanDbModel db, List<ComparisonRule> rules, IPropertyService propertyService)
    {
        ResourceSearch? search = null;
        if (!string.IsNullOrEmpty(db.SearchJson))
        {
            var searchDbModel = db.SearchJson.JsonDeserializeOrDefault<ResourceSearchDbModel>();
            if (searchDbModel != null)
            {
                search = await searchDbModel.ToDomainModel(propertyService);
            }
        }

        return new ComparisonPlan
        {
            Id = db.Id,
            Name = db.Name,
            Search = search,
            Threshold = db.Threshold,
            Rules = rules,
            CreatedAt = db.CreatedAt,
            LastRunAt = db.LastRunAt
        };
    }

    public static async Task<ComparisonPlanViewModel> ToViewModel(this ComparisonPlanDbModel db, List<ComparisonRuleViewModel> rules, IPropertyService propertyService, int? groupCount = null)
    {
        ResourceSearch? search = null;
        if (!string.IsNullOrEmpty(db.SearchJson))
        {
            var searchDbModel = db.SearchJson.JsonDeserializeOrDefault<ResourceSearchDbModel>();
            if (searchDbModel != null)
            {
                search = await searchDbModel.ToDomainModel(propertyService);
            }
        }

        return new ComparisonPlanViewModel
        {
            Id = db.Id,
            Name = db.Name,
            Search = search,
            Threshold = db.Threshold,
            Rules = rules,
            CreatedAt = db.CreatedAt,
            LastRunAt = db.LastRunAt,
            ResultGroupCount = groupCount
        };
    }

    #endregion

    #region ComparisonRule

    public static ComparisonRule ToDomainModel(this ComparisonRuleDbModel db)
    {
        return new ComparisonRule
        {
            Id = db.Id,
            PlanId = db.PlanId,
            Order = db.Order,
            PropertyPool = db.PropertyPool,
            PropertyId = db.PropertyId,
            PropertyValueScope = db.PropertyValueScope,
            Mode = db.Mode,
            Parameter = db.Parameter,
            Normalize = db.Normalize,
            Weight = db.Weight,
            IsVeto = db.IsVeto,
            VetoThreshold = db.VetoThreshold,
            OneNullBehavior = db.OneNullBehavior,
            BothNullBehavior = db.BothNullBehavior
        };
    }

    public static ComparisonRuleViewModel ToViewModel(this ComparisonRuleDbModel db)
    {
        object? parameter = null;
        if (!string.IsNullOrEmpty(db.Parameter))
        {
            try
            {
                parameter = JsonSerializer.Deserialize<object>(db.Parameter);
            }
            catch
            {
                parameter = db.Parameter;
            }
        }

        return new ComparisonRuleViewModel
        {
            Id = db.Id,
            Order = db.Order,
            PropertyPool = db.PropertyPool,
            PropertyId = db.PropertyId,
            PropertyValueScope = db.PropertyValueScope,
            Mode = db.Mode,
            Parameter = parameter,
            Normalize = db.Normalize,
            Weight = db.Weight,
            IsVeto = db.IsVeto,
            VetoThreshold = db.VetoThreshold,
            OneNullBehavior = db.OneNullBehavior,
            BothNullBehavior = db.BothNullBehavior
        };
    }

    public static ComparisonRuleDbModel ToDbModel(this ComparisonRuleInputModel input, int planId, int order)
    {
        string? parameter = null;
        if (input.Parameter != null)
        {
            parameter = input.Parameter is string str ? str : JsonSerializer.Serialize(input.Parameter);
        }

        return new ComparisonRuleDbModel
        {
            PlanId = planId,
            Order = order,
            PropertyPool = input.PropertyPool,
            PropertyId = input.PropertyId,
            PropertyValueScope = input.PropertyValueScope.HasValue ? input.PropertyValueScope.Value : null,
            Mode = input.Mode,
            Parameter = parameter,
            Normalize = input.Normalize,
            Weight = input.Weight,
            IsVeto = input.IsVeto,
            VetoThreshold = input.VetoThreshold,
            OneNullBehavior = input.OneNullBehavior,
            BothNullBehavior = input.BothNullBehavior
        };
    }

    #endregion

    #region ComparisonResultGroup

    public static ComparisonResultGroupViewModel ToViewModel(this ComparisonResultGroupDbModel db, List<ComparisonResultGroupMemberViewModel>? members = null)
    {
        return new ComparisonResultGroupViewModel
        {
            Id = db.Id,
            PlanId = db.PlanId,
            MemberCount = db.MemberCount,
            IsHidden = db.IsHidden,
            Members = members,
            CreatedAt = db.CreatedAt
        };
    }

    #endregion

    #region ComparisonResultGroupMember

    public static ComparisonResultGroupMemberViewModel ToViewModel(this ComparisonResultGroupMemberDbModel db)
    {
        return new ComparisonResultGroupMemberViewModel
        {
            Id = db.Id,
            GroupId = db.GroupId,
            ResourceId = db.ResourceId,
            IsSuggestedPrimary = db.IsSuggestedPrimary
        };
    }

    #endregion

    #region ComparisonResultPair

    public static ComparisonResultPairViewModel ToViewModel(this ComparisonResultPairDbModel db)
    {
        List<RuleScoreDetailViewModel>? ruleScores = null;
        if (!string.IsNullOrEmpty(db.RuleScoresJson))
        {
            try
            {
                var details = JsonSerializer.Deserialize<List<RuleScoreDetail>>(db.RuleScoresJson);
                ruleScores = details?.Select(d => new RuleScoreDetailViewModel
                {
                    RuleId = d.RuleId,
                    Order = d.Order,
                    Score = d.Score,
                    Weight = d.Weight,
                    Value1 = d.Value1,
                    Value2 = d.Value2,
                    IsSkipped = d.IsSkipped,
                    IsVetoed = d.IsVetoed
                }).ToList();
            }
            catch
            {
                // Ignore deserialization errors
            }
        }

        return new ComparisonResultPairViewModel
        {
            Id = db.Id,
            GroupId = db.GroupId,
            Resource1Id = db.Resource1Id,
            Resource2Id = db.Resource2Id,
            TotalScore = db.TotalScore,
            RuleScores = ruleScores
        };
    }

    #endregion
}
