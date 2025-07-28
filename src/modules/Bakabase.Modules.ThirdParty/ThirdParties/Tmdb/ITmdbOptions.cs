using Bakabase.Modules.ThirdParty.Abstractions.Http;

namespace Bakabase.Modules.ThirdParty.ThirdParties.Tmdb;

public interface ITmdbOptions : IThirdPartyHttpClientOptions
{
    public string? ApiKey { get; }
}