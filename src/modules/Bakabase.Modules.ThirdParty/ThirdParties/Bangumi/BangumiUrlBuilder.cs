using System.Net;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Bangumi;

public class BangumiUrlBuilder
{
    // Search: https://bgm.tv/subject_search/%E3%83%9F%E3%82%B9%E3%83%86%E3%83%AA%E3%81%A8%E8%A8%80%E3%81%86%E5%8B%BF%E3%82%8C+%E5%8B%BF%E8%A8%80%E6%8E%A8%E7%90%86?cat=all
    // Detail: https://bgm.tv/subject/338076

    public static Uri Domain = new Uri(@"https://bgm.tv/");

    public static Uri Search(string keyword, string cat = "all") =>
        new(Domain, $"subject_search/{WebUtility.UrlEncode(keyword)}?cat={cat}");
}