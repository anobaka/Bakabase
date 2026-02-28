using System.Collections.Generic;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options]
    public class UIStyleOptions
    {
        /// <summary>
        /// CSS variable overwrites. Key is CSS variable name (e.g. "--resource-cover-property-font-size"), Value is the value (e.g. "12px").
        /// Frontend defines variable defaults and valid ranges; backend only stores.
        /// </summary>
        public Dictionary<string, string> CssVariableOverwrites { get; set; } = new();
    }
}
