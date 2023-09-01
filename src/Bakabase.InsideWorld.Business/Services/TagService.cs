using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Tasks;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.InsideWorld.Models.ResponseModels;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.Extensions.Logging;
using NPOI.SS.Formula.Functions;

namespace Bakabase.InsideWorld.Business.Services
{
    public class TagService : BootstrapService
    {
        private readonly FullMemoryCacheResourceService<InsideWorldDbContext, Tag, int> _orm;
        private ResourceService ResourceService => GetRequiredService<ResourceService>();
        private TagGroupService TagGroupService => GetRequiredService<TagGroupService>();
        private AliasService AliasService => GetRequiredService<AliasService>();
        private ResourceCategoryService ResourceCategoryService => GetRequiredService<ResourceCategoryService>();
        private BackgroundTaskManager BackgroundTaskManager => GetRequiredService<BackgroundTaskManager>();
        private ResourceTagMappingService ResourceTagMappingService => GetRequiredService<ResourceTagMappingService>();
        private ILogger<TagService> _logger;

        public TagService(IServiceProvider serviceProvider,
            FullMemoryCacheResourceService<InsideWorldDbContext, Tag, int> orm, ILogger<TagService> logger) : base(
            serviceProvider)
        {
            _orm = orm;
            _logger = logger;
        }

        public async Task<Tag[]> GetEntitiesByKeys(IEnumerable<int> ids) => await _orm.GetByKeys(ids);

        public async Task<TagDto[]> GetByKeys(IEnumerable<int> ids, TagAdditionalItem additionalItems)
        {
            var tags = await GetEntitiesByKeys(ids);
            return await ToDto(tags, additionalItems);
        }

        private async Task<TagDto[]> ToDto(Tag[] tags, TagAdditionalItem additionalItems)
        {
            var tagDtoList = tags.Select(a => a.ToDto(null, null)!).ToArray();
            if (additionalItems > 0)
            {
                foreach (var item in SpecificEnumUtils<TagAdditionalItem>.Values)
                {
                    if (additionalItems.HasFlag(item))
                    {
                        switch (item)
                        {
                            case TagAdditionalItem.GroupName:
                            {
                                var tagGroupIds = tags.Select(t => t.GroupId).Distinct().ToArray();
                                var groups =
                                    (await TagGroupService.GetByKeys(tagGroupIds)).ToDictionary(t => t.Id, t => t);
                                foreach (var t in tagDtoList)
                                {
                                    t.GroupName = groups.TryGetValue(t.GroupId, out var g) ? g.Name : null;
                                }

                                break;
                            }
                            case TagAdditionalItem.PreferredAlias:
                            {
                                var names = tagDtoList.Select(a => a.Name).Where(a => a.IsNotEmpty()).ToHashSet();
                                var aliases = await AliasService.GetByNames(names, false, false);
                                foreach (var t in tagDtoList)
                                {
                                    if (aliases.TryGetValue(t.Name, out var list))
                                    {
                                        t.PreferredAlias = list?.FirstOrDefault()?.Name ?? t.Name;
                                    }
                                }

                                break;
                            }
                        }
                    }
                }
            }

            return tagDtoList;
        }

        public async Task<TagDto[]> GetAll(TagAdditionalItem additionalItems)
        {
            var tags = await _orm.GetAll();
            return await ToDto(tags.ToArray(), additionalItems);
        }

        public async Task<SearchResponse<TagDto>> Search(TagSearchRequestModel model)
        {
            Func<Tag, bool> exp = a => (!model.GroupId.HasValue || model.GroupId == a.GroupId);
            var tags = await _orm.Search(exp, model.PageIndex, model.PageSize, returnCopy: false);
            var dtoList = await ToDto(tags.Data.ToArray(), model.AdditionalItems);
            return model.BuildResponse(dtoList, tags.TotalCount);
        }

        public async Task<int> Count(Func<Tag, bool> exp)
        {
            return await _orm.Count(exp);
        }

