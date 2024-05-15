using System.Linq.Expressions;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.Enhancer.Abstractions.Services;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm.Infrastructures;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Modules.Enhancer.Services
{
    public abstract class AbstractEnhancementService<TDbContext>(IServiceProvider serviceProvider)
        : ResourceService<TDbContext, Bakabase.Abstractions.Models.Db.Enhancement, int>(serviceProvider),
            IEnhancementService where TDbContext : DbContext
    {
        protected ICustomPropertyValueService CustomPropertyValueService =>
            GetRequiredService<ICustomPropertyValueService>();

        protected IEnumerable<EnhancerDescriptor> EnhancerDescriptors =>
            GetRequiredService<IEnumerable<EnhancerDescriptor>>();

        public async Task<List<Enhancement>> GetAll(
            Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>>? exp,
            EnhancementAdditionalItem additionalItem = EnhancementAdditionalItem.None)
        {
            var data = await base.GetAll(exp);
            var doModels = data.Select(d => d.ToDomainModel()!).ToList();
            await Populate(doModels, additionalItem);
            return doModels;
        }

        protected async Task Populate(List<Enhancement> data,
            EnhancementAdditionalItem additionalItem)
        {
            foreach (var ai in SpecificEnumUtils<EnhancementAdditionalItem>.Values)
            {
                if (additionalItem.HasFlag(ai))
                {
                    switch (ai)
                    {
                        case EnhancementAdditionalItem.None:
                            break;
                        case EnhancementAdditionalItem.GeneratedCustomPropertyValue:
                        {
                            var cpvIds = data.Select(d => d.CustomPropertyValueId).ToHashSet();
                            var cpValuesMap = (await CustomPropertyValueService.GetAll(x => cpvIds.Contains(x.Id),
                                CustomPropertyValueAdditionalItem.None, false)).ToDictionary(d => d.Id, d => d);
                            foreach (var d in data)
                            {
                                d.CustomPropertyValue = cpValuesMap.GetValueOrDefault(d.CustomPropertyValueId);
                            }

                            break;
                        }
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }

        public async Task AddRange(List<Enhancement> enhancements)
        {
            var dbValuesMap = enhancements.ToDictionary(e => e.ToDbModel()!, e => e);
            await base.AddRange(dbValuesMap.Keys.ToList());
            foreach (var (k, v) in dbValuesMap)
            {
                v.Id = k.Id;
            }
        }

        public async Task UpdateRange(List<Enhancement> enhancements)
        {
            var dbValues = enhancements.Select(e => e.ToDbModel()!);
            await base.UpdateRange(dbValues);
        }

        public async Task<BaseResponse> RemoveAll(
            Expression<Func<Bakabase.Abstractions.Models.Db.Enhancement, bool>> selector,
            bool removeGeneratedCustomPropertyValues)
        {
            var enhancements = await base.GetAll(selector);
            var enhancerDescriptorMap = EnhancerDescriptors.ToDictionary(d => d.Id, d => d);
            var keys = enhancements
                .Select(e => (Scope: enhancerDescriptorMap.GetValueOrDefault(e.EnhancerId)?.CustomPropertyScope,
                    e.ResourceId)).Where(x => x.Scope.HasValue).ToList();
            var scopes = keys.Select(d => d.Scope).ToHashSet();
            var resourceIds = keys.Select(d => d.ResourceId).ToHashSet();
            await using var tran = await DbContext.Database.BeginTransactionAsync();
            await base.RemoveRange(enhancements);
            if (removeGeneratedCustomPropertyValues)
            {
                // avoid query entire table
                var cpValues = await CustomPropertyValueService.GetAllDbModels(x =>
                    scopes.Contains(x.Scope) && resourceIds.Contains(x.ResourceId));
                cpValues = cpValues.Where(v => keys.Any(k => k.Scope == v.Scope && k.ResourceId == v.ResourceId))
                    .ToList();
                await CustomPropertyValueService.RemoveRange(cpValues);
            }

            tran.CommitAsync();
            return BaseResponseBuilder.Ok;
        }
    }
}