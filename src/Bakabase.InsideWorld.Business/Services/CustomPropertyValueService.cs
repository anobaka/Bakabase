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
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Services
{
    public class
        CustomPropertyValueService(IServiceProvider serviceProvider)
        : FullMemoryCacheResourceService<InsideWorldDbContext, Abstractions.Models.Db.CustomPropertyValue, int>(
            serviceProvider), ICustomPropertyValueService
    {
        protected ICustomPropertyService CustomPropertyService => GetRequiredService<ICustomPropertyService>();
        protected ConversionService ConversionService => GetRequiredService<ConversionService>();

        public async Task<List<CustomPropertyValue>> GetAll(
            Expression<Func<Abstractions.Models.Db.CustomPropertyValue, bool>>? exp,
            CustomPropertyValueAdditionalItem additionalItems, bool returnCopy)
        {
            var data = await GetAll(exp, returnCopy);
            return await ToDomainModels(data, additionalItems, returnCopy);
        }

        public Task<List<Abstractions.Models.Db.CustomPropertyValue>> GetAllDbModels(
            Expression<Func<Abstractions.Models.Db.CustomPropertyValue, bool>>? selector = null,
            bool returnCopy = true) =>
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
                .Select(v =>
                    CustomPropertyExtensions.Descriptors[(CustomPropertyType) propertyMap[v.PropertyId].Type]
                        .BuildDomainValue(v)!)
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

        public async Task<BaseResponse> AddRange(IEnumerable<CustomPropertyValue> values)
        {
            var dbModelsMap = values.ToDictionary(v => Abstractions.Extensions.CustomPropertyExtensions.ToDbModel(v)!,
                v => v);
            await AddRange(dbModelsMap.Keys.ToList());
            foreach (var (k, v) in dbModelsMap)
            {
                v.Id = k.Id;
            }

            return BaseResponseBuilder.Ok;
        }

        public async Task<BaseResponse> UpdateRange(IEnumerable<CustomPropertyValue> values)
        {
            var dbModels = values.Select(v => Abstractions.Extensions.CustomPropertyExtensions.ToDbModel(v)!).ToList();
            await UpdateRange(dbModels);
            return BaseResponseBuilder.Ok;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data">ResourceId - PropertyId - String values</param>
        /// <returns></returns>
        public async Task SetListStringValuesToResources(
            Dictionary<int, Dictionary<int, (int Scope, string[] ListStringValue)>> data)
        {
            var propertyIds = data.SelectMany(d => d.Value.Keys).ToHashSet();
            var propertyMap =
                (await CustomPropertyService.GetByKeys(propertyIds, CustomPropertyAdditionalItem.None)).ToDictionary(
                    d => d.Id, d => d);

            // // ResourceId - PropertyId - Scope - Value
            // var dbValueMap = (await GetAll(x => data.Keys.Contains(x.ResourceId))).GroupBy(d => d.ResourceId)
            //     .ToDictionary(d => d.Key,
            //         d => d.GroupBy(x => x.PropertyId)
            //             .ToDictionary(c => c.Key, c => c.ToDictionary(e => e.Scope, e => e)));
            //
            // var valuesToAdd = new List<Abstractions.Models.Db.CustomPropertyValue>();
            // var valuesToUpdate = new List<Abstractions.Models.Db.CustomPropertyValue>();

            foreach (var (resourceId, propertyValues) in data)
            {
                foreach (var (propertyId, (scope, strings)) in propertyValues)
                {
                    var property = propertyMap[propertyId];

                    var dbValue = dbValueMap.GetValueOrDefault(resourceId)?.GetValueOrDefault(propertyId)
                        ?.GetValueOrDefault(scope);
                    if (dbValue == null)
                    {
                        dbValue = new Abstractions.Models.Db.CustomPropertyValue()
                        {
                            ResourceId = resourceId,
                            PropertyId = propertyId,
                            Scope = scope
                        };
                        valuesToAdd.Add(dbValue);
                    }
                    else
                    {
                        valuesToUpdate.Add(dbValue);
                    }

                    dbValue.Value = JsonConvert.SerializeObject(
                        (await ConversionService.CheckConversionLoss(strings, StandardValueType.ListString,
                            property.ValueType)).NewValue);
                }
            }

            await AddRange(valuesToAdd);
            await UpdateRange(valuesToUpdate);
        }
    }
}