using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Values;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.ValueHelpers
{
	public class SingleChoicePropertyValueHelper : AbstractCustomPropertyValueHelper<SingleChoicePropertyValue, string>
	{
		public override CustomPropertyType Type => CustomPropertyType.SingleChoice;
	}
}
