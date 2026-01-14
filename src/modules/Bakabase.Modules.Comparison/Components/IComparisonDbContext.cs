using Bakabase.Modules.Comparison.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Comparison.Components;

public interface IComparisonDbContext
{
    DbSet<ComparisonPlanDbModel> ComparisonPlans { get; set; }
    DbSet<ComparisonRuleDbModel> ComparisonRules { get; set; }
    DbSet<ComparisonResultGroupDbModel> ComparisonResultGroups { get; set; }
    DbSet<ComparisonResultGroupMemberDbModel> ComparisonResultGroupMembers { get; set; }
    DbSet<ComparisonResultPairDbModel> ComparisonResultPairs { get; set; }
}
