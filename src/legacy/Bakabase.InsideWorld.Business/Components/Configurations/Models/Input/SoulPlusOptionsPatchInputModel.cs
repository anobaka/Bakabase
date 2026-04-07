using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class SoulPlusOptionsPatchInputModel
{
    public List<ThirdPartyAccount>? Accounts { get; set; }
    public string? Cookie { get; set; }
    public int? AutoBuyThreshold { get; set; }
}