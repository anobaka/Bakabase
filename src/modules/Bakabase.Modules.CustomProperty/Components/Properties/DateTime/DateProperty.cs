using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.DateTime
{
    public class DatePropertyDescriptor(IStandardValueHelper standardValueHelper) : DateTimePropertyDescriptor(standardValueHelper)
    {
        public override CustomPropertyType EnumType => CustomPropertyType.Date;
    }

    public record DatePropertyValue : DateTimePropertyValue;
}
