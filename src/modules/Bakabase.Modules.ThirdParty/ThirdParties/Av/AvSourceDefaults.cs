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
            [AvSourceIds.Javbus] = "existmag=mag; dv=1",
            [AvSourceIds.Javdb] = "over18=1; locale=zh; theme=auto",
            [AvSourceIds.Javlibrary] = "over18=18",
            [AvSourceIds.Dmm] = "age_check_done=1",
            [AvSourceIds.Fc2] = "wmode=1; contents_tag=1",
        };

    public static readonly IReadOnlyDictionary<string, string> DefaultBaseUrls =
        new Dictionary<string, string>
        {
            [AvSourceIds.Javbus] = "https://www.javbus.com",
            [AvSourceIds.Javdb] = "https://javdb.com",
            [AvSourceIds.Javlibrary] = "https://www.javlibrary.com",
            [AvSourceIds.Airav] = "https://airav.io",
            [AvSourceIds.Avsex] = "https://gg5.co",
            [AvSourceIds.Avsox] = "https://avsox.com",
            [AvSourceIds.Cnmdb] = "https://cnmdb.net",
            [AvSourceIds.Dmm] = "https://www.dmm.co.jp",
            [AvSourceIds.Dahlia] = "https://dahlia-av.com",
            [AvSourceIds.Fc2] = "https://adult.contents.fc2.com",
            [AvSourceIds.Faleno] = "https://falenogroup.com",
            [AvSourceIds.Fantastica] = "https://fantastica-vr.com",
            [AvSourceIds.Fc2Hub] = "https://fc2hub.com",
            [AvSourceIds.Freejavbt] = "https://freejavbt.com",
            [AvSourceIds.GetchuDl] = "https://dl.getchu.com",
            [AvSourceIds.Iqqtv] = "https://iqqtv.cloud",
            [AvSourceIds.Jav321] = "https://www.jav321.com",
            [AvSourceIds.Javday] = "https://javday.tv",
            [AvSourceIds.Lulubar] = "https://lulubar.co",
            [AvSourceIds.Mmtv] = "https://mmtv.tv",
        };
}
