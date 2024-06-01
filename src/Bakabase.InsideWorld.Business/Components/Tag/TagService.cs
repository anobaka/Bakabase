using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Db;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Services;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Components.Tag
{
    public class TagService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.Tag, int>(serviceProvider), ITagService
    {
        protected ITagResourceMappingService TagResourceMappingService =>
            GetRequiredService<ITagResourceMappingService>();


        public async Task SaveByResources(List<Models.Domain.Resource> resources)
        {
            var resourceIds = resources.Select(a => a.Id).Distinct().ToList();

            var dbResourceMappings = (await TagResourceMappingService.GetAll(x => resourceIds.Contains(x.ResourceId)))
                .GroupBy(d => d.ResourceId).ToDictionary(d => d.Key, d => d.ToList());

            var incomingBizTags = resources.ToDictionary(d => d.Id,
                d => d.Properties?.GetValueOrDefault(ResourcePropertyType.Reserved)?.Values
                    .SelectMany(
                        v => v.Values?.Select(x => (x.Scope, Tags: x.AsTags())).Where(x => x.Tags != null) ?? [])
                    .OfType<(int Scope, List<Abstractions.Models.Domain.Tag> Tags)>()
                    .ToList() ?? []);

            var dbTags = await GetAll(null, false);
            var incomingTags = incomingBizTags.SelectMany(x => x.Value.SelectMany(z => z.Tags)).ToList();

            var newTags = incomingTags.Except(dbTags, Abstractions.Models.Domain.Tag.BizComparer)
                .OfType<Abstractions.Models.Domain.Tag>().ToArray();
            var addedTags = (await AddRange(newTags.Select(x => x.ToDbModel()).ToList())).Data;
            dbTags.AddRange(addedTags);
            foreach (var it in incomingTags.Except(newTags))
            {
                var dbTag = dbTags.First(x => Abstractions.Models.Domain.Tag.BizComparer.Equals(x, it));
                it.Id = dbTag.Id;
            }

            var keyTagMap = dbTags.GroupBy(x => x.BuildKeyString()).ToDictionary(d => d.Key, d => d.ToList());

            var mappingIdsToDelete = dbResourceMappings.Where(dm => !resourceIds.Contains(dm.Key))
                .SelectMany(x => x.Value.Select(y => y.Id)).ToHashSet();

            var mappingsToAdd = new List<TagResourceMapping>();

            foreach (var (rId, scopeTags) in incomingBizTags)
            {
                var dbMappings = dbResourceMappings.GetValueOrDefault(rId);
                var incomingDbMappings = scopeTags.SelectMany(x => x.Tags.Select(a =>
                {
                    var dbTag = keyTagMap[a.BuildKeyString()]
                        .First(b => Abstractions.Models.Domain.Tag.BizComparer.Equals(a, b));
                    return new TagResourceMapping {ResourceId = rId, Scope = x.Scope, TagId = dbTag.Id};
                })).ToList();

                if (dbMappings != null)
                {
                    mappingIdsToDelete.UnionWith(dbMappings.Except(incomingDbMappings, TagResourceMapping.BizComparer)
                        .Select(d => d.Id));
                }

                var newMappings = incomingDbMappings.Except(dbMappings ?? [], TagResourceMapping.BizComparer).ToList();
                mappingsToAdd.AddRange(newMappings);
            }

            await TagResourceMappingService.RemoveByKeys(mappingIdsToDelete);
            await TagResourceMappingService.AddRange(mappingsToAdd);
        }
    }
}
