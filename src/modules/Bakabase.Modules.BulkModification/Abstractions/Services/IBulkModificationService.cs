using Bakabase.Modules.BulkModification.Abstractions.Models;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.BulkModification.Abstractions.Services;

public interface IBulkModificationService
{
    Task<Models.BulkModification?> Get(int id);
    Task Add(Models.BulkModification bm);
    Task Put(Models.BulkModification bm);
    Task Delete(int id);
    Task Duplicate(int id);
    Task Patch(int id, PatchBulkModification model);

    Task<List<Models.BulkModification>> GetAll();

    Task<int[]> Filter(int id);
    Task Preview(int id);
    Task Apply(int id);
    Task Revert(int id);
}