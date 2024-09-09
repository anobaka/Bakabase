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
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Components;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.CustomProperty.Models.Input;
using Bakabase.Modules.CustomProperty.Models.View;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Services;
using Bakabase.Modules.StandardValue.Extensions;
using Bakabase.Modules.StandardValue.Models.Domain;
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
        IStandardValueService standardValueService
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

        [HttpPost("{id:int}/{type}/loss")]
        [SwaggerOperation(OperationId = "CalculateCustomPropertyTypeConversionLoss")]
        public async Task<SingletonResponse<CustomPropertyTypeConversionLossViewModel>> CalculateTypeConversionLoss(
            int id,
            CustomPropertyType type)
        {
            return new SingletonResponse<CustomPropertyTypeConversionLossViewModel>(
                await service.CalculateTypeConversionLoss(id, type));
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
        public async Task<SingletonResponse<CustomPropertyTypeConversionOverviewViewModel>>
            CheckTypeConversionOverview()
        {
            var testData = new List<(CustomPropertyType Type, IStandardValueBuilder? ValueBuilder)>
            {
                (CustomPropertyType.SingleLineText, new StringValueBuilder("aaa")),
                (CustomPropertyType.SingleLineText, new StringValueBuilder("4.5")),
                (CustomPropertyType.MultilineText, new StringValueBuilder("aaa\nbbb<br/>ccc")),
                (CustomPropertyType.MultilineText, new StringValueBuilder("4.5")),
                (CustomPropertyType.SingleChoice, new StringValueBuilder("aaa")),
                (CustomPropertyType.SingleChoice, new StringValueBuilder("4.5")),
                (CustomPropertyType.MultipleChoice, new ListStringValueBuilder(["aaa", "bbb"])),
                (CustomPropertyType.MultipleChoice, new ListStringValueBuilder(["4.5", "aaa"])),
                (CustomPropertyType.Number, new DecimalValueBuilder(4)),
                (CustomPropertyType.Percentage, new DecimalValueBuilder(40)),
                (CustomPropertyType.Rating, new DecimalValueBuilder(4.5m)),
                (CustomPropertyType.Boolean, new BooleanValueBuilder(true)),
                (CustomPropertyType.Link, new LinkValueBuilder(new LinkValue {Text = "text", Url = "http://a.com"})),
                (CustomPropertyType.Attachment, new ListStringValueBuilder(["C:/1.png", "D:/2.pdf"])),
                (CustomPropertyType.Date, new DateTimeValueBuilder(DateTime.Today)),
                (CustomPropertyType.DateTime, new DateTimeValueBuilder(DateTime.Now)),
                (CustomPropertyType.Time, new TimeValueBuilder(DateTime.Now.TimeOfDay)),
                (CustomPropertyType.Formula, new StringValueBuilder("fff")),
                (CustomPropertyType.Multilevel,
                    new ListListStringValueBuilder([["aaa", "bbb", "ccc"], ["aaa", "ddd", "eee"]])),
                (CustomPropertyType.Multilevel,
                    new ListListStringValueBuilder([["4.5", "4.6", "4.7"], ["aaa", "bbb"]])),
                (CustomPropertyType.Tags,
                    new ListTagValueBuilder([new TagValue("Group1", "Name1"), new TagValue(null, "Name2")])),
                (CustomPropertyType.Tags,
                    new ListTagValueBuilder([new TagValue("Group2", "Name3"), new TagValue(null, "4.5")])),
            }.Select(x => new CustomPropertyTypeConversionOverviewInputModel.T()
            {
                Type = x.Type,
                SerializedBizValue =
                    x.ValueBuilder?.Value?.SerializeAsStandardValue(
                        customPropertyDescriptors[(int) x.Type].BizValueType)
            });

            var results = new CustomPropertyTypeConversionOverviewViewModel {Results = []};

            foreach (var @in in testData)
            {
                var inPropertyDescriptor = customPropertyDescriptors[(int) @in.Type];

                var notNullValueResult = new CustomPropertyTypeConversionOverviewViewModel.Tin
                {
                    SerializedBizValue = @in.SerializedBizValue,
                    Type = @in.Type,
                    Outputs = [],
                    BizValueType = inPropertyDescriptor.BizValueType
                };

                var nullValueResult = new CustomPropertyTypeConversionOverviewViewModel.Tin
                {
                    SerializedBizValue = null,
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
                    if (@in.Type == CustomPropertyType.Attachment && outType == CustomPropertyType.Link)
                    {

                    }

                    var outBizType = outType.GetBizValueType();
                    var (nv, _) =
                        await standardValueService.CheckConversionLoss(inBizValue, fakeInProperty.BizValueType,
                            outBizType);

                    notNullValueResult.Outputs.Add(new CustomPropertyTypeConversionOverviewViewModel.Tout
                    {
                        SerializedBizValue = nv?.SerializeAsStandardValue(outBizType),
                        Type = outType,
                        BizValueType = outBizType
                    });

                    var (nv2, _) =
                        await standardValueService.CheckConversionLoss(null, fakeInProperty.BizValueType, outBizType);
                    nullValueResult.Outputs.Add(new CustomPropertyTypeConversionOverviewViewModel.Tout
                    {
                        SerializedBizValue = nv2?.SerializeAsStandardValue(outBizType),
                        Type = outType,
                        BizValueType = outBizType
                    });
                }

                results.Results.Add(notNullValueResult);
                results.Results.Add(nullValueResult);
            }

            return new SingletonResponse<CustomPropertyTypeConversionOverviewViewModel>(results);
        }
    }
}