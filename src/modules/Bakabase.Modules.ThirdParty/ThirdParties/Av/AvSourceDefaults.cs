using System.Collections.Generic;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

/// <summary>
/// Built-in default cookies / base URLs for known AV sources.
///
/// Cookie notes:
/// - javbus: needs both "existmag=mag" (age gate) AND "dv=1" (driver-verify gate
///   introduced 2026 — without it the site redirects every detail request to
///   /doc/driver-verify).
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
            ["javbus"] = "existmag=mag; dv=1",
            ["javdb"] = "over18=1; locale=zh; theme=auto",
            ["javlibrary"] = "over18=18",
            ["dmm"] = "age_check_done=1",
            ["fc2"] = "wmode=1; contents_tag=1",
        };

    public static readonly IReadOnlyDictionary<string, string> DefaultBaseUrls =
        new Dictionary<string, string>
        {
            ["javbus"] = "https://www.javbus.com",
            ["javdb"] = "https://javdb.com",
            ["javlibrary"] = "https://www.javlibrary.com",
            ["airav"] = "https://airav.io",
            ["avsex"] = "https://gg5.co",
            ["avsox"] = "https://avsox.com",
            ["cnmdb"] = "https://cnmdb.net",
            ["dmm"] = "https://www.dmm.co.jp",
            ["dahlia"] = "https://dahlia-av.com",
            ["fc2"] = "https://adult.contents.fc2.com",
            ["faleno"] = "https://falenogroup.com",
            ["fantastica"] = "https://fantastica-vr.com",
            ["fc2hub"] = "https://fc2hub.com",
            ["freejavbt"] = "https://freejavbt.com",
            ["getchudl"] = "https://dl.getchu.com",
            ["iqqtv"] = "https://iqqtv.cloud",
            ["jav321"] = "https://www.jav321.com",
            ["javday"] = "https://javday.tv",
            ["lulubar"] = "https://lulubar.co",
            ["mmtv"] = "https://mmtv.tv",
        };

    public static readonly IReadOnlyList<string> AllSources = new[]
    {
        "airav", "avsex", "avsox", "cnmdb", "dmm", "dahlia",
        "fc2", "faleno", "fantastica", "fc2hub", "freejavbt",
        "getchudl", "iqqtv", "jav321", "javbus", "javday", "javdb",
        "javlibrary", "lulubar", "mmtv",
    };
}
