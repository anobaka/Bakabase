using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/custom-property")]
    public class CustomPropertyController : Controller
    {
        private readonly ICustomPropertyService _service;
        private readonly ICustomPropertyValueService _propertyValueService;

        public CustomPropertyController(ICustomPropertyService service, ICustomPropertyValueService propertyValueService)
        {
            _service = service;
            _propertyValueService = propertyValueService;
        }

        [HttpGet("all")]
        [SwaggerOperation(OperationId = "GetAllCustomProperties")]
        public async Task<ListResponse<CustomProperty>> GetAll(
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            return new ListResponse<CustomProperty>(await _service.GetAll(null, additionalItems, false));
        }

        [HttpGet("ids")]
        [SwaggerOperation(OperationId = "GetCustomPropertyByKeys")]
        public async Task<ListResponse<CustomProperty>> GetByKeys(int[] ids,
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            return new ListResponse<CustomProperty>(await _service.GetByKeys(ids, additionalItems, false));
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddCustomProperty")]
        public async Task<SingletonResponse<CustomProperty>> Add([FromBody] CustomPropertyAddOrPutDto model)
        {
            return new SingletonResponse<CustomProperty>(await _service.Add(model));
        }

        [HttpPut("{id:int}")]
        [SwaggerOperation(OperationId = "PutCustomProperty")]
        public async Task<SingletonResponse<CustomProperty>> Put(int id, [FromBody] CustomPropertyAddOrPutDto model)
        {
            return new(await _service.Put(id, model));
        }

        [HttpDelete("{id:int}")]
        [SwaggerOperation(OperationId = "RemoveCustomProperty")]
        public async Task<BaseResponse> Remove(int id)
        {
            return await _service.RemoveByKey(id);
        }

        [HttpPost("{id:int}/{type}/loss")]
        [SwaggerOperation(OperationId = "CalculateCustomPropertyTypeConversionLoss")]
        public async Task<SingletonResponse<CustomPropertyTypeConversionLossViewModel>> CalculateTypeConversionLoss(
            int id,
            CustomPropertyType type)
        {
            return new SingletonResponse<CustomPropertyTypeConversionLossViewModel>(
                await _service.CalculateTypeConversionLoss(id, type));
        }

        [HttpPut("{id:int}/options/adding-new-data-dynamically")]
        [SwaggerOperation(OperationId = "EnableAddingNewDataDynamicallyForCustomProperty")]
        public async Task<BaseResponse> EnableAddingNewDataDynamically(int id)
        {
            return await _service.EnableAddingNewDataDynamically(id);
        }
    }
}