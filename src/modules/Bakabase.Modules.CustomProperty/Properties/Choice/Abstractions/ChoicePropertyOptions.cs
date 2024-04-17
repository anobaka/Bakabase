namespace Bakabase.Modules.CustomProperty.Properties.Choice.Abstractions
{
    public record ChoicePropertyOptions<TValue>
    {
        public List<ChoiceOptions>? Choices { get; set; }
        public bool AllowAddingNewOptionsWhileChoosing { get; set; }
        public TValue? DefaultValue { get; set; }

        public record ChoiceOptions
        {
            public string Id { get; set; } = null!;
            public string Value { get; set; } = null!;
            public string Color { get; set; } = null!;
        }
    }
}