using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.PropertyHelpers
{
    public class SingleChoicePropertyHelper : AbstractCustomPropertyHelper<SingleChoiceProperty,
		SingleChoiceProperty.SingleChoicePropertyOptions>
	{
		public override CustomPropertyType Type => CustomPropertyType.SingleChoice;
	}
}