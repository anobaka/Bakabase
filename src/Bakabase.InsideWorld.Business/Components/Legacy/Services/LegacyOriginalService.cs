using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Components.Orm;

namespace Bakabase.InsideWorld.Business.Components.Legacy.Services
{
    [Obsolete]
    public class LegacyOriginalService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<InsideWorldDbContext, Original, int>(serviceProvider)
    {
        // public async Task<string[]> GetNamesByIds(int[] ids)
        // {
        //     var data = await _orm.GetByKeys(ids, false);
        //     return data.Select(t => t.Name).ToArray();
        // }
        //
        // public async Task<Dictionary<int, List<OriginalDto>>> GetByResourceIds(List<int> resourceIds)
        // {
        //     var mappings = (await _resourceMappingService.GetAll(a => resourceIds.Contains(a.ResourceId)))
        //         .GroupBy(a => a.ResourceId)
        //         .ToDictionary(a => a.Key, a => a.Select(b => b.OriginalId).ToList());
        //     var originalIds = mappings.Values.SelectMany(a => a).Distinct().ToList();
        //     var originals = (await _orm.GetAll(a => originalIds.Contains(a.Id))).ToDictionary(a => a.Id, a => a);
        //     var originalDtoList = resourceIds.ToDictionary(a => a,
        //         a => mappings.TryGetValue(a, out var oIds)
        //             ? oIds.Select(t => originals.TryGetValue(t, out var o) ? o.ToDto() : null)
        //                 .Where(t => t != null).ToList()
        //             : null);
        //     return originalDtoList;
        // }
        //
        // public async Task<List<OriginalDto>> GetAllDtoList(Expression<Func<Original, bool>>? selector = null,
        //     bool returnCopy = true)
        // {
        //     var data = await _orm.GetAll(selector, returnCopy);
        //     var list = data.Select(d => d.ToDto()!).ToList();
        //     return list;
        // }
        //
        // /// <summary>
        // /// If a record with the same name does not exist in the database, a new data entry will be created, regardless of whether it has an id.
        // /// </summary>
        // /// <param name="names"></param>
        // /// <returns></returns>
        // public async Task<SimpleRangeAddResult<OriginalDto>> GetOrAddRangeByNames(List<string> names)
        // {
        //     names = names.Distinct().ToList();
        //     var existOriginals = (await _orm.GetAll(a => names.Contains(a.Name))).ToList();
        //     var existOriginalNames = existOriginals.Select(a => a.Name).ToList();
        //     var newOriginals = names.Except(existOriginalNames)
        //         .Select(a => new Original {Name = a}).ToList();
        //     var result = existOriginals.Concat((await _orm.AddRange(newOriginals)).Data)
        //         .ToDictionary(a => a.Name, a => a.ToDto());
        //     return new SimpleRangeAddResult<OriginalDto>
        //     {
        //         Data = result!,
        //         AddedCount = newOriginals.Count,
        //         ExistingCount = existOriginals.Count
        //     };
        // }
        //
        // /// <summary>
        // /// todo: optimize
        // /// </summary>
        // /// <param name="names"></param>
        // /// <param name="fuzzy"></param>
        // /// <returns></returns>
        // public async Task<List<int>> GetAllIdsByNames(HashSet<string> names, bool fuzzy)
        // {
        //     Expression<Func<Original, bool>> exp =
        //         fuzzy ? a => names.Any(b => a.Name.Contains(b)) : a => names.Contains(a.Name);
        //     var originals = await _orm.GetAll(exp);
        //     return originals.Select(a => a.Id).ToList();
        // }
        //
        //
        // public async Task<List<int>> GetAllIdsByRegexs(HashSet<string> regexStrings)
        // {
        //     var allData = await _orm.GetAll();
        //     var regexs = regexStrings.Select(t => new Regex(t)).ToArray();
        //     var ids = allData.Where(t => regexs.Any(r => r.IsMatch(t.Name))).Select(t => t.Id)
        //         .ToList();
        //     return ids;
        // }
    }
}