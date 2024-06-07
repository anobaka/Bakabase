using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Text.Abstractions;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Text
{
    public record SingleLineTextProperty : TextProperty;

    public record SingleLineTextPropertyValue : TextPropertyValue;

    public class SingleLineTextPropertyDescriptor : TextPropertyDescriptor<SingleLineTextPropertyValue, string>
    {
        public override CustomPropertyType EnumType => CustomPropertyType.SingleLineText;
        protected override string[] GetMatchSources(string? value)
        {
            return string.IsNullOrEmpty(value) ? [] : [value];
        }
    }
}
