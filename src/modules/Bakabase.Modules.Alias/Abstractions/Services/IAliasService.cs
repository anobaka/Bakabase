using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.Alias.Models.Input;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.Modules.Alias.Abstractions.Services;

public interface IAliasService
{
    Task<List<object>> GetAliasAppliedValues(List<(object BizValue, StandardValueType BizValueType)> values);
    Task<DataChangeViewModel> SaveByResources(List<Bakabase.Abstractions.Models.Domain.Resource> resources);
    Task<SingletonResponse<Models.Domain.Alias>> Add(AliasAddInputModel model);
    Task<List<Models.Db.Alias>> AddAll(IEnumerable<Models.Db.Alias> aliases);
    Task<BaseResponse> Patch(string text, AliasPatchInputModel model);
    // Task<ListResponse<Abstractions.Models.Db.Alias>> AddRange(List<Abstractions.Models.Db.Alias> resources);
    // Task<Abstractions.Models.Db.Alias> GetByKey(Int32 key, bool returnCopy = true);
    // Task<Abstractions.Models.Db.Alias[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    //
    // Task<Abstractions.Models.Db.Alias> GetFirst(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector,
    //     Expression<Func<Abstractions.Models.Db.Alias, object>> orderBy = null, bool asc = false, bool returnCopy = true);
    //
    // Task<List<Abstractions.Models.Db.Alias>> GetAll(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector = null, bool returnCopy = true);
    Task<int> Count(Func<Models.Db.Alias, bool>? selector = null);
    Task<bool> Any(Func<Models.Db.Alias, bool>? selector = null);
    
    Task<SearchResponse<Models.Domain.Alias>> SearchGroups(AliasSearchInputModel model);
    //
    // Task<SearchResponse<Abstractions.Models.Db.Alias>> Search(Func<Abstractions.Models.Db.Alias, bool> selector,
    //     int pageIndex, int pageSize, Func<Abstractions.Models.Db.Alias, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    // Task<BaseResponse> Remove(Abstractions.Models.Db.Alias resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<Abstractions.Models.Db.Alias> resources);
    // Task<BaseResponse> RemoveAll(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector);
    Task<BaseResponse> Delete(string text);
    Task<BaseResponse> DeleteGroups(string[] preferredTexts);
    Task<BaseResponse> MergeGroups(string[] preferredTexts);
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    // Task<SingletonResponse<Abstractions.Models.Db.Alias>> Add(Abstractions.Models.Db.Alias resource);
    // Task<SingletonResponse<Abstractions.Models.Db.Alias>> UpdateByKey(Int32 key, Action<Abstractions.Models.Db.Alias> modify);
    // Task<BaseResponse> Update(Abstractions.Models.Db.Alias resource);
    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<Abstractions.Models.Db.Alias> resources);
    //
    // Task<ListResponse<Abstractions.Models.Db.Alias>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<Abstractions.Models.Db.Alias> modify);
    //
    // Task<SingletonResponse<Abstractions.Models.Db.Alias>> UpdateFirst(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector,
    //     Action<Abstractions.Models.Db.Alias> modify);
    //
    // Task<ListResponse<Abstractions.Models.Db.Alias>> UpdateAll(Expression<Func<Abstractions.Models.Db.Alias, bool>> selector,
    //     Action<Abstractions.Models.Db.Alias> modify);
}