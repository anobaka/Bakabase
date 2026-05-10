using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

/// <summary>
/// Built-in default cookies to bypass age/region gates for known AV sites.
///
/// Sources:
/// - javbus: "existmag=mag" pre-seeds the age-verification preference so detail pages
///   render directly instead of showing the regional gate.
/// - javdb: "over18=1; locale=zh; theme=auto" satisfies the over-18 acknowledgement
///   and pins the parser-friendly Chinese locale.
/// - javlibrary: "over18=18" skips the entry interstitial.
/// - dmm/fanza: "age_check_done=1" skips the age confirmation interstitial.
/// </summary>
public static class AvSourceDefaults
{
    public static readonly IReadOnlyDictionary<string, string> DefaultCookies =
        new Dictionary<string, string>
        {
            ["javbus"] = "existmag=mag",
            ["javdb"] = "over18=1; locale=zh; theme=auto",
            ["javlibrary"] = "over18=18",
            ["dmm"] = "age_check_done=1",
            ["fc2"] = "wmode=1; contents_tag=1",
            ["mgstage"] = "adc=1",
        };

    public static readonly IReadOnlyDictionary<string, string> DefaultBaseUrls =
        new Dictionary<string, string>
        {
            ["javbus"] = "https://www.javbus.com",
            ["javdb"] = "https://javdb.com",
            ["javlibrary"] = "https://www.javlibrary.com",
            ["airav"] = "https://www.airav.io",
            ["airavcc"] = "https://airav.cc",
            ["avsex"] = "https://www.avsex.cc",
            ["avsox"] = "https://avsox.com",
            ["cableav"] = "https://cableav.tv",
            ["cnmdb"] = "https://www.cnmdb.net",
            ["dmm"] = "https://www.dmm.co.jp",
            ["dahlia"] = "https://dahlia-av.com",
            ["fc2"] = "https://adult.contents.fc2.com",
            ["faleno"] = "https://faleno.jp",
            ["fantastica"] = "https://fantastica-vr.com",
            ["fc2club"] = "https://fc2club.top",
            ["fc2hub"] = "https://fc2hub.com",
            ["fc2ppvdb"] = "https://fc2ppvdb.com",
            ["freejavbt"] = "https://freejavbt.com",
            ["getchu"] = "https://www.getchu.com",
            ["getchudl"] = "https://dl.getchu.com",
            ["giga"] = "https://giga-web.jp",
            ["hdouban"] = "https://hdouban.com",
            ["hscangku"] = "https://hscangku.net",
            ["iqqtv"] = "https://iqqtv.cloud",
            ["iqqtvnew"] = "https://iqq5.xyz",
            ["jav321"] = "https://www.jav321.com",
            ["javday"] = "https://javday.tv",
            ["kin8"] = "https://www.kin8tengoku.com",
            ["love6"] = "https://love6.tv",
            ["lulubar"] = "https://lulubar.co",
            ["madouqu"] = "https://madouqu.com",
            ["mdtv"] = "https://www.mdtv.com",
            ["mgstage"] = "https://www.mgstage.com",
            ["mmtv"] = "https://mmtv.tv",
            ["mywife"] = "https://mywife.cc",
            ["official"] = "https://official.javbus.com",
            ["prestige"] = "https://www.prestige-av.com",
            ["theporndb"] = "https://api.theporndb.net",
            ["theporndbmovies"] = "https://api.theporndb.net",
            ["xcity"] = "https://xcity.jp",
        };

    public static readonly IReadOnlyList<string> AllSources = new[]
    {
        "airav", "airavcc", "avsex", "avsox", "cableav", "cnmdb", "dmm", "dahlia",
        "fc2", "faleno", "fantastica", "fc2club", "fc2hub", "fc2ppvdb", "freejavbt",
        "getchu", "getchudl", "giga", "hdouban", "hscangku", "iqqtv", "iqqtvnew",
        "jav321", "javbus", "javday", "javdb", "javlibrary", "kin8", "love6", "lulubar",
        "madouqu", "mdtv", "mgstage", "mmtv", "mywife", "official", "prestige",
        "theporndb", "theporndbmovies", "xcity",
    };
}
