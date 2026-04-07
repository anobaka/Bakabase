namespace Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;

/// <summary>
/// Shared account model for third-party platforms that need cookie-based authentication.
/// </summary>
public class ThirdPartyAccount
{
    public string? Name { get; set; }
    public string? Cookie { get; set; }
}
