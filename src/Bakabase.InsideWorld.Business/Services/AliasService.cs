using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.Constants;
using Bootstrap.Models.ResponseModels;
using CsQuery.ExtensionMethods.Internal;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Services
{
    public class AliasService : FullMemoryCacheResourceService<InsideWorldDbContext, Alias, int>
    {
        protected AliasGroupService AliasGroupService => GetRequiredService<AliasGroupService>();

        public AliasService(IServiceProvider serviceProvider) : base(serviceProvider)
        {
        }

        public async Task<int> GetMaxGroupIdAsync()
        {
            return (await DbContext.Aliases.OrderByDescending(t => t.GroupId).FirstOrDefaultAsync())?.GroupId ?? 0;
        }

        public async Task<Dictionary<string, string>> GetPreferredNames(HashSet<string> names)
        {
            var aliasAndGroupIds =
                (await GetAll(a => names.Contains(a.Name))).ToDictionary(a => a.Name, a => a.GroupId);
            var groupIds = aliasAndGroupIds.Values.ToHashSet();
            var preferredAliases =
                (await GetAll(a => a.IsPreferred && groupIds.Contains(a.GroupId))).ToDictionary(a => a.GroupId,
                    a => a.Name);
            return names.ToDictionary(a => a,
                a => aliasAndGroupIds.TryGetValue(a, out var groupId) ? preferredAliases[groupId] : a);
        }

        public Task<Alias> GetByName(string name) =>
            GetFirst(t => t.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        public async Task<SingletonResponse<Alias>> Create(AliasCreateRequestModel model)
        {
            var alias = await GetByName(model.Name);
            if (alias != null)
            {
                return SingletonResponseBuilder<Alias>.BuildBadRequest("Name is duplicated");
            }

            alias = new Alias
            {
                GroupId = model.GroupId ?? await GetMaxGroupIdAsync() + 1,
                Name = model.Name,
                IsPreferred = !model.GroupId.HasValue ||
                              !await DbContext.Aliases.AnyAsync(a => a.GroupId == model.GroupId.Value && a.IsPreferred)
            };
            return await Add(alias);
        }

        public async Task<BaseResponse> UpdateByKey(int id, AliasUpdateRequestModel model)
        {
            if (!string.IsNullOrEmpty(model.Name))
            {
                var existedAlias = await GetByName(model.Name);
                if (existedAlias != null)
                {
                    var currentAlias = await GetByKey(id);
                    return BaseResponseBuilder.Build(ResponseCode.InvalidPayloadOrOperation,
                        existedAlias.GroupId == currentAlias.GroupId ? "同组内已存在相同词语" : "在其他组内发现同名词，可考虑合并分组");
                }
            }

            if (model.IsPreferred)
            {
                var a = await GetByKey(id);
                if (!a.IsPreferred)
                {
                    await UpdateAll(t => t.GroupId == a.GroupId && t.Id != id, t => t.IsPreferred = false);
                }
            }

            await base.UpdateByKey(id, t =>
            {
                if (!string.IsNullOrEmpty(model.Name))
                {
                    t.Name = model.Name;
                }

                if (!t.IsPreferred && model.IsPreferred)
                {
                    t.IsPreferred = model.IsPreferred;
                }
            });

            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// 会返回同组所有词语
        /// </summary>
        /// <returns></returns>
        public async Task<SearchResponse<AliasDto>> Search(AliasSearchRequestModel model)
        {
            Func<Alias, bool> func = null;
            var names = new HashSet<string> {model.Name};
            if (model.Names != null)
            {
                names.AddRange(model.Names);
            }

            names.RemoveWhere(a => a.IsNullOrEmpty());

            if (names.Any())
            {
                if (names.Count == 1)
                {
                    var name = names.FirstOrDefault()!;
                    func = model.Exactly ? a => a.Name == name : a => a.Name.Contains(name);
                }
                else
                {
                    // force exactly
                    func = a => names.Contains(a.Name);
                }
            }

            var aliases = await Search(func, model.PageIndex, model.PageSize);
            var allAliases = aliases.Data;
            if (model.AdditionalItems.HasFlag(AliasAdditionalItem.Candidates))
            {
                allAliases = (await GetGroupMembers(allAliases)).ToList();
            }

            return model.BuildResponse(allAliases.Merge(), aliases.TotalCount);
        }

        public async Task<string> Export()
        {
            var aliases = await GetAll(null, false);
            var groupedAliases = aliases.GroupBy(t => t.GroupId).ToList();

            var data = (from ga in groupedAliases
                let preferred = (ga.FirstOrDefault(a => a.IsPreferred)?.Name)
                let candidates = ga.Where(t => t.Name != preferred).Select(t => t.Name).ToArray()
                select (Preferred: preferred, Candidates: candidates)).ToArray();

            var lines = data.Select(a =>
                string.Join(',',
                    new[] {a.Preferred}.Concat(a.Candidates).Distinct().Where(t => t.IsNotEmpty())
                        .Select(CsvUtils.Escape)));
            var text = string.Join(Environment.NewLine, lines);
            return text;
        }

        public async Task<BaseResponse> Import(Stream stream)
        {
            var rawData = CsvUtils.ReadAllLines(stream);
            var data = rawData.Where(t => t.Any()).Select(t => t.Distinct().ToArray()).ToList();
            var uniques = new Dictionary<string, int>();
            var duplicates = new HashSet<string>();
            for (var i = 0; i < data.Count; i++)
            {
                foreach (var d in data[i])
                {
                    if (uniques.ContainsKey(d))
                    {
                        duplicates.Add(d);
                    }
                    else
                    {
                        uniques[d] = i;
                    }
                }
            }

            if (duplicates.Any())
            {
                return BaseResponseBuilder.BuildBadRequest(
                    $"Duplicate aliases are found: {string.Join(',', duplicates)}");
            }

            var aliases = await GetAll(null, false);
            var groupedAliases = aliases.GroupBy(t => t.GroupId).ToDictionary(a => a.Key, a => a.ToArray());
            var nameGroupIdMap = aliases.ToDictionary(a => a.Name, a => a.GroupId);
            var names = nameGroupIdMap.Keys.ToHashSet();

            var errors = new List<string>();
            var newAliases = new List<Alias[]>();
            var newKingAliasIds = new List<int>();
            var fallenKingAliasIds = new List<int>();

            foreach (var newGroupNames in data)
            {
                var associateNames = newGroupNames.Where(names.Contains).ToArray();
                var associateGroupIds = associateNames.Select(t => nameGroupIdMap[t]).ToHashSet();
                if (associateGroupIds.Count > 1)
                {
                    var msg =
                        $"[{string.Join(",", newGroupNames)}] exist in more than one alias group on words: [{string.Join(",", associateNames)}]";
                    errors.Add(msg);
                    continue;
                }

                if (associateGroupIds.Any())
                {
                    var dbGroupId = associateGroupIds.First();
                    var dbGroup = groupedAliases[dbGroupId];
                    var dbCurrentPreferred = dbGroup.FirstOrDefault(t => t.IsPreferred);
                    var newPreferredName = newGroupNames[0];
                    var @new = newGroupNames.Except(dbGroup.Select(g => g.Name)).Select(a => new Alias
                    {
                        GroupId = dbGroupId,
                        Name = a
                    }).ToArray();

                    var sameNameAliasInDb = dbGroup.FirstOrDefault(t => t.Name == newPreferredName);

                    if (sameNameAliasInDb == null)
                    {
                        @new[0].IsPreferred = true;
                    }
                    else
                    {
                        if (!sameNameAliasInDb.IsPreferred)
                        {
                            newKingAliasIds.Add(sameNameAliasInDb.Id);
                        }
                    }

                    if (dbCurrentPreferred != null && dbCurrentPreferred != sameNameAliasInDb)
                    {
                        fallenKingAliasIds.Add(dbCurrentPreferred.Id);
                    }

                    if (@new.Length > 0)
                    {
                        newAliases.Add(@new);
                    }
                }
                else
                {
                    newAliases.Add(newGroupNames.Select((t, i) => new Alias
                    {
                        Name = t,
                        IsPreferred = i == 0
                    }).ToArray());
                }
            }

            newAliases.RemoveAll(a => !a.Any());

            if (newAliases.Any())
            {
                var groupIds = await AliasGroupService.RequestNewGroupIds(newAliases.Count);
                for (var i = 0; i < newAliases.Count; i++)
                {
                    foreach (var a in newAliases[i])
                    {
                        a.GroupId = groupIds[i];
                    }
                }

                await AddRange(newAliases.SelectMany(t => t).ToList());
            }

            if (newKingAliasIds.Any())
            {
                await UpdateByKeys(newKingAliasIds, t => t.IsPreferred = true);
            }

            if (fallenKingAliasIds.Any())
            {
                await UpdateByKeys(fallenKingAliasIds, t => t.IsPreferred = false);
            }

            return BaseResponseBuilder.Build(ResponseCode.Success,
                errors.Any() ? string.Join(Environment.NewLine, errors) : null);
        }

        /// <summary>
        /// 如果不存在，则创建
        /// </summary>
        /// <param name="names"></param>
        /// <returns></returns>
        public async Task<SimpleRangeAddResult<AliasDto>> AddRange(List<string> names)
        {
            var distinctNames = names.Distinct().Where(a => a.IsNotEmpty()).ToList();
            var existedAliases = (await GetAll(a => distinctNames.Contains(a.Name)))
                .ToDictionary(a => a.Name, a => a.ToDto());
            var newNames = distinctNames.Except(existedAliases.Keys).ToList();
            var newGroups = (await AliasGroupService.AddRange(newNames.Select(a => new AliasGroup()).ToList())).Data;
            var newAliases = newNames
                .Select((a, i) => new Alias {Name = a, IsPreferred = true, GroupId = newGroups[i].Id}).ToList();
            var newAliasDtoList = (await AddRange(newAliases)).Data;
            var result = new Dictionary<string, AliasDto>(existedAliases);
            foreach (var d in newAliasDtoList)
            {
                result.Add(d.Name, d.ToDto());
            }

            return new SimpleRangeAddResult<AliasDto>
            {
                Data = result,
                AddedCount = newAliasDtoList.Count,
                ExistingCount = existedAliases.Count
            };
        }

        public async Task<int> GroupCount() => (await GetCacheVault()).Values.GroupBy(t => t.GroupId).Count();

        public override async Task<BaseResponse> RemoveByKey(int id)
        {
            var alias = await GetByKey(id);

            if (alias.IsPreferred)
            {
                var candidates = await GetGroupMembers(new List<Alias> {alias});
                var notPreferredCandidate = candidates.FirstOrDefault(a => a.Id != id);
                if (notPreferredCandidate != null)
                {
                    await UpdateByKey(notPreferredCandidate.Id, a => a.IsPreferred = true);
                }
            }

            await base.RemoveByKey(id);
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> RemoveGroup(int groupId)
        {
            await RemoveAll(t => t.GroupId == groupId);
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> MergeGroup(int groupId, AliasGroupUpdateRequestModel model)
        {
            await UpdateAll(t => t.GroupId == groupId, t =>
            {
                t.GroupId = model.TargetGroupId;
                t.IsPreferred = false;
            });
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="names"></param>
        /// <param name="fuzzy"></param>
        /// <param name="populateCandidates"></param>
        /// <returns>
        /// search key - matched alias groups
        /// </returns>
        public async Task<Dictionary<string, List<AliasDto>>> GetByNames(IEnumerable<string> names, bool fuzzy,
            bool populateCandidates)
        {
            var distinctNames = names.ToHashSet();
            Expression<Func<Alias, bool>> exp =
                fuzzy ? a => distinctNames.Any(b => a.Name.Contains(b)) : a => distinctNames.Contains(a.Name);
            var aliases = await GetAll(exp);

            var notPreferredAliasGroupIds = aliases.GroupBy(a => a.GroupId).Where(a => a.All(b => !b.IsPreferred))
                .Select(a => a.Key).ToHashSet();
            if (notPreferredAliasGroupIds.Any())
            {
                var additionalAliases =
                    await GetAll(a => notPreferredAliasGroupIds.Contains(a.GroupId) && a.IsPreferred);
                aliases.AddRange(additionalAliases);
            }

            var queryGroupIdMap = distinctNames.ToDictionary(n => n,
                n => aliases.Where(a => fuzzy ? a.Name.Contains(n) : a.Name == n).Select(x => x.GroupId).ToHashSet());
            if (populateCandidates)
            {
                aliases = await GetGroupMembers(aliases);
            }

            var mergedAliases = aliases.Merge();

            return distinctNames.ToDictionary(n => n, n =>
            {
                var groupIds = queryGroupIdMap[n];
                return mergedAliases.Where(v => groupIds.Contains(v.GroupId)).ToList();
            });
        }

        private async Task<List<Alias>> GetGroupMembers(List<Alias> aliases)
        {
            var groupIds = aliases.Select(a => a.GroupId).Distinct().ToList();
            aliases = await GetAll(a => groupIds.Contains(a.GroupId));
            return aliases;
        }

        public Task<Dictionary<string, List<AliasDto>>> GetByName(string name, bool fuzzy, bool populateCandidates) =>
            GetByNames(new[] {name}, fuzzy, populateCandidates);

        public async Task RemoveInvalid()
        {
            try
            {
                // Check exists
                var count = await Count();
            }
            catch (Exception e)
            {
                Logger.LogWarning(
                    $"An error occurred before removing invalid aliases. {e.BuildFullInformationText()}");
                return;
            }

            var aliases = await base.GetAll(null, false);
            var invalidAliases = aliases.GroupBy(a => a.Name).SelectMany(a => a.Skip(1));
            await RemoveRange(invalidAliases);
        }
    }
}