using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.RequestModels;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Extensions;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Components.Orm;
using Bootstrap.Models.ResponseModels;

namespace Bakabase.InsideWorld.Business.Services
{
    public class
        CustomPropertyValueService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.CustomPropertyValue, int>(serviceProvider), ICustomPropertyValueService
    {
        protected ICustomPropertyService CustomPropertyService => GetRequiredService<ICustomPropertyService>();

        public async Task<List<CustomPropertyValue>> GetAll(Expression<Func<Abstractions.Models.Db.CustomPropertyValue, bool>>? exp, CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var data = await GetAll(exp, returnCopy);
            return await ToDomainModels(data, additionalItems, returnCopy);
        }

        public Task<List<Abstractions.Models.Db.CustomPropertyValue>> GetAllDbModels(
            Expression<Func<Abstractions.Models.Db.CustomPropertyValue, bool>>? selector = null, bool returnCopy = true) =>
            base.GetAll(selector, returnCopy);

        protected async Task<List<CustomPropertyValue>> ToDomainModels(
            List<Abstractions.Models.Db.CustomPropertyValue> values,
            CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var propertyIds = values.Select(v => v.PropertyId).ToHashSet();
            var properties =
                await CustomPropertyService.GetAll(x => propertyIds.Contains(x.Id),
                    CustomPropertyAdditionalItem.None, returnCopy);
            var propertyMap = properties.ToDictionary(x => x.Id);
            var dtoList = values
                .Select(v => CustomPropertyExtensions.Descriptors[(CustomPropertyType)propertyMap[v.PropertyId].Type].BuildDomainValue(v)!)
                .ToList();

            foreach (var dto in dtoList)
            {
                dto.Property = propertyMap[dto.PropertyId];
            }

            return dtoList;
        }

        public async Task<BaseResponse> SetResourceValue(int resourceId, int propertyId,
            ResourceCustomPropertyValuePutRequestModel model)
        {
            var value = await GetFirst(x => x.ResourceId == resourceId && x.PropertyId == propertyId);
            if (value == null)
            {
                value = new Abstractions.Models.Db.CustomPropertyValue()
                {
                    ResourceId = resourceId,
                    PropertyId = propertyId,
                    Value = model.Value
                };
                return await Add(value);
            }
            else
            {
                value.Value = model.Value;
                return await Update(value);
            }
        }

        public async Task<BaseResponse> AddRange(IEnumerable<Abstractions.Models.Domain.CustomPropertyValue> values)
        {
            var dbModels = values.Select(v => Abstractions.Extensions.CustomPropertyExtensions.ToDbModel(v)!).ToList();
            await AddRange(dbModels);
            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> UpdateRange(IEnumerable<Abstractions.Models.Domain.CustomPropertyValue> values)
        {
            var dbModels = values.Select(v => Abstractions.Extensions.CustomPropertyExtensions.ToDbModel(v)!).ToList();
            await UpdateRange(dbModels);
            return BaseResponseBuilder.Ok;
        }
    }
}