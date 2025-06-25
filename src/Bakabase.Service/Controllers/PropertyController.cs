using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.Property.Abstractions.Services;
using Bakabase.Modules.Property.Components;
using Bakabase.Modules.Property.Extensions;
using Bakabase.Modules.Property.Models.Input;
using Bakabase.Modules.Property.Models.View;
using Bakabase.Modules.StandardValue.Extensions;
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
            var typePropertiesMap = (await service.GetProperties(PropertyPool.All)).GroupBy(d => d.Type)
                .ToDictionary(d => d.Key, d => d.ToArray());
            var viewModels = SpecificEnumUtils<PropertyType>.Values.Select(a =>
            {
                PropertyViewModel[]? properties = null;
                string? unavailableReason = null;
                if (a.IsReferenceValueType())
                {
                    properties = typePropertiesMap.GetValueOrDefault(a)?.Select(x => x.ToViewModel(localizer))
                        .ToArray();
                    if (properties?.Any() != true)
                    {
                        unavailableReason = localizer
                            .UnavailablePropertyTypeForManuallySettingValue_DueTo_NoPropertyWithReferenceValueType();
                    }
                }
                else
                {
                    var virtualProperty = PropertyInternals.VirtualPropertyMap.GetValueOrDefault(a);
                    if (virtualProperty != null)
                    {
                        properties = [virtualProperty.ToViewModel(localizer)];
                    }
                }

                return new PropertyTypeForManuallySettingValueViewModel(a, a.GetDbValueType(), a.GetBizValueType(),
                    a.IsReferenceValueType(), properties, unavailableReason);
            });
            return new ListResponse<PropertyTypeForManuallySettingValueViewModel>(viewModels);
        }

        [HttpGet("pool/{pool}/id/{id}/biz-value")]
        [SwaggerOperation(OperationId = "GetPropertyBizValue")]
        public async Task<SingletonResponse<string>> GetBizValue(PropertyPool pool, int id, string? dbValue)
        {
            var property = await service.GetProperty(pool, id);
            var bizValue = property.GetBizValue(dbValue.DeserializeDbValueAsStandardValue(property.Type))
                ?.SerializeBizValueAsStandardValue(property.Type);
            return new SingletonResponse<string>(bizValue);
        }

        [HttpGet("pool/{pool}/id/{id}/db-value")]
        [SwaggerOperation(OperationId = "GetPropertyDbValue")]
        public async Task<SingletonResponse<string>> GetDbValue(PropertyPool pool, int id, string? bizValue)
        {
            var property = await service.GetProperty(pool, id);
            var pd = PropertyInternals.DescriptorMap[property.Type];
            var (dbValue, _) = pd.PrepareDbValue(property, bizValue.DeserializeBizValueAsStandardValue(property.Type));
            return new SingletonResponse<string>(dbValue.SerializeDbValueAsStandardValue(property.Type));
        }

        [HttpGet("best-matching")]
        [SwaggerOperation(OperationId = "FindBestMatchingProperty")]
        public async Task<SingletonResponse<PropertyViewModel?>> FindBestMatchingProperty(PropertyType type,
            string name)
        {
            var properties = await service.GetProperties(PropertyPool.Custom | PropertyPool.Reserved);
            var property = properties.OrderBy(x => x.Pool == PropertyPool.Reserved ? -1 : 0)
                .FirstOrDefault(x => x.Type == type && x.Name == name);
            return new SingletonResponse<PropertyViewModel?>(property?.ToViewModel(localizer));
        }
    }
}