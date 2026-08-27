using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain.Options;

namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Input;

public class SteamOptionsPatchInputModel
{
    public List<SteamAccount>? Accounts { get; set; }
    public bool? ShowCover { get; set; }
    public int? AutoSyncIntervalMinutes { get; set; }

    /// <summary>
    /// Steam API language code. Empty string clears it back to following the app language,
    /// since a null here means "not specified" and leaves the stored value alone.
    /// </summary>
    public string? Language { get; set; }
}
