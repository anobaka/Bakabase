using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>
/// Manages dynamic registration and unregistration of tasks based on configuration changes
/// </summary>
public class DynamicTaskRegistry : IDisposable
{
    private readonly BTaskManager _taskManager;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DynamicTaskRegistry> _logger;
    private readonly List<IPredefinedBTaskBuilder> _taskBuilders = [];
    private readonly Dictionary<string, bool> _taskStates = [];
    private readonly List<IDisposable> _optionsChangeHandlers = [];

    public DynamicTaskRegistry(
        BTaskManager taskManager,
        IServiceProvider serviceProvider,
        ILogger<DynamicTaskRegistry> logger)
    {
        _taskManager = taskManager;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Discovers and registers all IPredefinedBTaskBuilder implementations from DI container.
    /// </summary>
    public async Task RegisterAllTasksAsync()
    {
        var taskBuilders = _serviceProvider.GetServices<IPredefinedBTaskBuilder>().ToList();

        _logger.LogInformation($"Found {taskBuilders.Count} predefined task(s)");

        // First, register all task builders to _taskBuilders list
        foreach (var builder in taskBuilders)
        {
            _logger.LogInformation($"Registering task: {builder.Id}");

            // Check for circular dependencies before registering
            if (HasCircularDependency(builder.Id, builder.DependsOn, [builder.Id]))
            {
                _logger.LogError($"Cannot register task [{builder.Id}]: circular dependency detected");
                throw new InvalidOperationException($"Task [{builder.Id}] has circular dependencies");
            }

            _taskBuilders.Add(builder);

            // Register change handlers for all watched option types
            foreach (var optionsType in builder.WatchedOptionsTypes)
            {
                RegisterOptionsChangeHandler(optionsType, builder);
            }
        }

        // Then, evaluate and enqueue all tasks together to ensure dependencies are registered
        await Task.WhenAll(taskBuilders.Select(EvaluateAndUpdateTaskAsync));
    }

    /// <summary>
    /// Register a task builder and start monitoring it
    /// </summary>
    public void RegisterTask(IPredefinedBTaskBuilder builder)
    {
        _logger.LogInformation($"Registering task: {builder.Id}");

        // Check for circular dependencies before registering
        if (HasCircularDependency(builder.Id, builder.DependsOn, [builder.Id]))
        {
            _logger.LogError($"Cannot register task [{builder.Id}]: circular dependency detected");
            throw new InvalidOperationException($"Task [{builder.Id}] has circular dependencies");
        }

        _taskBuilders.Add(builder);

        // Register change handlers for all watched option types
        foreach (var optionsType in builder.WatchedOptionsTypes)
        {
            RegisterOptionsChangeHandler(optionsType, builder);
        }

        // Immediately evaluate and update the task
        _ = EvaluateAndUpdateTaskAsync(builder);
    }

    /// <summary>
    /// Checks if adding a task with the given dependencies would create a circular dependency
    /// </summary>
    /// <param name="taskId">The task being checked</param>
    /// <param name="dependsOn">The dependencies of the task</param>
    /// <param name="visited">Set of already visited task IDs in the current path</param>
    /// <returns>True if a circular dependency would be created</returns>
    private bool HasCircularDependency(string taskId, HashSet<string>? dependsOn, HashSet<string> visited)
    {
        if (dependsOn == null || dependsOn.Count == 0)
            return false;

        foreach (var depId in dependsOn)
        {
            // If we've already visited this dependency in the current path, we have a cycle
            if (visited.Contains(depId))
            {
                _logger.LogWarning($"Circular dependency detected: {taskId} -> ... -> {depId}");
                return true;
            }

            // Find the dependency builder
            var depBuilder = _taskBuilders.FirstOrDefault(b => b.Id == depId);
            if (depBuilder != null)
            {
                // Add to visited set and recursively check
                var newVisited = new HashSet<string>(visited) { depId };
                if (HasCircularDependency(depId, depBuilder.DependsOn, newVisited))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Evaluate task state and update accordingly (enqueue or clean)
    /// </summary>
    private async Task EvaluateAndUpdateTaskAsync(IPredefinedBTaskBuilder builder)
    {
        try
        {
            var shouldBeEnabled = builder.IsEnabled();
            var currentlyEnabled = _taskStates.GetValueOrDefault(builder.Id, false);

            _logger.LogDebug(
                $"Evaluating task [{builder.Id}]: shouldBeEnabled={shouldBeEnabled}, currentlyEnabled={currentlyEnabled}");

            if (shouldBeEnabled && !currentlyEnabled)
            {
                // Enable task: add to queue
                await EnableTaskAsync(builder);
            }
            else if (!shouldBeEnabled && currentlyEnabled)
            {
                // Disable task: remove from queue
                await DisableTaskAsync(builder);
            }
            else if (shouldBeEnabled && currentlyEnabled)
            {
                // Task is already enabled, but interval might have changed
                // The BTaskManager's OnChange handler will automatically handle interval updates
                _logger.LogDebug($"Task [{builder.Id}] is already enabled, checking for interval updates");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error evaluating task [{builder.Id}]");
        }
    }

    private async Task EnableTaskAsync(IPredefinedBTaskBuilder builder)
    {
        _logger.LogInformation($"Enabling task: {builder.Id}");

        var handlerBuilder = new BTaskHandlerBuilder
        {
            Id = builder.Id,
            GetName = builder.GetName,
            GetDescription = builder.GetDescription,
            GetMessageOnInterruption = builder.GetMessageOnInterruption,
            Run = builder.RunAsync,
            ConflictKeys = builder.ConflictKeys,
            DependsOn = builder.DependsOn,
            Level = builder.Level,
            Interval = builder.GetInterval(),
            IsPersistent = builder.IsPersistent,
            Type = builder.Type,
            ResourceType = builder.ResourceType,
            RetryPolicy = builder.RetryPolicy,
            DependencyFailurePolicy = builder.DependencyFailurePolicy
        };

        await _taskManager.Enqueue(handlerBuilder);
        _taskStates[builder.Id] = true;
    }

    private async Task DisableTaskAsync(IPredefinedBTaskBuilder builder)
    {
        _logger.LogInformation($"Disabling task: {builder.Id}");

        // Stop the task if it's running
        await _taskManager.Stop(builder.Id);

        // Remove from task manager
        await _taskManager.Clean(builder.Id);

        _taskStates[builder.Id] = false;
    }

    /// <summary>
    /// Register an options change handler for a specific options type
    /// </summary>
    private void RegisterOptionsChangeHandler(Type optionsType, IPredefinedBTaskBuilder builder)
    {
        try
        {
            // Get AspNetCoreOptionsManager<TOptions> using reflection
            var optionsManagerType = typeof(AspNetCoreOptionsManager<>).MakeGenericType(optionsType);
            var optionsManager = _serviceProvider.GetService(optionsManagerType);

            if (optionsManager == null)
            {
                _logger.LogWarning(
                    $"Could not find AspNetCoreOptionsManager for type {optionsType.Name}, task [{builder.Id}] will not respond to configuration changes");
                return;
            }

            // Get the OnChange method
            var onChangeMethod = optionsManagerType.GetMethod("OnChange", new[] { typeof(Action<>).MakeGenericType(optionsType) });

            if (onChangeMethod == null)
            {
                _logger.LogWarning(
                    $"Could not find OnChange method for type {optionsType.Name}, task [{builder.Id}] will not respond to configuration changes");
                return;
            }

            // Create callback delegate
            var callbackType = typeof(Action<>).MakeGenericType(optionsType);
            var callback = Delegate.CreateDelegate(
                callbackType,
                this,
                GetType().GetMethod(nameof(OnOptionsChanged), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(optionsType));

            // Create a closure to capture the builder
            var parameters = new object[] { callback };
            var closureCallback = new Action<object>(_ =>
            {
                _ = EvaluateAndUpdateTaskAsync(builder);
            });

            // Invoke OnChange to register the handler
            var handler = onChangeMethod.Invoke(optionsManager, new object[]
            {
                Delegate.CreateDelegate(callbackType, closureCallback.Target, closureCallback.Method)
            }) as IDisposable;

            if (handler != null)
            {
                _optionsChangeHandlers.Add(handler);
                _logger.LogDebug(
                    $"Registered options change handler for task [{builder.Id}] on type {optionsType.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"Error registering options change handler for task [{builder.Id}] on type {optionsType.Name}");
        }
    }

    private void OnOptionsChanged<TOptions>(TOptions options)
    {
        // This method is used for reflection to create properly typed delegates
        // The actual logic is handled by the closure in RegisterOptionsChangeHandler
    }

    public void Dispose()
    {
        _logger.LogInformation("Disposing DynamicTaskRegistry");

        foreach (var handler in _optionsChangeHandlers)
        {
            try
            {
                handler?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing options change handler");
            }
        }

        _optionsChangeHandlers.Clear();
    }
}
