using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.Modules.CustomProperty.Properties.Multilevel
{
    internal class MultilevelPropertyDescriptor: AbstractCustomPropertyDescriptor<MultilevelProperty, MultilevelPropertyOptions, MultilevelPropertyValue, string>
    {
        public override CustomPropertyType Type => CustomPropertyType.Multilevel;
        protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
