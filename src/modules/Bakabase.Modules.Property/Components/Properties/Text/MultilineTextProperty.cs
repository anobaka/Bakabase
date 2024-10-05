using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Components.Properties.Text.Abstractions;

namespace Bakabase.Modules.Property.Components.Properties.Text
{
    public class MultilineTextPropertyDescriptor : TextPropertyDescriptor<string, string>
    {
        public override PropertyType Type => PropertyType.MultilineText;

        protected override string[] GetMatchSources(string value) => [value];
    }
}