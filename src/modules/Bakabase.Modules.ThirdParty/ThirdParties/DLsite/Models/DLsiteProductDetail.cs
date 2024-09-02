using Bakabase.Modules.StandardValue.Models.Domain;

namespace Bakabase.Modules.ThirdParty.ThirdParties.DLsite.Models;

public record DLsiteProductDetail
{
    public string[]? CoverUrls { get; set; }
    public string? Introduction { get; set; }
    public string? Name { get; set; }
    public decimal? Rating { get; set; }
    public Dictionary<string, List<string>>? PropertiesOnTheRightSideOfCover { get; set; }
}