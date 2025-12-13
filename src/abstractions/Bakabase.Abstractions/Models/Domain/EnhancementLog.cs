namespace Bakabase.Abstractions.Models.Domain;

public record EnhancementLog
{
    /// <summary>
    /// Log timestamp
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Log level: Information, Warning, Error
    /// </summary>
    public string Level { get; set; } = null!;

    /// <summary>
    /// Event type/category
    /// </summary>
    public string Event { get; set; } = null!;

    /// <summary>
    /// Log message
    /// </summary>
    public string Message { get; set; } = null!;

    /// <summary>
    /// Additional data (will be JSON serialized)
    /// </summary>
    public object? Data { get; set; }
}
