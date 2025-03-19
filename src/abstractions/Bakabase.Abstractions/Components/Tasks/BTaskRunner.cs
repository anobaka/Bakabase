using System.Collections.Concurrent;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Input;

namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskRunner
{
    public string Key { get; }
    private Func<BTaskArgs, Task> _run;
    public Func<string> GetName { get; }
    public Func<string?>? GetDescription { get; }
    public Func<string?>? GetMessageOnInterruption { get; }
    public HashSet<string>? ConflictWithTaskKeys { get; }
    public bool IgnoreConflicts { get; }
    public BTaskLevel Level { get; }
    public bool StopPrevious { get; }
    private ConcurrentQueue<BTaskEvent<int>> _percentageEvents = [];
    private ConcurrentQueue<BTaskEvent<string>> _processEvents = [];
    private readonly CancellationTokenSource _cts = new CancellationTokenSource();
    private readonly CancellationToken _ct;

    public BTaskRunner(BTaskRunInputModel model)
    {
        Key = key;
        _run = run;
        GetName = name ?? (Func<string>) (() => key);
        GetDescription = getDescription;
        GetMessageOnInterruption = getMessageOnInterruption;
        ConflictWithTaskKeys = conflictWithTaskKeys;
        IgnoreConflicts = ignoreConflicts;
        Level = level;
        StopPrevious = stopPrevious;

        if (ct.HasValue)
        {
            var mixedCts = CancellationTokenSource.CreateLinkedTokenSource(ct.Value, _cts.Token);
            _ct = mixedCts.Token;
        }
        else
        {
            _ct = _cts.Token;
        }
    }

    public async Task Run()
    {

    }

    public BTask Task
    {
        get
        {
            return new BTask
            {
                Key = Key,
                Name = GetName(),
                Description = GetDescription?.Invoke(),
                MessageOnInterruption = GetMessageOnInterruption?.Invoke(),
                ConflictWithTaskKeys = ConflictWithTaskKeys,
                EnableAfter = 
            }
        }
    }

    public async Task Stop()
    {
        await _cts.CancelAsync();
    }

    public BTaskStatus Status { get; }
}