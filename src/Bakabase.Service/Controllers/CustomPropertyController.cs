using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bakabase.Modules.CustomProperty.Extensions;
using Bakabase.Modules.StandardValue.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/custom-property")]
    public class CustomPropertyController(
        ICustomPropertyService service,
        ICustomPropertyValueService propertyValueService,
        ICustomPropertyDescriptors customPropertyDescriptors)
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
    }
}