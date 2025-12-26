---
globs:
  - "**/Components/Tasks/**"
  - "**/BTask*.cs"
  - "**/IPredefinedBTaskBuilder.cs"
  - "**/AbstractPredefinedBTaskBuilder.cs"
  - "**/DynamicTaskRegistry.cs"
  - "**/*Task.cs"
---

# BTask Background Task System

## Overview

BTask is a background task management system that supports scheduling, progress tracking, pause/resume, conflict detection, task dependencies, and real-time UI updates via SignalR.

## Architecture

```
BTaskManager (daemon loop every 1s)
    ├── ConcurrentDictionary<string, BTaskHandler>
    ├── Auto-starts tasks when EnableAfter reached
    ├── Conflict detection via ConflictKeys
    └── Dependency checking via DependsOn

DynamicTaskRegistry (upper-level, registered separately)
    ├── Discovers IPredefinedBTaskBuilder implementations via DI
    ├── Monitors configuration changes
    ├── Enables/disables tasks based on IsEnabled()
    └── Updates task intervals dynamically

IPredefinedBTaskBuilder (interface)
    └── Implemented by task classes in each module

AbstractPredefinedBTaskBuilder (abstract class)
    └── Common functionality: localization, service scope
```

## Core Components

| Component | Location | Purpose |
|-----------|----------|---------|
| BTask | `abstractions/.../Models/Domain/BTask.cs` | Task state record |
| BTaskHandler | `abstractions/.../Components/Tasks/BTaskHandler.cs` | Execution wrapper |
| BTaskHandlerBuilder | `abstractions/.../Components/Tasks/BTaskHandlerBuilder.cs` | Fluent builder |
| BTaskManager | `abstractions/.../Components/Tasks/BTaskManager.cs` | Central manager |
| DynamicTaskRegistry | `abstractions/.../Components/Tasks/DynamicTaskRegistry.cs` | Dynamic registration |
| IPredefinedBTaskBuilder | `abstractions/.../Components/Tasks/IPredefinedBTaskBuilder.cs` | Interface for predefined tasks |
| AbstractPredefinedBTaskBuilder | `abstractions/.../Components/Tasks/AbstractPredefinedBTaskBuilder.cs` | Abstract base class |

## Task Lifecycle

```
NotStarted → Running → Completed
           ↓         ↓
         Paused ─────┘
           ↓
        Running → Cancelled / Error
```

## Creating a New Predefined Task

### 1. Create a Task Class

Create a class that extends `AbstractPredefinedBTaskBuilder` in the appropriate module:

```csharp
using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace MyModule.Components.Tasks;

public class MyTask : AbstractPredefinedBTaskBuilder
{
    public MyTask(IServiceProvider serviceProvider, IBakabaseLocalizer localizer)
        : base(serviceProvider, localizer)
    {
    }

    public override string Id => "MyTask";

    public override bool IsEnabled() => true;

    // Optional: Override for one-time tasks (return null) or custom intervals
    public override TimeSpan? GetInterval() => TimeSpan.FromMinutes(1);

    // Optional: Override to watch configuration changes
    public override Type[] WatchedOptionsTypes => [typeof(MyOptions)];

    // Optional: Override for task dependencies
    public override HashSet<string>? DependsOn => ["OtherTaskId"];

    public override async Task RunAsync(BTaskArgs args)
    {
        await using var scope = CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<IMyService>();
        await service.DoWork(
            async (percentage, process) =>
            {
                await args.UpdateTask(t =>
                {
                    t.Percentage = percentage;
                    t.Process = process;
                });
            },
            args.CancellationToken);
    }
}
```

### 2. Registration

Tasks are automatically discovered and registered via `AddBTask<TBTaskEventHandler>()`:

```csharp
// In Startup/ConfigureServices
services.AddBTask<BTaskEventHandler>(); // Discovers and registers all IPredefinedBTaskBuilder implementations
services.AddSingleton<DynamicTaskRegistry>(); // Register separately (upper-level component)

// In host initialization
dynamicTaskRegistry.RegisterAllTasks();
```

### 3. Add Localization

Add entries in localization files:
- `BTask_Name_{TaskId}`
- `BTask_Description_{TaskId}`
- `BTask_MessageOnInterruption_{TaskId}`

## Built-in Tasks