        public async Task<Dictionary<int, List<TagDto>>> GetByResourceIds(List<int> resourceIds)
        {
            var mappings = await ResourceTagMappingService.GetAll(a => resourceIds.Contains(a.ResourceId));
            var tagIds = mappings.Select(a => a.TagId).Distinct().ToList();
            var resourceIdTagIdsMap = mappings.GroupBy(a => a.ResourceId)
                .ToDictionary(a => a.Key, a => a.Select(b => b.TagId).ToList());
            var tagEntities = await _orm.GetAll(a => tagIds.Contains(a.Id));
            var groupIds = tagEntities.Select(t => t.GroupId).Distinct().ToArray();
            var groups = await TagGroupService.GetByKeys(groupIds);
            var groupMap = groups.ToDictionary(t => t.Id, t => t);
            var tags = (tagEntities).ToDictionary(a => a.Id,
                a =>
                {
                    var group = groupMap.GetValueOrDefault(a.GroupId);
                    return a.ToDto(group?.Name, group?.PreferredAlias);
                });
            return resourceIds.ToDictionary(a => a,
                a => resourceIdTagIdsMap.TryGetValue(a, out var ts)
                    ? ts.Select(b => (tags.TryGetValue(b, out var dto), dto)).Where(b => b.Item1).Select(b => b.dto)
                        .ToList()
                    : null);
        }

