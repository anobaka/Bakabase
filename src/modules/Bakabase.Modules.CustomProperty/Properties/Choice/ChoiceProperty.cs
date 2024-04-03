using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.CustomProperty.Properties.Choice;

public abstract record ChoiceProperty<TValue>() : CustomProperty<ChoicePropertyOptions<TValue>>;