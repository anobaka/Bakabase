using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class SteamOptionsPatchInputModel
{
    public List<SteamAccount>? Accounts { get; set; }
}
