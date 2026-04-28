using Bakabase.Modules.HealthScore.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.HealthScore.Components;

public interface IHealthScoreDbContext
{
    DbSet<HealthScoreProfileDbModel> HealthScoreProfiles { get; set; }
    DbSet<ResourceHealthScoreDbModel> ResourceHealthScores { get; set; }
}
