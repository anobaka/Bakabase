using System;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;

/// <summary>Optional, current-operation byte counters. These are not a committed file manifest.</summary>
public sealed record DownloadTransferProgress(long? CompletedBytes, long? TotalBytes, DateTime ObservedAt);
