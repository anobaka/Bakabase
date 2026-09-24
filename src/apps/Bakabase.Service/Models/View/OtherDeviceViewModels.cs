using System;
using System.Collections.Generic;

namespace Bakabase.Service.Models.View
{
    /// <summary>
    /// Everything that can be installed on a device other than this one.
    /// </summary>
    /// <remarks>
    /// A wrapper rather than the mobile packages alone, so another product can join them
    /// without changing the endpoint's shape. <see cref="Mobile"/> is null when its manifest
    /// could not be reached — an offline host, a blocked CDN, or nothing published yet all
    /// look alike, and the page explains rather than erroring.
    /// </remarks>
    public record OtherDeviceDownloadsViewModel
    {
        public MobileAppDownloadsViewModel? Mobile { get; set; }
    }

    /// <summary>
    /// The latest published mobile packages, as CI recorded them in the
    /// download manifest.
    /// </summary>
    public record MobileAppDownloadsViewModel
    {
        public string Version { get; set; } = null!;

        public DateTime? PublishedAt { get; set; }

        /// <summary>The GitHub release these packages were published on.</summary>
        public string? ReleaseUrl { get; set; }

        /// <summary>The SideStore source users add once for iOS auto-updates.</summary>
        public string? SidestoreSourceUrl { get; set; }

        public List<MobileAppDownloadFileViewModel> Files { get; set; } = [];
    }

    public record MobileAppDownloadFileViewModel
    {
        public string Name { get; set; } = null!;

        /// <summary>e.g. <c>android-arm64-v8a</c> or <c>ios</c>.</summary>
        public string Platform { get; set; } = null!;

        public long Size { get; set; }

        public string? GithubUrl { get; set; }

        public string? CdnUrl { get; set; }
    }
}
