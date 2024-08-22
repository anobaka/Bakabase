using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.InsideWorld.Business.Components;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants.AdditionalItems;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.InsideWorld.Models.RequestModels;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/component-options")]
    public class ComponentOptionsController : Controller
    {
        private readonly ComponentOptionsService _service;
        private readonly ICategoryService _categoryService;
        private readonly CategoryComponentService _categoryComponentService;
        private readonly InsideWorldLocalizer _localizer;

        public ComponentOptionsController(ComponentOptionsService service, ICategoryService categoryService,
            CategoryComponentService categoryComponentService, InsideWorldLocalizer localizer)
        {
            _service = service;
            _categoryService = categoryService;
            _categoryComponentService = categoryComponentService;
            _localizer = localizer;
        }

        [HttpPost]
        [SwaggerOperation(OperationId = "AddComponentOptions")]
        public async Task<SingletonResponse<ComponentOptions>> Add([FromBody] ComponentOptionsAddRequestModel model)
        {
            return await _service.Add(model);
        }

        [HttpPut("{id}")]
        [SwaggerOperation(OperationId = "PutComponentOptions")]
        public async Task<BaseResponse> Put(int id, [FromBody] ComponentOptionsAddRequestModel model)
        {
            return await _service.Put(id, model);
        }

        [HttpDelete("{id}")]
        [SwaggerOperation(OperationId = "RemoveComponentOptions")]
        public async Task<BaseResponse> Remove(int id)
        {
            var key = id.ToString();
            var mappings = await _categoryComponentService.GetAll(a => a.ComponentKey == key);
            if (mappings.Any())
            {
                var cIds = mappings.Select(a => a.CategoryId).Distinct().ToArray();
                var categories = await _categoryService.GetByKeys(cIds, CategoryAdditionalItem.None);
                if (categories.Any())
                {
                    return BaseResponseBuilder.BuildBadRequest(
                        _localizer.Component_NotDeletableWhenUsingByCategories(categories.Select(c => c.Name)));
                }
            }

            return await _service.RemoveByKey(id);
        }
    }
}