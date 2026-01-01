namespace Bakabase.Modules.Enhancer.Models.Domain.Constants;

/// <summary>
/// Constants for enhancement log event types.
/// </summary>
public static class EnhancementLogEvent
{
    /// <summary>
    /// Enhancement process started.
    /// </summary>
    public const string Started = "Started";

    /// <summary>
    /// Configuration information logged.
    /// </summary>
    public const string Configuration = "Configuration";

    /// <summary>
    /// Keyword resolved from property or filename.
    /// </summary>
    public const string KeywordResolved = "KeywordResolved";

    /// <summary>
    /// Keyword pretreated (special text processing).
    /// </summary>
    public const string KeywordPretreated = "KeywordPretreated";

    /// <summary>
    /// Data fetching started (HTTP request, API call, etc.).
    /// </summary>
    public const string DataFetching = "DataFetching";

    /// <summary>
    /// Data fetched successfully.
    /// </summary>
    public const string DataFetched = "DataFetched";

    /// <summary>
    /// Context built from fetched data.
    /// </summary>
    public const string ContextBuilt = "ContextBuilt";

    /// <summary>
    /// Target value converted.
    /// </summary>
    public const string TargetConverted = "TargetConverted";

    /// <summary>
    /// File saved (cover, attachment, etc.).
    /// </summary>
    public const string FileSaved = "FileSaved";

    /// <summary>
    /// Enhancement process completed.
    /// </summary>
    public const string Completed = "Completed";

    /// <summary>
    /// Error occurred during enhancement.
    /// </summary>
    public const string Error = "Error";

    /// <summary>
    /// Regex pattern matched.
    /// </summary>
    public const string RegexMatched = "RegexMatched";

    /// <summary>
    /// Search results found.
    /// </summary>
    public const string SearchResults = "SearchResults";

    /// <summary>
    /// HTTP request sent to external source.
    /// </summary>
    public const string HttpRequest = "HttpRequest";

    /// <summary>
    /// HTTP response received from external source.
    /// </summary>
    public const string HttpResponse = "HttpResponse";
}
