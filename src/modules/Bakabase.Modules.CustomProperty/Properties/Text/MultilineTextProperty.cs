using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text.Abstractions;

namespace Bakabase.Modules.CustomProperty.Properties.Text
{
    public record MultilineTextProperty : TextProperty;

    public record MultilineTextPropertyValue : TextPropertyValue;

    public class MultilineTextPropertyDescriptor : TextPropertyDescriptor<MultilineTextPropertyValue, string>
    {
        public override CustomPropertyType EnumType => CustomPropertyType.MultilineText;
        protected override string[] GetMatchSources(string? value)
        {
            return string.IsNullOrEmpty(value) ? [] : [value];
        }
    }
}
