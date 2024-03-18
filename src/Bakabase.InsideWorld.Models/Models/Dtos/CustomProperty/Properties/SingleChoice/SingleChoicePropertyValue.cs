using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;
using Bakabase.InsideWorld.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.SingleChoice
{
    public record SingleChoicePropertyValue : CustomPropertyValueDto<string>
    {
        protected override bool IsMatch(string? value, CustomPropertyValueSearchRequestModel model)
        {
            throw new NotImplementedException();
        }
    }
}
