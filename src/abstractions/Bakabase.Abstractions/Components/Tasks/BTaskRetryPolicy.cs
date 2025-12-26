namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>
/// Defines retry behavior for failed tasks
/// </summary>
public record BTaskRetryPolicy
{
    /// <summary>
    /// No retries - task fails immediately on error
    /// </summary>
    public static BTaskRetryPolicy None => new() { MaxRetries = 0 };

    /// <summary>
    /// Default retry policy with exponential backoff
    /// </summary>
    public static BTaskRetryPolicy Default => new()
    {
        MaxRetries = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        BackoffMultiplier = 2,
        MaxDelay = TimeSpan.FromMinutes(5)
    };

    /// <summary>
    /// Maximum number of retry attempts. 0 means no retries.
    /// </summary>
    public int MaxRetries { get; init; } = 0;

    /// <summary>
    /// Initial delay before the first retry
    /// </summary>
    public TimeSpan InitialDelay { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Multiplier for exponential backoff. Each retry waits InitialDelay * (BackoffMultiplier ^ retryCount)
    /// </summary>
    public double BackoffMultiplier { get; init; } = 2;

    /// <summary>
    /// Maximum delay between retries (caps the exponential growth)
    /// </summary>
    public TimeSpan MaxDelay { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// Calculate the delay for the given retry attempt (0-indexed)
    /// </summary>
    public TimeSpan GetDelayForRetry(int retryCount)
    {
        var delay = InitialDelay * Math.Pow(BackoffMultiplier, retryCount);
        return TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds, MaxDelay.TotalMilliseconds));
    }
}
