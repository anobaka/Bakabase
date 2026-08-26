using System.Collections.Generic;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.Modules.ThirdParty.Abstractions.Http
{
    /// <summary>
    /// Sources whose traffic can be routed through their own proxy.
    /// </summary>
    /// <remarks>
    /// A per-source proxy is bound to the source's own <see cref="AbstractThirdPartyHttpMessageHandler{TOptions}"/>,
    /// so only sources that have one can honour the setting. Everything else shares the default
    /// HTTP client and therefore always follows the global proxy — offering them the setting would
    /// mean offering one that silently does nothing.
    ///
    /// Keep this in step with the concrete handlers under <c>ThirdParties/*/…HttpMessageHandler.cs</c>.
    /// </remarks>
    public static class ProxyCapableThirdParties
    {
        public static readonly IReadOnlyList<ThirdPartyId> All = new List<ThirdPartyId>
        {
            ThirdPartyId.ExHentai,
            ThirdPartyId.Pixiv,
            ThirdPartyId.Bilibili,
            ThirdPartyId.DLsite,
            ThirdPartyId.Bangumi,
            ThirdPartyId.SoulPlus,
            ThirdPartyId.Tmdb,
        };
    }
}
