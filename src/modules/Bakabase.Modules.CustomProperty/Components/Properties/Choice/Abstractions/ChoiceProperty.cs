using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.CustomProperty.Models;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions;

public abstract record ChoiceProperty<TValue>() : CustomProperty<ChoicePropertyOptions<TValue>>;