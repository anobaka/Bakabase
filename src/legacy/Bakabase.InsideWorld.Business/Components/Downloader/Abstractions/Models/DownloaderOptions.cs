using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models
{
    /// <summary>
    /// Unified downloader options for all platforms
    /// </summary>
    public record DownloaderOptions
    {
        /// <summary>
        /// Authentication cookie for requests
        /// </summary>
        public string? Cookie { get; set; }
        
        /// <summary>
        /// Number of concurrent download threads
        /// </summary>
        public int MaxConcurrency { get; set; } = 1;
        
        /// <summary>
        /// Interval between requests in milliseconds
        /// </summary>
        public int RequestInterval { get; set; } = 1000;
        
        /// <summary>
        /// Default download path
        /// </summary>
        public string? DefaultPath { get; set; }
        
        /// <summary>
        /// Default file naming convention
        /// </summary>
        public string? NamingConvention { get; set; }
        
        /// <summary>
        /// Whether to skip existing files
        /// </summary>
        public bool SkipExisting { get; set; } = true;
        
        /// <summary>
        /// Maximum retry attempts for failed downloads
        /// </summary>
        public int MaxRetries { get; set; } = 3;
        
        /// <summary>
        /// Timeout for HTTP requests in seconds
        /// </summary>
        public int RequestTimeout { get; set; } = 30;
    }
}