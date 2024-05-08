using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Properties.DateTime
{
    public class DatePropertyDescriptor : DateTimePropertyDescriptor
    {
        public override CustomPropertyType EnumType => CustomPropertyType.Date;
    }

    public record DatePropertyValue : DateTimePropertyValue;
}
