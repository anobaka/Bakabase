using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

public interface IPostLinkHealthChecker
{
    Task<PostLinkHealth> CheckAsync(string url, string? accessCode, CancellationToken ct = default);
}
