using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Business.Components.Modules.CustomProperty;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Configurations;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Components;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models.Input;
using Bakabase.Modules.CustomProperty.Models.View;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Configurations;
using Bakabase.Modules.StandardValue.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
using Bakabase.Modules.StandardValue.Models.View;
using Bakabase.Modules.StandardValue.Services;
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
        ICustomPropertyDescriptors customPropertyDescriptors,
        IStandardValueService standardValueService,
        IStandardValueLocalizer standardValueLocalizer
    )
        : Controller
    {
        [HttpGet("all")]
        [SwaggerOperation(OperationId = "GetAllCustomProperties")]
        public async Task<ListResponse<CustomProperty>> GetAll(
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            return new ListResponse<CustomProperty>(await service.GetAll(null, additionalItems, false));
        }

        [HttpGet("ids")]
        [SwaggerOperation(OperationId = "GetCustomPropertyByKeys")]
        public async Task<ListResponse<CustomProperty>> GetByKeys(int[] ids,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            return new ListResponse<CustomProperty>(await service.GetByKeys(ids, additionalItems, false));
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddCustomProperty")]
        public async Task<SingletonResponse<CustomProperty>> Add([FromBody] CustomPropertyAddOrPutDto model)
        {
            return new SingletonResponse<CustomProperty>(await service.Add(model));
        }

        [HttpPut("{id:int}")]
        [SwaggerOperation(OperationId = "PutCustomProperty")]
        public async Task<SingletonResponse<CustomProperty>> Put(int id, [FromBody] CustomPropertyAddOrPutDto model)
        {
            return new(await service.Put(id, model));
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
            int sourceCustomPropertyId, CustomPropertyType targetType)
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
            var customPropertyTypes = SpecificEnumUtils<CustomPropertyType>.Values;
            var ruleMap = customPropertyTypes.ToDictionary(v => (int) v, v => customPropertyTypes.ToDictionary(
                d => (int) d,
                d =>
                {
                    var fromStdType = customPropertyDescriptors[(int) v].BizValueType;
                    var toStdType = customPropertyDescriptors[(int) d].BizValueType;
                    var rulesFlag = StandardValueOptions.ConversionRules[fromStdType][toStdType];
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
        public async Task<BaseResponse> ChangeType(int id, CustomPropertyType type)
        {
            return await service.ChangeType(id, type);
        }

        [HttpPut("{id:int}/options/adding-new-data-dynamically")]
        [SwaggerOperation(OperationId = "EnableAddingNewDataDynamicallyForCustomProperty")]
        public async Task<BaseResponse> EnableAddingNewDataDynamically(int id)
        {
            return await service.EnableAddingNewDataDynamically(id);
        }

        [HttpGet("{id:int}/value-usage")]
        [SwaggerOperation(OperationId = "GetCustomPropertyValueUsage")]
        public async Task<SingletonResponse<int>> GetValueUsage(int id, [FromQuery] string value)
        {
            var property = await service.GetByKey(id);
            if (!property.EnumType.IsReferenceValueType())
            {
                return new(data: 0);
            }

            var values = await propertyValueService.GetAll(x => x.PropertyId == id,
                CustomPropertyValueAdditionalItem.None, false);
            var pd = customPropertyDescriptors[property.Type];
            var count = values.Count(v => pd.IsMatch(v, new ResourceSearchFilter
            {
                DbValue = value.SerializeAsStandardValue(StandardValueType.String),
                Operation = property.EnumType == CustomPropertyType.SingleChoice
                    ? SearchOperation.Equals
                    : SearchOperation.Contains
            }));

            return new(data: count);
        }

        [HttpGet("type-conversion-overview")]
        [SwaggerOperation(OperationId = "TestCustomPropertyTypeConversion")]
        public async Task<SingletonResponse<CustomPropertyTypeConversionExampleViewModel>>
            CheckTypeConversionOverview()
        {
            var testData = StandardValueOptions.ExpectedConversions.SelectMany(x =>
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
                var inPropertyDescriptor = customPropertyDescriptors[(int) @in.Type];

                var valueResult = new CustomPropertyTypeConversionExampleViewModel.Tin
                {
                    SerializedBizValue = @in.SerializedBizValue,
                    Type = @in.Type,
                    Outputs = [],
                    BizValueType = inPropertyDescriptor.BizValueType
                };

                var fakeInProperty = inPropertyDescriptor.ToDomainModel(new Abstractions.Models.Db.CustomProperty()
                    {Type = (int) @in.Type})!;
                fakeInProperty.SetAllowAddingNewDataDynamically(true);

                var inBizValue = @in.SerializedBizValue?.DeserializeAsStandardValue(inPropertyDescriptor.BizValueType);

                foreach (var outType in SpecificEnumUtils<CustomPropertyType>.Values)
                {
                    if (@in.Type == CustomPropertyType.Number && outType == CustomPropertyType.SingleLineText)
                    {

                    }

                    var outBizType = outType.GetBizValueType();
                    var nv =
                        await standardValueService.Convert(inBizValue, fakeInProperty.BizValueType,
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