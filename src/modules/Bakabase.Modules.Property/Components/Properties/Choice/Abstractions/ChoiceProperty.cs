using Bakabase.Modules.Property.Abstractions.Models;

namespace Bakabase.Modules.Property.Components.Properties.Choice.Abstractions;

public abstract record ChoiceProperty<TChoicePropertyOptions, TDbValue>() : CustomProperty<TChoicePropertyOptions> where TChoicePropertyOptions: ChoicePropertyOptions<TDbValue>;