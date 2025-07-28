using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;
using static Bakabase.InsideWorld.Models.Configs.UIOptions;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input
{
    public class UIOptionsPatchRequestModel
    {
        public UIResourceOptions? Resource { get; set; }
        public StartupPage? StartupPage { get; set; }
        public bool? IsMenuCollapsed { get; set; }
        public List<PropertyKey>? LatestUsedProperties { get; set; }
    }
}
