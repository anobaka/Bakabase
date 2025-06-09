using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.Property.Components.Properties.Choice.Abstractions
{
    public abstract record ChoicePropertyOptions<TDbValue>
    {
        public List<ChoiceOptions>? Choices { get; set; }
        // public bool AllowAddingNewDataDynamically { get; set; }
        public TDbValue? DefaultValue { get; set; }
    }
}