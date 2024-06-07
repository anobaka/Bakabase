using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.DateTime
{
    public class DatePropertyDescriptor : DateTimePropertyDescriptor
    {
        public override CustomPropertyType EnumType => CustomPropertyType.Date;
    }

    public record DatePropertyValue : DateTimePropertyValue;
}
