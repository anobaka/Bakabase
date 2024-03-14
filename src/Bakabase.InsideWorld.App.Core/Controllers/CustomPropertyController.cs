using System;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
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

		public CustomPropertyController(CustomPropertyService service)
		{
			_service = service;
		}

		[HttpGet]
		[SwaggerOperation(OperationId = "GetAllCustomPropertiesV2")]
		public async Task<ListResponse<CustomPropertyDto>> GetAll(CustomPropertyAdditionalItem additionalItems = CustomPropertyAdditionalItem.None)
		{
			return new ListResponse<CustomPropertyDto>(await _service.GetDtoList(null, additionalItems, false));
		}

		[HttpPost]
		[SwaggerOperation(OperationId = "AddCustomProperty")]
		public async Task<SingletonResponse<CustomPropertyDto>> Add([FromBody] CustomPropertyAddOrPutRequestModel model)
		{
			return new SingletonResponse<CustomPropertyDto>(await _service.Add(model));
		}

		[HttpPut("{id:int}")]
		[SwaggerOperation(OperationId = "UpdateCustomProperty")]
		public async Task<SingletonResponse<CustomPropertyDto>> Update(int id, [FromBody] CustomPropertyAddOrPutRequestModel model)
		{
			return new(await _service.Update(id, model));
		}
	}
}