using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Tag;

public interface ITagService
{
    Task SaveByResources(List<Models.Domain.Resource> resources);
    // Task<Abstractions.Models.Db.Tag> GetByKey(Int32 key, bool returnCopy = true);
    // Task<Abstractions.Models.Db.Tag[]> GetByKeys(IEnumerable<Int32> keys, bool returnCopy = true);
    //
    // Task<Abstractions.Models.Db.Tag> GetFirst(Expression<Func<Abstractions.Models.Db.Tag, bool>> selector,
    //     Expression<Func<Abstractions.Models.Db.Tag, object>> orderBy = null, bool asc = false, bool returnCopy = true);
    //
    // Task<List<Abstractions.Models.Db.Tag>> GetAll(Expression<Func<Abstractions.Models.Db.Tag, bool>> selector = null, bool returnCopy = true);
    // Task<int> Count(Func<Abstractions.Models.Db.Tag, bool> selector = null);
    //
    // /// <summary>
    // /// 
    // /// </summary>
    // /// <param name="selector"></param>
    // /// <param name="pageIndex"></param>
    // /// <param name="pageSize"></param>
    // /// <param name="orders">Key Selector - Asc</param>
    // /// <param name="returnCopy"></param>
    // /// <returns></returns>
    // Task<SearchResponse<Abstractions.Models.Db.Tag>> Search(Func<Abstractions.Models.Db.Tag, bool> selector,
    //     int pageIndex, int pageSize, (Func<Abstractions.Models.Db.Tag, object> SelectKey, bool Asc, IComparer<object>? comparer)[] orders,
    //     bool returnCopy = true);
    //
    // Task<SearchResponse<Abstractions.Models.Db.Tag>> Search(Func<Abstractions.Models.Db.Tag, bool> selector,
    //     int pageIndex, int pageSize, Func<Abstractions.Models.Db.Tag, object> orderBy = null, bool asc = false, IComparer<object>? comparer = null, bool returnCopy = true);
    //
    // Task<BaseResponse> Remove(Abstractions.Models.Db.Tag resource);
    // Task<BaseResponse> RemoveRange(IEnumerable<Abstractions.Models.Db.Tag> resources);
    // Task<BaseResponse> RemoveAll(Expression<Func<Abstractions.Models.Db.Tag, bool>> selector);
    // Task<BaseResponse> RemoveByKey(Int32 key);
    // Task<BaseResponse> RemoveByKeys(IEnumerable<Int32> keys);
    // Task<SingletonResponse<Abstractions.Models.Db.Tag>> Add(Abstractions.Models.Db.Tag resource);
    // Task<ListResponse<Abstractions.Models.Db.Tag>> AddRange(List<Abstractions.Models.Db.Tag> resources);
    // Task<SingletonResponse<Abstractions.Models.Db.Tag>> UpdateByKey(Int32 key, Action<Abstractions.Models.Db.Tag> modify);
    // Task<BaseResponse> Update(Abstractions.Models.Db.Tag resource);
    // Task<BaseResponse> UpdateRange(IReadOnlyCollection<Abstractions.Models.Db.Tag> resources);
    //
    // Task<ListResponse<Abstractions.Models.Db.Tag>> UpdateByKeys(IReadOnlyCollection<Int32> keys,
    //     Action<Abstractions.Models.Db.Tag> modify);
    //
    // Task<SingletonResponse<Abstractions.Models.Db.Tag>> UpdateFirst(Expression<Func<Abstractions.Models.Db.Tag, bool>> selector,
    //     Action<Abstractions.Models.Db.Tag> modify);
    //
    // Task<ListResponse<Abstractions.Models.Db.Tag>> UpdateAll(Expression<Func<Abstractions.Models.Db.Tag, bool>> selector,
    //     Action<Abstractions.Models.Db.Tag> modify);
}