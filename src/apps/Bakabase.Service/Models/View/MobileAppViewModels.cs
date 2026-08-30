using System;
using System.Collections.Generic;

namespace Bakabase.Service.Models.View
{
    /// <summary>
    /// The latest published mobile packages, as CI recorded them in the
    /// download manifest. Null data on the endpoint means the manifest could
    /// not be reached (offline host, or nothing published yet).
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
