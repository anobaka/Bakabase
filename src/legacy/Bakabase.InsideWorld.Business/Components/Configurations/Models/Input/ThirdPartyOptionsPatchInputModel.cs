using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input
{
    public class ThirdPartyOptionsPatchInput
    {
        public List<SimpleSearchEngineOptionsPatchInput>? SimpleSearchEngines { get; set; }
        public bool? AutomaticallyParsingPosts { get; set; }

        [Range(1, int.MaxValue)]
        public int? PostParserMaxConcurrency { get; set; }

        [Range(1, int.MaxValue)]
        public int? PostParserAiMaxConcurrency { get; set; }

        public class SimpleSearchEngineOptionsPatchInput
        {
            public string? Name { get; set; }
            public string? UrlTemplate { get; set; }
        }
    }
}
