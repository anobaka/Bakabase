using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/component")]
    public class ComponentController : Controller
    {
        private readonly ComponentService _componentService;

        public ComponentController(ComponentService componentService)
        {
            _componentService = componentService;
        }

        [HttpGet]
        [SwaggerOperation(OperationId = "GetComponentDescriptors")]
        public async Task<ListResponse<ComponentDescriptorDto>> GetDescriptors(ComponentType? type = null,
            ComponentDescriptorAdditionalItem additionalItems = ComponentDescriptorAdditionalItem.None)
        {
            return new ListResponse<ComponentDescriptorDto>(
                await _componentService.GetDescriptorDtoList(type, additionalItems));
        }

        [HttpGet("key")]
        [SwaggerOperation(OperationId = "GetComponentDescriptorByKey")]
        public async Task<SingletonResponse<ComponentDescriptorDto>> GetDescriptor(string key,
            ComponentDescriptorAdditionalItem additionalItems = ComponentDescriptorAdditionalItem.None)
        {
            return new SingletonResponse<ComponentDescriptorDto>(
                await _componentService.GetDescriptorDto(key, additionalItems));
        }
    }
}