using Bakabase.Modules.CustomProperty.Abstractions.Components;
using Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Choice.Abstractions
{
    public record ChoicePropertyOptions<TValue> : IAllowAddingNewDataDynamically
    {
        public List<ChoiceOptions>? Choices { get; set; }
        public bool AllowAddingNewDataDynamically { get; set; }
        public TValue? DefaultValue { get; set; }

        public record ChoiceOptions
        {
            public string Value { get; set; } = Guid.NewGuid().ToString();
            public string Label { get; set; } = null!;
            public string Color { get; set; } = null!;
        }
    }
}