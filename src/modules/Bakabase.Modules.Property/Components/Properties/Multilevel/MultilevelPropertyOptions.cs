using Bakabase.Modules.Property.Abstractions.Components;

namespace Bakabase.Modules.Property.Components.Properties.Multilevel
{
    public class MultilevelPropertyOptions : IAllowAddingNewDataDynamically
    {
        public List<MultilevelDataOptions>? Data { get; set; }
        public List<string>? DefaultValue { get; set; }
        public bool AllowAddingNewDataDynamically { get; set; }
    }
}