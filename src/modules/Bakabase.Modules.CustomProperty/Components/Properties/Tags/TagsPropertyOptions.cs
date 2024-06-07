using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Tags;

public record TagsPropertyOptions
{
    public List<TagOptions>? Tags { get; set; }
    public bool AllowAddingNewDataDynamically { get; set; }

    public record TagOptions(string? Group, string Name) : TagValue(Group, Name), ITagBizKey
    {
        public string Value { get; set; } = null!;
        public string? Color { get; set; }
    }
}