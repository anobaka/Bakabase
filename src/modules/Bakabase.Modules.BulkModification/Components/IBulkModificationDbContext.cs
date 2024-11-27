using Bakabase.Modules.BulkModification.Models.Db;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.BulkModification.Components;

public interface IBulkModificationDbContext
{
    DbSet<BulkModificationDbModel> BulkModifications { get; set; }
    DbSet<BulkModificationDiffDbModel> BulkModificationDiffs { get; set; }
}