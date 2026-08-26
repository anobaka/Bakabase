using System.Collections.Generic;

namespace Bakabase.InsideWorld.Models.Configs
{
    /// <summary>
    /// Built-in destinations offered when testing a proxy.
    /// </summary>
    /// <remarks>
    /// A proxy is rarely "working" or "not working" outright — it usually reaches some
    /// destinations and not others, which is the whole reason a single test URL was not
    /// enough. These are the sites users actually care about reaching.
    ///
    /// Each URL is chosen to be cheap to fetch and to answer without authentication;
    /// several are the vendors' own connectivity endpoints.
    /// </remarks>
    public record ProxyTestSite(string Id, string Name, string Url);

    public static class ProxyTestSites
    {
        public static readonly IReadOnlyList<ProxyTestSite> All = new List<ProxyTestSite>
        {
            new("google", "Google", "https://www.google.com/generate_204"),
            new("bing", "Bing", "https://www.bing.com"),
            new("youtube", "YouTube", "https://www.youtube.com/generate_204"),
            new("twitter", "X (Twitter)", "https://x.com"),
            new("facebook", "Facebook", "https://www.facebook.com"),
            new("instagram", "Instagram", "https://www.instagram.com"),
            new("openai", "OpenAI", "https://api.openai.com"),
            new("claude", "Claude", "https://api.anthropic.com"),
            new("github", "GitHub", "https://api.github.com"),
            new("exhentai", "ExHentai", "https://exhentai.org"),
        };

        /// <summary>
        /// Applied when the user has not chosen a selection yet — a small, fast spread
        /// rather than every site, so the first test is not needlessly slow.
        /// </summary>
        public static readonly IReadOnlyList<string> DefaultSelectedIds =
            new List<string> {"google", "youtube", "github"};
    }
}
