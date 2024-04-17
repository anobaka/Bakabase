using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Properties.Text.Abstractions;

namespace Bakabase.Modules.CustomProperty.Properties.Text
{
    public record SingleLineTextProperty : TextProperty;

    public record SingleLineTextPropertyValue : TextPropertyValue;

    public class SingleLineTextPropertyDescriptor : TextPropertyDescriptor<SingleLineTextPropertyValue, string>
    {
        public override CustomPropertyType Type => CustomPropertyType.SingleLineText;
        protected override string[] GetMatchSources(string? value)
        {
            return string.IsNullOrEmpty(value) ? [] : [value];
        }
    }
}
