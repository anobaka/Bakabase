using Newtonsoft.Json;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite.Models;

public record DLsiteProductInfo
{
    [JsonProperty(propertyName: "rate_average_2dp")]
    public decimal? RateAverage2Dp { get; set; }
}