| Task ID | Location | Purpose | Type | Enable Condition |
|---------|----------|---------|------|------------------|
| Enhancement | Enhancer module | Enhance resources with metadata | Recurring | Always |
| PrepareCache | Bakabase.Service | Pre-generate thumbnails | Recurring | Cache not disabled |
| MoveFiles | FileMover component | Move files per configuration | Recurring | FileMover enabled |
| SearchIndex | Search/Index component | Build search index | One-time | Always |
| ResourceProfileIndex | Services | Build profile index | One-time | Depends on SearchIndex |
| GenerateResourceMarker | Bakabase.Service | Create marker files | Recurring | KeepResourcesOnPathChange |

## Key Interfaces & Classes

### IPredefinedBTaskBuilder

| Property/Method | Type | Default | Description |
|-----------------|------|---------|-------------|
| `Id` | string | - | Unique identifier (required) |
| `IsEnabled()` | bool | - | Should task be enabled (required) |
| `RunAsync(args)` | Task | - | Execution logic (required) |
| `GetInterval()` | TimeSpan? | 1 min | null = one-time, TimeSpan = recurring |
| `WatchedOptionsTypes` | Type[] | [] | Config types that trigger re-evaluation |
| `GetName()` | string | - | Task display name |
| `GetDescription()` | string? | null | Task description |
| `GetMessageOnInterruption()` | string? | null | Message shown on interruption |
| `Type` | BTaskType | Any | Task type |
| `ResourceType` | BTaskResourceType | Any | Resource type |
| `ConflictKeys` | HashSet\<string\>? | null | Keys for conflict detection |
| `DependsOn` | HashSet\<string\>? | null | Task IDs that must complete first |
| `Level` | BTaskLevel | Default | Default or Critical |
| `IsPersistent` | bool | true | Stays in task list after completion |
| `RetryPolicy` | BTaskRetryPolicy? | null | Retry configuration for failed tasks |
| `DependencyFailurePolicy` | BTaskDependencyFailurePolicy | Wait | Behavior when dependencies fail |

### AbstractPredefinedBTaskBuilder

Provides common functionality:
- `ServiceProvider` field for DI
- `Localizer` field for localization
- Default implementations for `GetName()`, `GetDescription()`, `GetMessageOnInterruption()` using localization
- Default `ConflictKeys => [Id]`
- `CreateScope()` helper method

### BTaskArgs (passed to RunAsync)

| Property | Description |
|----------|-------------|
| `CancellationToken` | Check for cancellation |
| `PauseToken` | Support pause/resume |
| `UpdateTask(Action<BTask>)` | Update progress/state |
| `RootServiceProvider` | For DI (create scopes) |

## Progress Reporting Pattern

```csharp
public override async Task RunAsync(BTaskArgs args)
{
    await using var scope = CreateScope();
    var items = await GetItems();
    var processed = 0;

    foreach (var item in items)
    {
        args.CancellationToken.ThrowIfCancellationRequested();
        await args.PauseToken.WaitIfPaused();

        await ProcessItem(item);
        processed++;

        await args.UpdateTask(t =>
        {
            t.Percentage = processed * 100 / items.Count;
            t.Process = $"{processed}/{items.Count}";
        });
    }
}
```

## Conflict Detection

Tasks with overlapping `ConflictKeys` cannot run simultaneously:

```csharp
// Task A
public override HashSet<string>? ConflictKeys => ["DatabaseWrite", "IndexRebuild"];

// Task B
public override HashSet<string>? ConflictKeys => ["IndexRebuild"];

// A and B cannot run at the same time due to "IndexRebuild"
```

## Task Dependencies

Tasks can declare dependencies on other tasks using `DependsOn`. A task will not start until all its dependencies have completed successfully.

```csharp
public override HashSet<string>? DependsOn => ["SearchIndex"]; // Waits for SearchIndex
```

**Behavior:**
- Task waits for all dependency tasks to reach `Completed` status
- If dependency task is not registered, a warning is logged but task is not blocked
- Circular dependencies are automatically detected and rejected at registration time
- Dependency failure behavior is configurable via `DependencyFailurePolicy`

### DependencyFailurePolicy

```csharp
public enum BTaskDependencyFailurePolicy
{
    Wait = 1,   // Continue waiting for dependency to succeed (default)
    Skip = 2,   // Skip failed dependencies, proceed if non-failed ones complete
    Fail = 3    // Mark this task as failed when any dependency fails
}

// Example usage:
public override BTaskDependencyFailurePolicy DependencyFailurePolicy => BTaskDependencyFailurePolicy.Fail;
```

