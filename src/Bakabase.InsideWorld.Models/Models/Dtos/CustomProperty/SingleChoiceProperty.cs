using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstrations;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty;

public record SingleChoiceProperty() : CustomPropertyDto<SingleChoiceProperty.SingleChoicePropertyOptions>
{
	public record SingleChoicePropertyOptions
	{
		public List<ChoiceOptions>? Choices { get; set; }
		public bool AllowAddingNewOptionsWhileChoosing { get; set; }
		public string? DefaultValue { get; set; }

		public record ChoiceOptions
		{
			public string Value { get; set; } = null!;
			public string Color { get; set; } = null!;
		}
	}
}