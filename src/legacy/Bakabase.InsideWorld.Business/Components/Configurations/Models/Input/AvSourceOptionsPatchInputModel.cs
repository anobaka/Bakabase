using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class AvSourceOptionsPatchInputModel
{
    public Dictionary<string, AvSourceConfig>? Sources { get; set; }
    public Dictionary<int, List<string>>? PreferredSourcesByTarget { get; set; }
}
