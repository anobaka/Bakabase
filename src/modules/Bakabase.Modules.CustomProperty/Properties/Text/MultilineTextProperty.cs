using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Properties.Text
{
    public record MultilineTextProperty : TextProperty;

    public record MultilineTextPropertyValue : TextPropertyValue;

    public class MultilineTextPropertyDescriptor : TextPropertyDescriptor
    {
        public override CustomPropertyType Type => CustomPropertyType.MultilineText;
    }
}
