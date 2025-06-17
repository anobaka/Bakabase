using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.Input;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Modules.StandardValue.Models.View;
using Bakabase.Modules.StandardValue.Services;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.Input;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/custom-property")]
    public class CustomPropertyController(
        ICustomPropertyService service,
        ICustomPropertyValueService propertyValueService,
        IStandardValueService standardValueService,
        IStandardValueLocalizer standardValueLocalizer,
        IPropertyLocalizer propertyLocalizer
    )
        : Controller
    {
        [HttpGet("all")]
        [SwaggerOperation(OperationId = "GetAllCustomProperties")]
        public async Task<ListResponse<CustomPropertyViewModel>> GetAll(
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            var cps = await service.GetAll(null, additionalItems, false);
            var vms = cps.Select(c => c.ToViewModel(propertyLocalizer)).ToList();
            return new ListResponse<CustomPropertyViewModel>(vms);
        }

        [HttpGet("ids")]
        [SwaggerOperation(OperationId = "GetCustomPropertyByKeys")]
        public async Task<ListResponse<CustomPropertyViewModel>> GetByKeys(int[] ids,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            var cps = await service.GetByKeys(ids, additionalItems, false);
            var vms = cps.Select(c => c.ToViewModel(propertyLocalizer)).ToList();
            return new ListResponse<CustomPropertyViewModel>(vms);
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddCustomProperty")]
        public async Task<SingletonResponse<CustomPropertyViewModel>> Add([FromBody] CustomPropertyAddOrPutDto model)
        {
            return new SingletonResponse<CustomPropertyViewModel>(
                (await service.Add(model)).ToViewModel(propertyLocalizer));
        }

        [HttpPut("{id:int}")]
        [SwaggerOperation(OperationId = "PutCustomProperty")]
        public async Task<SingletonResponse<CustomPropertyViewModel>> Put(int id,
            [FromBody] CustomPropertyAddOrPutDto model)
        {
            return new((await service.Put(id, model)).ToViewModel(propertyLocalizer));
        }

        [HttpPut("order")]
        [SwaggerOperation(OperationId = "SortCustomProperties")]
        public async Task<BaseResponse> Sort([FromBody] IdBasedDataSortInputModel model)
        {
            await service.Sort(model.Ids);
            return BaseResponseBuilder.Ok;
        }

        [HttpDelete("{id:int}")]
        [SwaggerOperation(OperationId = "RemoveCustomProperty")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await service.RemoveByKey(id);
        }

        [HttpPost("{sourceCustomPropertyId:int}/{targetType}/conversion-preview")]
        [SwaggerOperation(OperationId = "PreviewCustomPropertyTypeConversion")]
        public async Task<SingletonResponse<CustomPropertyTypeConversionPreviewViewModel>> PreviewTypeConversion(
            int sourceCustomPropertyId, PropertyType targetType)
        {
            return new SingletonResponse<CustomPropertyTypeConversionPreviewViewModel>(
                await service.PreviewTypeConversion(sourceCustomPropertyId, targetType));
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>FromType - ToType - Rules</returns>
        [HttpGet("conversion-rule")]
        [SwaggerOperation(OperationId = "GetCustomPropertyConversionRules")]
        public async Task<SingletonResponse<
                Dictionary<int, Dictionary<int, List<StandardValueConversionRuleViewModel>>>>>
            GetConversionRules()
        {
            var customPropertyTypes = SpecificEnumUtils<PropertyType>.Values;
            var ruleMap = customPropertyTypes.ToDictionary(v => (int) v, v => customPropertyTypes.ToDictionary(
                d => (int) d,
                d =>
                {
                    var fromStdType = PropertyInternals.DescriptorMap[v].BizValueType;
                    var toStdType = PropertyInternals.DescriptorMap[d].BizValueType;
                    var rulesFlag = StandardValueInternals.ConversionRules[fromStdType][toStdType];
                    return SpecificEnumUtils<StandardValueConversionRule>.Values.Select(x =>
                    {
                        if (!rulesFlag.HasFlag(x))
                        {
                            return null;
                        }

                        return new StandardValueConversionRuleViewModel
                        {
                            Name = standardValueLocalizer.ConversionRuleName(x),
                            Description = standardValueLocalizer.ConversionRuleDescription(x),
                            Rule = x
                        };
                    }).OfType<StandardValueConversionRuleViewModel>().ToList();
                }));
            return new SingletonResponse<Dictionary<int, Dictionary<int, List<StandardValueConversionRuleViewModel>>>>(
                ruleMap);
        }

        [HttpPut("{id:int}/{type}")]
        [SwaggerOperation(OperationId = "ChangeCustomPropertyType")]
        public async Task<BaseResponse> ChangeType(int id, PropertyType type)
        {
            return await service.ChangeType(id, type);
        }

        // [HttpPut("{id:int}/options/adding-new-data-dynamically")]
        // [SwaggerOperation(OperationId = "EnableAddingNewDataDynamicallyForCustomProperty")]
        // public async Task<BaseResponse> EnableAddingNewDataDynamically(int id)
        // {
        //     return await service.EnableAddingNewDataDynamically(id);
        // }

        [HttpGet("{id:int}/value-usage")]
        [SwaggerOperation(OperationId = "GetCustomPropertyValueUsage")]
        public async Task<SingletonResponse<int>> GetValueUsage(int id, [FromQuery] string value)
        {
            var property = await service.GetByKey(id);
            if (!property.Type.IsReferenceValueType())
            {
                return new(data: 0);
            }

            var values = await propertyValueService.GetAll(x => x.PropertyId == id,
                CustomPropertyValueAdditionalItem.None, false);
            var psh = PropertyInternals.PropertySearchHandlerMap[property.Type];
            var count = values.Count(v => psh.IsMatch(v, property.Type == PropertyType.SingleChoice
                ? SearchOperation.Equals
                : SearchOperation.Contains, value));

            return new(data: count);
        }

        [HttpGet("type-conversion-overview")]
        [SwaggerOperation(OperationId = "TestCustomPropertyTypeConversion")]
        public async Task<SingletonResponse<CustomPropertyTypeConversionExampleViewModel>>
            CheckTypeConversionOverview()
        {
            var testData = StandardValueInternals.ExpectedConversions.SelectMany(x =>
                (x.Key.GetCompatibleCustomPropertyTypes() ?? []).SelectMany(cpt =>
                    x.Value.SelectMany(y => y.Value.Select(z => z.FromValue).Distinct().Select(a =>
                        new CustomPropertyTypeConversionExampleInputModel.T()
                        {
                            Type = cpt,
                            SerializedBizValue = a?.SerializeAsStandardValue(x.Key)
                        })))).ToList();

            var results = new CustomPropertyTypeConversionExampleViewModel {Results = []};

            foreach (var @in in testData)
            {
                var inPropertyDescriptor = PropertyInternals.DescriptorMap[@in.Type];

                var valueResult = new CustomPropertyTypeConversionExampleViewModel.Tin
                {
                    SerializedBizValue = @in.SerializedBizValue,
                    Type = @in.Type,
                    Outputs = [],
                    BizValueType = inPropertyDescriptor.BizValueType
                };

                var fakeInProperty = new Property(PropertyPool.Custom, 0, @in.Type);
                // fakeInProperty.SetAllowAddingNewDataDynamically(true);

                var inBizValue = @in.SerializedBizValue?.DeserializeAsStandardValue(inPropertyDescriptor.BizValueType);

                foreach (var outType in SpecificEnumUtils<PropertyType>.Values)
                {
                    if (@in.Type == PropertyType.Number && outType == PropertyType.SingleLineText)
                    {

                    }

                    var outBizType = outType.GetBizValueType();
                    var nv =
                        await standardValueService.Convert(inBizValue, fakeInProperty.Type.GetBizValueType(),
                            outBizType);

                    valueResult.Outputs.Add(new CustomPropertyTypeConversionExampleViewModel.Tout
                    {
                        SerializedBizValue = nv?.SerializeAsStandardValue(outBizType),
                        Type = outType,
                        BizValueType = outBizType
                    });
                }

                results.Results.Add(valueResult);
            }

            return new SingletonResponse<CustomPropertyTypeConversionExampleViewModel>(results);
        }
    }
}