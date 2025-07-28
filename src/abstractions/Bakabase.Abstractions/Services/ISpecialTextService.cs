using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Models.ResponseModels;
using System.Linq.Expressions;

namespace Bakabase.Abstractions.Services;

public interface ISpecialTextService
{
    // [Obsolete]
    // List<SpecialText> this[SpecialTextType type] { get; }
    Task<string> Pretreatment(string name);
    Task<List<SpecialText>> GetAll(Expression<Func<Abstractions.Models.Db.SpecialText, bool>>? selector = null, bool asNoTracking = true);

    Task<Dictionary<string, string>> GetAllWrappers();
    // Task<List<SpecialText>> GetAll(Expression<Func<SpecialText, bool>> selector = null, bool asNoTracking = true);
    Task<SingletonResponse<SpecialText>> Add(SpecialTextAddInputModel model);
    // Task<SingletonResponse<SpecialText>> Add(SpecialText resource);
    Task<BaseResponse> Patch(int id, SpecialTextPatchInputModel model);
    // Task<SingletonResponse<SpecialText>> UpdateByKey(Int32 key, Action<SpecialText> modify);
    Task<BaseResponse> AddPrefabs();
    Task<DateTime?> TryToParseDateTime(string? str);
    Task<List<(int Index, DateTime DateTime)>> TryToParseDateTime(string[] strings);
    // Task<ResourceLanguage?> TryToParseLanguage(string str);
    // Task<SpecialTextService.WrappedContent[]> MatchAllContentsWithWrappers(string str);
    // Task<(VolumeDto Volume, string Match, int Index)?> TryToParseVolume(string str);
    // InsideWorldDbContext DbContext { get; }
    // void ClearCache();
    // Task<SpecialText> GetByKey(Int32 key, bool asNoTracking = true);
    // Task<SpecialText[]> GetByKeys(IEnumerable<Int32> keys, bool asNoTracking = true);
    //
    // Task<SpecialText> GetFirst(Expression<Func<SpecialText, bool>> selector,
    //     Expression<Func<SpecialText, object>> orderBy = null, bool asc = false, bool asNoTracking = true);
    //
    Task<int> Count(Func<Models.Db.SpecialText, bool>? selector = null);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <param name="pageIndex"></param>
    // /// <param name="pageSize"></param>
    // /// <param name="orders">Key Selector - Asc</param>
    // /// <param name="asNoTracking"></param>
    // /// <returns></returns>
    // Task<SearchResponse<SpecialText>> Search(Func<SpecialText, bool> selector,
    //     int pageIndex, int pageSize, (Func<SpecialText, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool asNoTracking = true);
    //
    // Task<SearchResponse<SpecialText>> Search(Func<SpecialText, bool> selector,
    //     int pageIndex, int pageSize, Func<SpecialText, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool asNoTracking = true);
    //
    // Task<BaseResponse> Remove(SpecialText resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<SpecialText> resources);
    // Task<BaseResponse> RemoveAll(Expression<Func<SpecialText, bool>> selector);
    Task<BaseResponse> DeleteByKey(Int32 key);
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    Task<ListResponse<SpecialText>> AddRange(List<SpecialText> resources);
    // Task<BaseResponse> Update(SpecialText resource);
    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<SpecialText> resources);
    //
    // Task<ListResponse<SpecialText>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<SpecialText> modify);
    //
    // Task<SingletonResponse<SpecialText>> UpdateFirst(Expression<Func<SpecialText, bool>> selector,
    //     Action<SpecialText> modify);
    //
    // Task<ListResponse<SpecialText>> UpdateAll(Expression<Func<SpecialText, bool>> selector,
    //     Action<SpecialText> modify);
}