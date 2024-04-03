using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Properties.DateTime
{
    public class DatePropertyDescriptor : DateTimePropertyDescriptor
    {
        public override CustomPropertyType Type => CustomPropertyType.Date;
    }
}
