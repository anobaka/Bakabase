namespace Bakabase.Modules.Bittorrent.Models;

public sealed class BtDownloadJobException(BtDownloadJob job)
    : InvalidOperationException(job.Error ?? $"BitTorrent download is {job.Status}.")
{
    public BtDownloadJob Job { get; } = job;
}
