using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Properties.Text
{
    public record SingleLineTextProperty : TextProperty;

    public record SingleLineTextPropertyValue : TextPropertyValue;

    public class SingleLineTextPropertyDescriptor : TextPropertyDescriptor
    {
        public override CustomPropertyType Type => CustomPropertyType.SingleLineText;
    }
}
