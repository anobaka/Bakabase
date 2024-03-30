using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Multilevel
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
