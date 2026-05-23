using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

/// <summary>
/// Single source of truth for AV source identifiers.
///
/// The same string is used in three places that must stay in sync, or
/// per-target preferred-source filtering silently drops detail records:
/// the AvEnhancer dispatcher key, the AvController dispatcher key, and the
/// IAvDetail.Source value that each client assigns to its returned detail.
/// Reference the constants below instead of writing the literal string.
/// </summary>
public static class AvSourceIds
{
    public const string Airav = "airav";
    public const string Avsex = "avsex";
    public const string Avsox = "avsox";
    public const string Cnmdb = "cnmdb";
    public const string Dahlia = "dahlia";
    public const string Dmm = "dmm";
    public const string Fc2 = "fc2";
    public const string Fc2Hub = "fc2hub";
    public const string Faleno = "faleno";
    public const string Fantastica = "fantastica";
    public const string Freejavbt = "freejavbt";
    public const string GetchuDl = "getchudl";
    public const string Iqqtv = "iqqtv";
    public const string Jav321 = "jav321";
    public const string Javbus = "javbus";
    public const string Javday = "javday";
    public const string Javdb = "javdb";
    public const string Javlibrary = "javlibrary";
    public const string Lulubar = "lulubar";
    public const string Mmtv = "mmtv";

    public static readonly IReadOnlyList<string> All = new[]
    {
        Airav, Avsex, Avsox, Cnmdb, Dahlia, Dmm,
        Fc2, Fc2Hub, Faleno, Fantastica, Freejavbt,
        GetchuDl, Iqqtv, Jav321, Javbus, Javday, Javdb,
        Javlibrary, Lulubar, Mmtv,
    };
}
