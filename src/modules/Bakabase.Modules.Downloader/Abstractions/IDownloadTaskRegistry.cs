using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

public interface IDownloadTaskRegistry
{
    Task<string> EnsureBindingAsync(DownloadTaskBinding binding, CancellationToken ct = default);
    Task<bool> IsUserStoppedAsync(string id, CancellationToken ct = default);
    Task SetUserStoppedAsync(string id, bool stopped, CancellationToken ct = default);
}
