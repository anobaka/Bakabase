using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Tags;

public record TagsPropertyOptions : IAllowAddingNewDataDynamically
{
    public List<TagOptions>? Tags { get; set; }
    public bool AllowAddingNewDataDynamically { get; set; }

    public record TagOptions(string? Group, string Name) : TagValue(Group, Name), ITagBizKey
    {
        public static string GenerateValue() => Guid.NewGuid().ToString();
        public string Value { get; set; } = null!;
        public string? Color { get; set; }

        public TagValue ToTagValue()
        {
            return new(Group, Name);
        }
    }
}