using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.Abstractions.Models.View;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/custom-property")]
    public class CustomPropertyController : Controller
    {
        private readonly CustomPropertyService _service;
        private readonly CustomPropertyValueService _propertyValueService;

        public CustomPropertyController(CustomPropertyService service, CustomPropertyValueService propertyValueService)
        {
            _service = service;
            _propertyValueService = propertyValueService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetAllCustomPropertiesV2")]
        public async Task<ListResponse<CustomProperty>> GetAll(
            CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
        {
            return new ListResponse<CustomProperty>(await _service.GetAll(null, additionalItems, false));
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
    }
}