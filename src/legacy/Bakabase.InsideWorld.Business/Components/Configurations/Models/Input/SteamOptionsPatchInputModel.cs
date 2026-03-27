using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Options;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class SteamOptionsPatchInputModel
{
    public List<SteamAccount>? Accounts { get; set; }
    public bool? ShowCover { get; set; }
}
