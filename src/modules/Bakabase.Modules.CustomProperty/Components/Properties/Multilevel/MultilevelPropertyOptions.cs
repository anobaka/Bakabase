using Bakabase.Modules.CustomProperty.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Components.Properties.Multilevel
{
    public class MultilevelPropertyOptions : IAllowAddingNewDataDynamically
    {
        public List<MultilevelDataOptions>? Data { get; set; }
        public string? DefaultValue { get; set; }
        public bool AllowAddingNewDataDynamically { get; set; }
    }
}