**Current Dependencies:**
| Task | Depends On |
|------|-----------|
| ResourceProfileIndex | SearchIndex |

## Retry Mechanism

Tasks can configure automatic retry on failure using `BTaskRetryPolicy`:

```csharp
public record BTaskRetryPolicy
{
    public static BTaskRetryPolicy None => new() { MaxRetries = 0 };
    public static BTaskRetryPolicy Default => new()
    {
        MaxRetries = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        BackoffMultiplier = 2,
        MaxDelay = TimeSpan.FromMinutes(5)
    };

    public int MaxRetries { get; init; }           // Max retry attempts (0 = no retries)
    public TimeSpan InitialDelay { get; init; }    // Delay before first retry
    public double BackoffMultiplier { get; init; } // Exponential backoff multiplier
    public TimeSpan MaxDelay { get; init; }        // Maximum delay cap
}

// Example usage:
public override BTaskRetryPolicy? RetryPolicy => BTaskRetryPolicy.Default;

// Or custom policy:
public override BTaskRetryPolicy? RetryPolicy => new()
{
    MaxRetries = 5,
    InitialDelay = TimeSpan.FromSeconds(2),
    BackoffMultiplier = 1.5,
    MaxDelay = TimeSpan.FromMinutes(10)
};
```

**Retry Behavior:**
- On failure, task status is set to `NotStarted` with `NextRetryAt` scheduled
- Daemon automatically picks up task when retry time is reached
- Retry count is tracked and incremented with each attempt
- After max retries exhausted, task status is set to `Error`

## One-Time vs Recurring Tasks

| Type | GetInterval() Return | Behavior |
|------|---------------------|----------|
| One-time | null | Runs once at startup when enabled |
| Recurring | TimeSpan | Runs repeatedly at interval |

## Configuration Watching

Tasks can react to configuration changes by overriding `WatchedOptionsTypes`:

```csharp
public override Type[] WatchedOptionsTypes => [typeof(FileSystemOptions)];

public override bool IsEnabled()
{
    var fsOptions = ServiceProvider.GetRequiredService<IBOptions<FileSystemOptions>>();
    return fsOptions.Value.FileMover?.Enabled ?? false;
}
```

When `FileSystemOptions` changes, `IsEnabled()` is re-evaluated and task is enabled/disabled accordingly.

## Best Practices

### DO

1. Always check `CancellationToken` in loops
2. Report progress frequently via `UpdateTask`
3. Use `CreateScope()` for scoped services
4. Set appropriate `ConflictKeys` to prevent data corruption
5. Use `Level = BTaskLevel.Critical` for tasks that must complete before app exit
6. Add `using System;` and `using System.Threading.Tasks;` to task files

### DON'T

1. Don't capture service instances in closures - use `ServiceProvider` instead
2. Don't ignore `PauseToken` - call `WaitIfPaused()` at safe points
3. Don't set `IsPersistent = false` for tasks users need to track
4. Don't use overly broad `ConflictKeys` - it reduces parallelism

## Error Handling

```csharp
public override async Task RunAsync(BTaskArgs args)
{
    try
    {
        // Task logic
    }
    catch (OperationCanceledException)
    {
        throw; // Let handler set Cancelled status
    }
    catch (Exception ex)
    {
        // For custom error display, throw BTaskException
        throw new BTaskException("Brief message for UI", ex);
    }
}
```

## UI Integration

Tasks are pushed to the frontend via SignalR:
- `WebGuiHub.GetData("BTask", allTasks)` - Full sync
- `WebGuiHub.GetIncrementalData("BTask", task)` - Single task update

Updates are batched every 500ms to reduce UI thrashing.

## Known Limitations

1. **No priority queue** - All eligible tasks compete equally

## Recent Improvements

1. **Retry Mechanism** - Configurable retry policy with exponential backoff
2. **Circular Dependency Detection** - Automatically detected and rejected at registration
3. **Dependency Failure Policy** - Configurable behavior when dependencies fail
4. **Improved DisposeAsync** - Properly waits for Critical tasks to complete

## Future Improvements (TODO)

1. Add `Priority` for scheduling order
