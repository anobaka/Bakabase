using Bakabase.Modules.ThirdParty.Abstractions.Http;

namespace Bakabase.Modules.ThirdParty.ThirdParties.SoulPlus;

public interface ISoulPlusOptions : IThirdPartyHttpClientOptions
{
    string? TlsPreset { get; }
    int AutoBuyThreshold => 0;
    int MinimumRemainingCoins => 0;
}
