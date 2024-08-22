using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Components.Properties.Text.Abstractions;
using Bakabase.Modules.StandardValue.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Text
{
    public record SingleLineTextProperty : TextProperty;

    public record SingleLineTextPropertyValue : TextPropertyValue;

    public class SingleLineTextPropertyDescriptor(IStandardValueHelper standardValueHelper) : TextPropertyDescriptor<SingleLineTextPropertyValue, string>(standardValueHelper)
    {
        public override CustomPropertyType EnumType => CustomPropertyType.SingleLineText;
        protected override string[] GetMatchSources(string? value)
        {
            return string.IsNullOrEmpty(value) ? [] : [value];
        }
    }
}
