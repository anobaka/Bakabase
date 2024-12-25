using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Models.Input;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/property")]
    public class PropertyController(
        IPropertyService propertyService,
        IPropertyLocalizer propertyLocalizer,
        ICustomPropertyService customPropertyService) : Controller
    {
        [HttpGet("pool/{pool}")]
        [SwaggerOperation(OperationId = "GetPropertiesByPool")]
        public async Task<ListResponse<PropertyViewModel>> GetByPool(PropertyPool pool)
        {
            return new ListResponse<PropertyViewModel>(
                (await propertyService.GetProperties(pool)).Select(x => x.ToViewModel(propertyLocalizer)));
        }
    }
}