        public async Task<List<Tag>>
            GetAllEntities(Expression<Func<Tag, bool>> selector = null, bool returnCopy = true) =>
            await _orm.GetAll(selector, returnCopy);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model"></param>
        /// <param name="autoMatchGroupForTagsWithoutGroup"></param>
        /// <returns></returns>
        public async Task<List<TagDto>> AddRangeByNameAndGroupName(TagDto[] model,
            bool autoMatchGroupForTagsWithoutGroup)
        {
            var dict = model.Where(a => a.Name.IsNotEmpty()).Distinct(TagGroupNameAndNameEqualityComparer.Instance)
                .GroupBy(t => t.GroupName ?? string.Empty)
                .ToDictionary(a => a.Key, a => a.Select(b => b.Name).ToArray());
            return await AddRange(dict, autoMatchGroupForTagsWithoutGroup);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model">Group Name - Tag Names</param>
        /// <param name="autoMatchGroupForTagsWithoutGroup"></param>
        /// <returns></returns>
        public async Task<List<TagDto>> AddRange(Dictionary<string, string[]> model,
            bool autoMatchGroupForTagsWithoutGroup)
        {
            var notEmptyGroupNames = model.Keys.Where(a => a.IsNotEmpty()).ToArray();
            var tagGroups = await TagGroupService.AddRange(new TagGroupAddRequestModel {Names = notEmptyGroupNames});
            var idTagGroupMap = tagGroups.ToDictionary(t => t.Id, t => t);
            var tagGroupIdMap = tagGroups.GroupBy(t => t.Name).ToDictionary(t => t.Key, t => t.FirstOrDefault()!.Id);
            tagGroupIdMap[string.Empty] = 0;
            var groupIdAndTagNames = model.ToDictionary(t => tagGroupIdMap[t.Key],
                t => t.Value.Where(a => a.IsNotEmpty()).ToArray());
            var tags = await AddRange(groupIdAndTagNames, autoMatchGroupForTagsWithoutGroup);
            return tags.Select(t =>
                {
                    var group = idTagGroupMap.GetValueOrDefault(t.GroupId);
                    return t.ToDto(group?.Name, null)!;
                })
                .ToList();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="model">Group Id - Tag Names</param>
        /// <param name="autoMatchGroupForTagsWithoutGroup"></param>
        /// <returns></returns>
        public async Task<List<Tag>> AddRange(Dictionary<int, string[]> model, bool autoMatchGroupForTagsWithoutGroup)
        {
            if (model?.Any() != true)
            {
                return new List<Tag>();
            }

            var optimizedModel = model.Select(a =>
                    (GroupId: a.Key, TagNames: a.Value.Distinct().Where(b => b.IsNotEmpty()).ToArray()))
                .Where(a => a.TagNames.Any())
                .ToDictionary(a => a.GroupId, a => a.TagNames.ToList());

            if (autoMatchGroupForTagsWithoutGroup &&
                optimizedModel.TryGetValue(TagGroupService.DefaultGroupId, out var noGroupNames))
            {
                var existTags = (await _orm.GetAll(t => noGroupNames.Contains(t.Name))).GroupBy(t => t.Name)
                    .ToDictionary(t => t.Key, t => t.FirstOrDefault());
                optimizedModel.Remove(TagGroupService.DefaultGroupId);
                foreach (var n in noGroupNames)
                {
                    var groupId = TagGroupService.DefaultGroupId;
                    if (existTags.TryGetValue(n, out var tag))
                    {
                        groupId = tag!.GroupId;
                    }

                    if (!optimizedModel.TryGetValue(groupId, out var names))
                    {
                        optimizedModel[groupId] = names = new List<string>();
                    }

                    names.Add(n);
                }
            }

            var allTags = await _orm.GetAll();
            var newTags = optimizedModel.SelectMany(t => t.Value.Where(name =>
                    allTags.All(a => !a.Name.Equals(name, StringComparison.OrdinalIgnoreCase) || a.GroupId != t.Key))
                .Select(name => new Tag {Name = name, GroupId = t.Key})).ToList();
            newTags = (await _orm.AddRange(newTags)).Data;
            var exists = allTags.Where(t =>
                optimizedModel.Any(
                    a => a.Key == t.GroupId && a.Value.Contains(t.Name, StringComparer.OrdinalIgnoreCase)));
            return newTags.Concat(exists).ToList();
        }

        public async Task<BaseResponse> Update(int id, TagUpdateRequestModel model)
        {
            var prevGroupId = 0;
            var r = await _orm.UpdateByKey(id, a =>
            {
                if (model.Color.IsNotEmpty())
                {
                    a.Color = model.Color;
                }

                if (model.GroupId.HasValue && a.GroupId != model.GroupId)
                {
                    prevGroupId = a.GroupId;

                    a.GroupId = model.GroupId.Value;
                }

                if (model.Order.HasValue)
                {
                    a.Order = model.Order.Value;
                }
            });

            if (model.GroupId.HasValue && model.GroupId != prevGroupId)
            {
                var resourceIds = (await ResourceTagMappingService.GetAll(a => a.TagId == id)).Select(a => a.ResourceId)
                    .Distinct().ToArray();
                await ResourceService.RunBatchSaveNfoBackgroundTask(resourceIds,
                    $"{nameof(TagService)}:ChangeGroup:{id}:{prevGroupId}->{model.GroupId!.Value}", true);
            }

            return r;
        }

        public async Task<BaseResponse> UpdateName(int id, string name)
        {
            var otherSameNameTag = await _orm.GetFirst(a => a.Id != id && a.Name == name);
            if (otherSameNameTag != null)
            {
                throw new Exception($"There is another tag named: {name}");
            }

            var tagName = (await _orm.GetByKey(id, true))?.Name;
            if (tagName != name)
            {
                var r = await _orm.UpdateByKey(id, a => { a.Name = name; });
                var resourceIds = (await ResourceTagMappingService.GetAll(a => a.TagId == id)).Select(a => a.ResourceId)
                    .Distinct().ToArray();
                await ResourceService.RunBatchSaveNfoBackgroundTask(resourceIds,
                    $"{nameof(TagService)}:Rename:{tagName}->{name}", true);

                return r;
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task<ListResponse<TagMoveChanges>> Move(int id, TagMoveRequestModel model)
        {
            if (model.TargetGroupId.HasValue || model.TargetTagId.HasValue)
            {
                var tag = await _orm.GetByKey(id, true);

                if (model.TargetTagId.HasValue)
                {
                    var targetTag = await _orm.GetByKey(model.TargetTagId.Value);
                    if (targetTag != null)
                    {
                        var allChanges = new Dictionary<int, TagMoveChanges>();

                        var groupId = targetTag.GroupId;
                        var groupTags = (await _orm.GetAll(a => a.GroupId == groupId, true)).OrderBy(a => a.Order)
                            .ToList();
                        var targetTagIdx = groupTags.FindIndex(a => a.Id == targetTag.Id);
                        var groupChanged = tag.GroupId != targetTag.GroupId;
                        if (groupChanged)
                        {
                            tag.GroupId = targetTag.GroupId;
                            allChanges[tag.Id] = new TagMoveChanges
                            {
                                GroupId = targetTag.GroupId
                            };
                        }
                        else
                        {
                            groupTags.RemoveAll(a => a.Id == tag.Id);
                        }

                        groupTags.Insert(targetTagIdx, tag);
                        for (var i = 0; i < groupTags.Count; i++)
                        {
                            var t = groupTags[i];
                            if (t.Order != i || (groupChanged && t == tag))
                            {
                                if (!allChanges.TryGetValue(t.Id, out var cs))
                                {
                                    allChanges[t.Id] = cs = new TagMoveChanges();
                                }

                                cs.Order = i;
                                groupTags[i].Order = i;
                            }
                        }

                        var changedTags = groupTags.Where(a => allChanges.ContainsKey(a.Id)).ToList();

                        await _orm.UpdateRange(changedTags);

                        foreach (var (tId, c) in allChanges)
                        {
                            c.TagId = tId;
                        }

                        return new ListResponse<TagMoveChanges>(allChanges.Values);
                    }
                }
                else
                {
                    var allChanges = new Dictionary<int, TagMoveChanges>();

                    // put tag at the tail of group.
                    var groupTags = (await _orm.GetAll(a => a.GroupId == model.TargetGroupId.Value, true))
                        .OrderBy(a => a.Order)
                        .ToList();
                    groupTags.RemoveAll(a => a.Id == tag.Id);
                    var groupChanged = tag.GroupId != model.TargetGroupId!;
                    if (groupChanged)
                    {
                        tag.GroupId = model.TargetGroupId.Value;
                        allChanges[tag.Id] = new TagMoveChanges
                        {
                            GroupId = model.TargetGroupId.Value
                        };
                    }

                    groupTags.Add(tag);
                    for (var i = 0; i < groupTags.Count; i++)
                    {
                        var t = groupTags[i];
                        if (t.Order != i || (groupChanged && t == tag))
                        {
                            if (!allChanges.TryGetValue(t.Id, out var cs))
                            {
                                allChanges[t.Id] = cs = new TagMoveChanges();
                            }

                            cs.Order = i;
                            groupTags[i].Order = i;
                        }
                    }

                    var changedTags = groupTags.Where(a => allChanges.ContainsKey(a.Id)).ToList();

                    await _orm.UpdateRange(changedTags);


                    foreach (var (tId, c) in allChanges)
                    {
                        c.TagId = tId;
                    }

                    return new ListResponse<TagMoveChanges>(allChanges.Values);
                }
            }

            return ListResponseBuilder<TagMoveChanges>.BuildBadRequest(
                $"At least one of {nameof(model.TargetGroupId)} and {nameof(model.TargetTagId)} can not be null.");
        }

        public async Task<BaseResponse> RemoveByKey(int id)
        {
            var tag = await _orm.GetByKey(id);
            if (tag == null)
            {
                return BaseResponseBuilder.Ok;
            }

            await ResourceTagMappingService.RemoveAll(a => a.TagId == id);
            await _orm.RemoveByKey(id);

            var name = tag.Name;
            var resourceIds = (await ResourceTagMappingService.GetAll(a => a.TagId == id))
                .Select(a => a.ResourceId)
                .Distinct().ToArray();
            if (name.IsNotEmpty())
            {
                await ResourceService.RunBatchSaveNfoBackgroundTask(resourceIds, $"{nameof(TagService)}:Remove:{name}",
                    true);
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task RemoveInvalid()
        {
            try
            {
                // Check exists
                var count = await _orm.Count();
            }
            catch (Exception e)
            {
                _logger.LogWarning(
                    $"An error occurred before removing invalid tags. {e.BuildFullInformationText()}");
                return;
            }

            var tags = await _orm.GetAll(null, false);
            var invalidTags = tags.Where(a => a.Name.IsNullOrEmpty()).ToArray();
            await _orm.RemoveRange(invalidTags);

            var duplicatedTags = tags.GroupBy(a => $"{a.GroupId}|{a.Name}").Where(a => a.Count() > 1).ToArray();

            var idChanges = duplicatedTags
                .SelectMany(a => a.Skip(1).Select(b => (InvalidId: b.Id, TargetId: a.FirstOrDefault()!.Id)))
                .ToDictionary(a => a.InvalidId, a => a.TargetId);

            var invalidIds = idChanges.Keys.ToArray();

            var resourceTagMappings = await ResourceTagMappingService.GetAll(a => invalidIds.Contains(a.TagId));
            foreach (var m in resourceTagMappings)
            {
                if (idChanges.TryGetValue(m.TagId, out var targetId))
                {
                    m.TagId = targetId;
                }
            }

            await ResourceTagMappingService.UpdateRange(resourceTagMappings);

            await _orm.RemoveByKeys(invalidIds);
        }

        public async Task<BaseResponse> RemoveByKeys(int[] ids)
        {
            await ResourceTagMappingService.RemoveAll(a => ids.Contains(a.TagId));
            var tags = await GetByKeys(ids, TagAdditionalItem.GroupName);

            var names = tags.Select(a => a.DisplayName).ToArray();
            var resourceIds = (await ResourceTagMappingService.GetAll(a => ids.Contains(a.TagId)))
                .Select(a => a.ResourceId)
                .Distinct().ToArray();
            if (names.IsNotEmpty() && resourceIds.Any())
            {
                await ResourceService.RunBatchSaveNfoBackgroundTask(resourceIds,
                    $"{nameof(TagService)}:Remove:{string.Join(',', names)}", true);
            }

            await _orm.RemoveByKeys(ids);

            return BaseResponseBuilder.Ok;
        }
    }
}