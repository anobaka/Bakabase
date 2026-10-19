namespace Bakabase.Modules.HttpDownloads.Models;

public sealed class HttpDownloadJobException(HttpDownloadJob job)
    : InvalidOperationException(job.Error ?? $"HTTP download is {job.Status}.")
{
    public HttpDownloadJob Job { get; } = job;
}
