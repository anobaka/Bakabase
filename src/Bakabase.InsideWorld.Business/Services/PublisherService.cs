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
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Orm;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Services
{
    public class PublisherService
    {
        private readonly FullMemoryCacheResourceService<InsideWorldDbContext, Publisher, int> _orm;
        private readonly PublisherResourceMappingService _resourceMappingService;
        private readonly PublisherTagMappingService _publisherTagMappingService;
        private readonly TagService _tagService;
        private readonly AliasService _aliasService;

        public PublisherService(FullMemoryCacheResourceService<InsideWorldDbContext, Publisher, int> orm,
            AliasService aliasService, PublisherResourceMappingService resourceMappingService,
            PublisherTagMappingService publisherTagMappingService, TagService tagService)
        {
            _orm = orm;
            _aliasService = aliasService;
            _resourceMappingService = resourceMappingService;
            _publisherTagMappingService = publisherTagMappingService;
            _tagService = tagService;
        }

        public async Task<Dictionary<int, List<PublisherDto>>> GetByResourceIds(List<int> resourceIds)
        {
            var mappings = await _resourceMappingService.GetAll(a => resourceIds.Contains(a.ResourceId));
            var publisherIds = mappings.Select(a => a.PublisherId).Distinct().ToList();
            var publishers = await _orm.GetAll(a => publisherIds.Contains(a.Id));
            var publisherDtoList = publishers.Select(a => a.ToDto());
            var publisherDtoMap = publisherDtoList.ToDictionary(a => a.Id, a => a);
            var resourcePublishers = resourceIds.ToDictionary(a => a, a =>
            {
                if (a == 118481)
                {

                }

                var resourceMappings = mappings.Where(b => b.ResourceId == a).ToList();
                return MergeResourcePublishers(null, resourceMappings, publisherDtoMap);
            });
            return resourceIds.ToDictionary(a => a, a => resourcePublishers.TryGetValue(a, out var ps) ? ps : null);
        }

        public async Task<string[]> GetNamesByIds(int[] ids)
        {
            var data = await _orm.GetByKeys(ids, false);
            return data.Select(t => t.Name).ToArray();
        }

        private static List<PublisherDto> MergeResourcePublishers(PublisherDto parent,
            List<PublisherResourceMapping> mappings,
            Dictionary<int, PublisherDto> publishers)
        {
            var childrenPublisherIds = mappings.Where(a => parent?.Id == a.ParentPublisherId).Select(a => a.PublisherId)
                .ToList();
            var childrenPublishers = childrenPublisherIds.Select(a => publishers[a] with { }).ToList();

            // Debug
            var samePublishers = childrenPublishers.Where(a => a.Id == parent?.Id).ToList();
            if (samePublishers.Any())
            {

            }

            foreach (var cp in childrenPublishers)
            {
                // Avoid loop
                var validMappings = mappings.ToList();
                if (parent?.Id == cp.Id)
                {
                    validMappings.RemoveAll(a => a.ParentPublisherId == a.PublisherId && a.PublisherId == cp.Id);
                }

                cp.SubPublishers = MergeResourcePublishers(cp, validMappings, publishers);
            }

            return childrenPublishers;
        }

        public async Task<SimpleRangeAddResult<PublisherDto>> AddAll(List<PublisherDto> publishers)
        {
            var allPublishers = publishers.Extract();
            var publisherNames = allPublishers.Select(a => a.Name).Distinct().ToList();
            var existPublishers = await _orm.GetAll(a => publisherNames.Contains(a.Name));
            var existNames = existPublishers.Select(a => a.Name).ToList();
            var toBeAddedPublishers =
                publisherNames.Except(existNames).Select(a => new Publisher {Name = a}).ToList();
            var newPublishers = await _orm.AddRange(toBeAddedPublishers);
            var result = existPublishers.Concat(newPublishers.Data).ToList().ToDictionary(a => a.Name, a => a.ToDto());
            // // debug
            // var dupNames = existPublishers.GroupBy(a => a.Name).Where(a => a.Count() > 1).ToList();
            // if (dupNames.Any())
            // {
            //     Debugger.Break();
            // }
            return new SimpleRangeAddResult<PublisherDto>
            {
                Data = result,
                AddedCount = newPublishers.Data.Count,
                ExistingCount = existPublishers.Count
            };
        }

        /// <summary>
        /// todo: optimize
        /// </summary>
        /// <param name="names"></param>
        /// <param name="fuzzy"></param>
        /// <returns></returns>
        public async Task<List<int>> GetAllIdsByNames(HashSet<string> names, bool fuzzy)
        {
            Expression<Func<Publisher, bool>> exp =
                fuzzy ? a => names.Any(b => a.Name.Contains(b)) : a => names.Contains(a.Name);
            var publishers = await _orm.GetAll(exp);
            return publishers.Select(a => a.Id).ToList();
        }

        public async Task<List<int>> GetAllIdsByRegexs(HashSet<string> regexStrings)
        {
            var allPublishers = await _orm.GetAll();
            var regexs = regexStrings.Select(t => new Regex(t)).ToArray();
            var publisherIds = allPublishers.Where(t => regexs.Any(r => r.IsMatch(t.Name))).Select(t => t.Id)
                .ToList();
            return publisherIds;
        }

        public async Task Update(int id, PublisherUpdateModel model)
        {
            var p = await _orm.GetByKey(id);
            if (model.Favorite.HasValue)
            {
                p.Favorite = model.Favorite.Value;
            }

            if (model.Rank.HasValue)
            {
                p.Rank = model.Rank.Value;
            }

            if (model.TagIds != null)
            {
                var exists = (await _publisherTagMappingService.GetAll(a => a.PublisherId == id)).ToList();
                var toBeDeleted = exists.Where(a => !model.TagIds.Contains(a.TagId)).ToList();
                var toBeAdded = model.TagIds.Where(a => exists.All(b => b.TagId != a)).Select(a =>
                    new PublisherTagMapping
                    {
                        PublisherId = id,
                        TagId = a
                    }).ToList();
                await _publisherTagMappingService.RemoveRange(toBeDeleted);
                await _publisherTagMappingService.AddRange(toBeAdded);
            }

            if (model.Name.IsNotEmpty())
            {
                p.Name = model.Name;
            }

            await _orm.Update(p);
        }
    }
}