using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Modules.Enhancer.Components;

/// <summary>
/// Collects enhancement logs during the enhancement process.
/// </summary>
public class EnhancementLogCollector
{
    private readonly List<EnhancementLog> _logs = new();

    /// <summary>
    /// Log a message with specified level.
    /// </summary>
    public void Log(string level, string eventName, string message, object? data = null)
    {
        _logs.Add(new EnhancementLog
        {
            Timestamp = DateTime.Now,
            Level = level,
            Event = eventName,
            Message = message,
            Data = data
        });
    }

    /// <summary>
    /// Log an information message.
    /// </summary>
    public void LogInfo(string eventName, string message, object? data = null)
        => Log("Information", eventName, message, data);

    /// <summary>
    /// Log a warning message.
    /// </summary>
    public void LogWarning(string eventName, string message, object? data = null)
        => Log("Warning", eventName, message, data);

    /// <summary>
    /// Log an error message.
    /// </summary>
    public void LogError(string eventName, string message, object? data = null)
        => Log("Error", eventName, message, data);

    /// <summary>
    /// Get all collected logs.
    /// </summary>
    public List<EnhancementLog> GetLogs() => _logs.ToList();

    /// <summary>
    /// Clear all collected logs.
    /// </summary>
    public void Clear() => _logs.Clear();

    /// <summary>
    /// Get the number of collected logs.
    /// </summary>
    public int Count => _logs.Count;
}
