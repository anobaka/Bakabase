using System.Collections.Generic;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input
{
    public class ThirdPartyOptionsPatchInput
    {
        public List<SimpleSearchEngineOptionsPatchInput>? SimpleSearchEngines { get; set; }
        public string? CurlExecutable { get; set; }
        public bool? AutomaticallyParsingPosts { get; set; }

        public class SimpleSearchEngineOptionsPatchInput
        {
            public string? Name { get; set; }
            public string? UrlTemplate { get; set; }
        }
    }
}
