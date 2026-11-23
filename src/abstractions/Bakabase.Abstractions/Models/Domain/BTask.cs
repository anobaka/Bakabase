using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

public record BTask
{
    public string Id { get; }
    private readonly Func<string> _getName;
    public string Name => _getName();
    private readonly Func<string?>? _getDescription;
    public string? Description => _getDescription?.Invoke();
    private readonly Func<string?>? _getMessageOnInterruption;
    public string? MessageOnInterruption => _getMessageOnInterruption?.Invoke();
    public DateTime CreatedAt { get; } = DateTime.Now;
    public HashSet<string>? ConflictKeys { get; }
    public string? ConcurrencyGroup { get; }
    [Newtonsoft.Json.JsonIgnore, System.Text.Json.Serialization.JsonIgnore]
    public Func<int>? GetConcurrencyLimit { get; }
    public HashSet<string>? DependsOn { get; }
    public BTaskLevel Level { get; }
    public string? Error { get; set; }
    private string? _briefError;
    private BTaskText? _briefErrorText;
    public string? BriefError { get => _briefErrorText?.ToString() ?? _briefError; set { _briefErrorText = null; _briefError = value; } }
    public TimeSpan? Interval { get; set; }
    public DateTime? EnableAfter { get; set; }
    public BTaskStatus Status { get; set; } = BTaskStatus.NotStarted;
    public DateTime? StartedAt { get; set; }
    public int Percentage { get; set; }
    private string? _message;
    private BTaskText? _messageText;
    public string? Message { get => _messageText?.ToString() ?? _message; set { _messageText = null; _message = value; } }
    public void SetMessage(BTaskText? text) { _messageText = text; _message = null; }
    private string? _process;
    private BTaskText? _processText;
    public string? Process { get => _processText?.ToString() ?? _process; set { _processText = null; _process = value; } }
    public void SetProcess(BTaskText? text) { _processText = text; _process = null; }
    public Dictionary<string, string?>? GetProcessTranslations() => _processText?.Translations();
    public DateTime? LastFinishedAt { get; set; }
    public bool IsPersistent { get; }
    public TimeSpan? Elapsed { get; set; }
    public BTaskType Type { get; set; }
    public BTaskResourceType ResourceType { get; set; }
    public object[]? ResourceKeys { get; set; }
    public object? Data { get; set; }
    public BTaskRetryPolicy? RetryPolicy { get; }
    public BTaskDependencyFailurePolicy DependencyFailurePolicy { get; }
    public int RetryCount { get; set; }
    public DateTime? NextRetryAt { get; set; }

    public void ClearError()
    {
        BriefError = null;
        Error = null;
    }

    public void SetError(string? briefError, string? error)
    {
        BriefError = briefError;
        Error = error;
    }

    public void SetLocalizedError(BTaskText? briefError, string? error)
    {
        _briefErrorText = briefError;
        _briefError = null;
        Error = error;
    }

    public BTask(string id,
        Func<string> getName, Func<string?>? getDescription = null,
        Func<string?>? getMessageOnInterruption = null,
        HashSet<string>? conflictKeys = null,
        HashSet<string>? dependsOn = null,
        BTaskLevel level = BTaskLevel.Default,
        bool isPersistent = false,
        BTaskType type = BTaskType.Any,
        BTaskResourceType resourceType = BTaskResourceType.Any,
        object[]? resourceKeys = null,
        BTaskRetryPolicy? retryPolicy = null,
        BTaskDependencyFailurePolicy dependencyFailurePolicy = BTaskDependencyFailurePolicy.Wait,
        string? concurrencyGroup = null, Func<int>? getConcurrencyLimit = null)
    {
        Id = id;
        _getName = getName;
        _getDescription = getDescription;
        _getMessageOnInterruption = getMessageOnInterruption;

        ConflictKeys = conflictKeys;
        ConcurrencyGroup = concurrencyGroup;
        GetConcurrencyLimit = getConcurrencyLimit;
        DependsOn = dependsOn;
        Level = level;
        IsPersistent = isPersistent;

        Type = type;
        ResourceType = resourceType;
        ResourceKeys = resourceKeys;

        RetryPolicy = retryPolicy;
        DependencyFailurePolicy = dependencyFailurePolicy;
    }
}
