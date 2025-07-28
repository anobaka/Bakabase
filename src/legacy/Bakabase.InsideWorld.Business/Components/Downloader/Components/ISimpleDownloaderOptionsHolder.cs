using Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Models;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Components;

public interface ISimpleDownloaderOptionsHolder
{
    /// <summary>
    /// this property is optional, may be removed later.
    /// </summary>
    string? Cookie { get; set; }
        
    /// <summary>
    /// Number of concurrent download threads
    /// </summary>
    public int MaxConcurrency { get; set; }
        
    /// <summary>
    /// Interval between requests in milliseconds
    /// </summary>
    public int RequestInterval { get; set; }
        
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
    public bool SkipExisting { get; set; }
        
    /// <summary>
    /// Maximum retry attempts for failed downloads
    /// </summary>
    public int MaxRetries { get; set; }
        
    /// <summary>
    /// Timeout for HTTP requests in seconds
    /// </summary>
    public int RequestTimeout { get; set; }
}