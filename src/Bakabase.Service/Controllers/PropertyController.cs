using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.Input;
using Bakabase.Service.Extensions;
using Bakabase.Service.Models.View;
using Bootstrap.Components.Miscellaneous.ResponseBuilders;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/property")]
    public class PropertyController(
        IPropertyService service,
        IPropertyLocalizer localizer,
        ICustomPropertyService customPropertyService) : Controller
    {
        [HttpGet("pool/{pool}")]
        [SwaggerOperation(OperationId = "GetPropertiesByPool")]
        public async Task<ListResponse<PropertyViewModel>> GetByPool(PropertyPool pool)
        {
            return new ListResponse<PropertyViewModel>(
                (await service.GetProperties(pool)).Select(x => x.ToViewModel(localizer)));
        }

        [HttpGet("property-types-for-manually-setting-value")]
        [SwaggerOperation(OperationId = "GetAvailablePropertyTypesForManuallySettingValue")]
        public async Task<ListResponse<PropertyTypeForManuallySettingValueViewModel>>
            GetAvailablePropertyTypesForManuallySettingValue()
        {
            var propertyTypes = (await service.GetProperties(PropertyPool.All)).Select(a => a.Type).ToHashSet();
            var viewModels = SpecificEnumUtils<PropertyType>.Values.Select(a =>
            {
                var isAvailable = !a.IsReferenceValueType() || propertyTypes.Contains(a);
                return new PropertyTypeForManuallySettingValueViewModel(a, isAvailable,
                    isAvailable
                        ? null
                        : localizer
                            .UnavailablePropertyTypeForManuallySettingValue_DueTo_NoPropertyWithReferenceValueType());
            });
            return new ListResponse<PropertyTypeForManuallySettingValueViewModel>(viewModels);
        }
    }
}