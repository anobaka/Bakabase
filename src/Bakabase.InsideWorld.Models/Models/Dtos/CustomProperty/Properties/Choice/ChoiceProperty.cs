using Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Abstractions;

namespace Bakabase.InsideWorld.Models.Models.Dtos.CustomProperty.Properties.Choice;

public abstract record ChoiceProperty<TValue>() : CustomPropertyDto<ChoicePropertyOptions<TValue>>;