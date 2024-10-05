using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components.Properties.Text.Abstractions;
using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.Property.Components.Properties.Link;

public class LinkPropertyDescriptor : TextPropertyDescriptor<LinkValue, LinkValue>
{
    public override PropertyType Type => PropertyType.Link;

    protected override string[] GetMatchSources(LinkValue? value) =>
        new[] {value?.Text, value?.Url}.Where(s => !string.IsNullOrEmpty(s)).ToArray()!;
}