using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options]
    public class ThirdPartyOptions
    {
        public List<SimpleSearchEngineOptions>? SimpleSearchEngines { get; set; }

        public class SimpleSearchEngineOptions
        {
            public string Name { get; set; } = string.Empty;
            public string UrlTemplate { get; set; } = string.Empty;
        }

        public bool AutomaticallyParsingPosts { get; set; }

        [Range(1, int.MaxValue)]
        public int PostParserMaxConcurrency { get; set; } = 10;

        [Range(1, int.MaxValue)]
        public int PostParserAiMaxConcurrency { get; set; } = 1;
    }
}
