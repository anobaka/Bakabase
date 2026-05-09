namespace Bakabase.Modules.ThirdParty.ThirdParties.Av;

public record AvSourceResolvedConfig(bool Enabled, string? BaseUrl, string? Cookie, string? UserAgent);

public interface IAvSourceOptionsProvider
{
    AvSourceResolvedConfig Resolve(string sourceId);
}
