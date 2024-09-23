using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Text.Abstractions;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Text
{
    public record MultilineTextProperty : TextProperty;

    public record MultilineTextPropertyValue : TextPropertyValue;

    public class MultilineTextPropertyDescriptor(IStandardValueHelper standardValueHelper) : TextPropertyDescriptor<SingleLineTextProperty, MultilineTextPropertyValue, string>(standardValueHelper)
    {
        public override CustomPropertyType EnumType => CustomPropertyType.MultilineText;
        protected override string[] GetMatchSources(string? value)
        {
            return string.IsNullOrEmpty(value) ? [] : [value];
        }
    }
}
