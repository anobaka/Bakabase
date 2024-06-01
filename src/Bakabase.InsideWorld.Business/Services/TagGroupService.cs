using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Db;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.DependencyInjection;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    [Obsolete]
    public class TagGroupService : BootstrapService
    {
        private readonly FullMemoryCacheResourceService<InsideWorldDbContext, TagGroup, int> _orm;
        private readonly ResourceTagMappingService _resourceMappingService;
        protected ResourceService ResourceService => GetRequiredService<ResourceService>();
        protected AliasService AliasService => GetRequiredService<AliasService>();
        public const int DefaultGroupId = TagGroupDto.DefaultGroupId;

        public TagGroupService(IServiceProvider serviceProvider,
            FullMemoryCacheResourceService<InsideWorldDbContext, TagGroup, int> orm,
            ResourceTagMappingService resourceMappingService) : base(serviceProvider)
        {
            _orm = orm;
            _resourceMappingService = resourceMappingService;
        }

        public async Task<BaseResponse> RemoveByKey(int id)
        {
            return await _orm.RemoveByKey(id);
        }

        public async Task<BaseResponse> UpdateByKey(int id, Action<TagGroup> modify)
        {
            return await _orm.UpdateByKey(id, modify);
        }

        protected TagService TagService => GetRequiredService<TagService>();

        public async Task<TagGroupDto[]> GetByKeys(int[] ids, TagGroupAdditionalItem additionalItems = 0)
        {
            var groups = await _orm.GetByKeys(ids);

            var dtoMap = groups.ToDictionary(a => a.Id, a => a.ToDto());
            if (ids.Contains(TagGroupDto.DefaultGroupId))
            {
                var defaultGroup = TagGroupDto.CreateDefault();
                dtoMap[DefaultGroupId] = defaultGroup;
            }

            await PopulateAdditionalItems(dtoMap, additionalItems);
            return dtoMap.Values.ToArray();
        }

        private async Task PopulateAdditionalItems(Dictionary<int, TagGroupDto> groups,
            TagGroupAdditionalItem additionalItems)
        {
            foreach (var item in SpecificEnumUtils<TagGroupAdditionalItem>.Values)
            {
                if (additionalItems.HasFlag(item))
                {
                    switch (item)
                    {
                        case TagGroupAdditionalItem.Tags:
                        {
                            var tags = (await TagService.GetAllEntities()).GroupBy(a => a.GroupId);

                            foreach (var ts in tags)
                            {
                                if (!groups.TryGetValue(ts.Key, out var group))
                                {
                                    if (!groups.TryGetValue(DefaultGroupId, out group))
                                    {
                                        groups[DefaultGroupId] = group = TagGroupDto.CreateDefault();
                                    }
                                }

                                group.Tags = group.Tags
                                    .Concat(ts.Select(a => a.ToDto(group.Name, group.PreferredAlias)!)).ToList();
                            }

                            break;
                        }
                        case TagGroupAdditionalItem.PreferredAlias:
                        {
                            var names = groups.Values.Select(a => a.Name).Where(a => a.IsNotEmpty()).ToHashSet();
                            if (names.Any())
                            {
                                var aliases = await AliasService.GetByNames(names, false, false);
                                foreach (var g in groups.Values)
                                {
                                    aliases.TryGetValue(g.Name, out var list);
                                    g.PreferredAlias = list?.FirstOrDefault()?.Name;
                                    foreach (var t in g.Tags)
                                    {
                                        t.GroupNamePreferredAlias = g.PreferredAlias;
                                    }
                                }
                            }

                            break;
                        }
                        case TagGroupAdditionalItem.TagNamePreferredAlias:
                        {
                            var names = groups.Values.SelectMany(a => a.Tags)
                                .Select(a => a.Name)
                                .ToHashSet();
                            if (names.Any())
                            {
                                var aliases = await AliasService.GetByNames(names, false, false);
                                foreach (var g in groups.Values)
                                {
                                    foreach (var tag in g.Tags)
                                    {
                                        aliases.TryGetValue(tag.Name, out var list);
                                        tag.PreferredAlias = list?.FirstOrDefault()?.Name;
                                    }
                                }
                            }

                            break;
                        }
                    }
                }
            }
        }

        public async Task<TagGroupDto[]> GetAll(TagGroupAdditionalItem additionalItems)
        {
            var groups = (await _orm.GetAll()).ToDictionary(a => a.Id, a => a.ToDto());
            var noGroupTagsCount = await TagService.Count(a => a.GroupId == 0);
            if (noGroupTagsCount > 0)
            {
                var defaultGroup = TagGroupDto.CreateDefault();
                groups[DefaultGroupId] = defaultGroup;
            }

            await PopulateAdditionalItems(groups, additionalItems);

            return groups.Values.ToArray();
        }

        public async Task<List<TagGroup>> AddRange(TagGroupAddRequestModel model)
        {
            var optimizedNames = model.Names.Where(a => a.IsNotEmpty()).ToArray();
            var exists =
                await _orm.GetAll(a => optimizedNames.Contains(a.Name, StringComparer.OrdinalIgnoreCase));
            var existNames = exists.Select(a => a.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var @new = optimizedNames.Where(a => !existNames.Contains(a)).Select(a => new TagGroup {Name = a})
                .ToList();
            @new = (await _orm.AddRange(@new)).Data;
            return @new.Concat(exists).ToList();
        }

        public async Task<BaseResponse> Update(int id, TagGroupUpdateRequestModel model)
        {
            if (model.Name.IsNotEmpty())
            {
                var otherSameNameGroup = await _orm.GetFirst(a => a.Id != id && a.Name == model.Name);
                if (otherSameNameGroup != null)
                {
                    throw new Exception($"There is another tag group named: {model.Name}");
                }
            }

            string prevName = null;

            var r = await _orm.UpdateByKey(id, a =>
            {
                prevName = a.Name;
                if (model.Name.IsNotEmpty())
                {
                    a.Name = model.Name;
                }

                if (model.Order.HasValue)
                {
                    a.Order = model.Order.Value;
                }
            });

            if (prevName != model.Name && model.Name.IsNotEmpty())
            {
                var tags = await TagService.GetAllEntities(t => t.GroupId == id);
                var tagIds = tags.Select(t => t.Id).ToHashSet().ToArray();
                var resourceIds = (await _resourceMappingService.GetAll(a => tagIds.Contains(a.TagId)))
                    .Select(a => a.ResourceId)
                    .Distinct().ToArray();
                await ResourceService.RunBatchSaveNfoBackgroundTask(resourceIds,
                    $"{nameof(TagGroupService)}:ChangeName:{id}:{prevName}->{model.Name}", true);
            }

            return r;
        }

        public async Task<BaseResponse> Sort(int[] ids)
        {
            var groups = (await _orm.GetByKeys(ids)).ToDictionary(t => t.Id, t => t);
            var changed = new List<TagGroup>();
            for (var i = 0; i < ids.Length; i++)
            {
                var id = ids[i];
                var newOrder = i + 1;
                if (groups.TryGetValue(id, out var t) && t.Order != newOrder)
                {
                    t.Order = newOrder;
                    changed.Add(t);
                }
            }

            return await _orm.UpdateRange(changed);
        }
    }
}