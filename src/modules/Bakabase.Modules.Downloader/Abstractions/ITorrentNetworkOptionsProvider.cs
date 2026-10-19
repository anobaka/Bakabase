using Bakabase.Modules.Downloader.Models;

namespace Bakabase.Modules.Downloader.Abstractions;

/// <summary>The host resolves saved proxy identities and fails on unavailable/invalid selections.</summary>
public interface ITorrentNetworkOptionsProvider
{
    TorrentNetworkOptions GetOptions();
}
