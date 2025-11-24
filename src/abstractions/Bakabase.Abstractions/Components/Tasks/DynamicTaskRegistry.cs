using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
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
    private readonly List<PredefinedTaskDefinition> _taskDefinitions = [];
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
    /// Register a task definition and start monitoring it
    /// </summary>
    public void RegisterTask(PredefinedTaskDefinition definition)
    {
        _logger.LogInformation($"Registering task definition: {definition.Id}");
        _taskDefinitions.Add(definition);

        // Register change handlers for all watched option types
        foreach (var optionsType in definition.WatchedOptionsTypes)
        {
            RegisterOptionsChangeHandler(optionsType, definition);
        }

        // Immediately evaluate and update the task
        _ = EvaluateAndUpdateTaskAsync(definition);
    }

    /// <summary>
    /// Evaluate task state and update accordingly (enqueue or clean)
    /// </summary>
    private async Task EvaluateAndUpdateTaskAsync(PredefinedTaskDefinition definition)
    {
        try
        {
            var shouldBeEnabled = definition.IsEnabled();
            var currentlyEnabled = _taskStates.GetValueOrDefault(definition.Id, false);

            _logger.LogDebug(
                $"Evaluating task [{definition.Id}]: shouldBeEnabled={shouldBeEnabled}, currentlyEnabled={currentlyEnabled}");

            if (shouldBeEnabled && !currentlyEnabled)
            {
                // Enable task: add to queue
                await EnableTaskAsync(definition);
            }
            else if (!shouldBeEnabled && currentlyEnabled)
            {
                // Disable task: remove from queue
                await DisableTaskAsync(definition);
            }
            else if (shouldBeEnabled && currentlyEnabled)
            {
                // Task is already enabled, but interval might have changed
                // The BTaskManager's OnChange handler will automatically handle interval updates
                _logger.LogDebug($"Task [{definition.Id}] is already enabled, checking for interval updates");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, $"Error evaluating task [{definition.Id}]");
        }
    }

    private async Task EnableTaskAsync(PredefinedTaskDefinition definition)
    {
        _logger.LogInformation($"Enabling task: {definition.Id}");

        var builder = definition.BuildHandler(_serviceProvider);

        // Apply dynamic interval if provided
        if (definition.GetInterval != null)
        {
            var interval = definition.GetInterval();
            if (interval.HasValue)
            {
                // Create a new builder with updated interval
                builder = new BTaskHandlerBuilder
                {
                    Id = builder.Id,
                    GetName = builder.GetName,
                    GetDescription = builder.GetDescription,
                    GetMessageOnInterruption = builder.GetMessageOnInterruption,
                    CancellationToken = builder.CancellationToken,
                    Run = builder.Run,
                    ConflictKeys = builder.ConflictKeys,
                    Level = builder.Level,
                    Interval = interval,
                    IsPersistent = builder.IsPersistent,
                    RootServiceProvider = builder.RootServiceProvider,
                    OnStatusChange = builder.OnStatusChange,
                    OnPercentageChanged = builder.OnPercentageChanged,
                    Type = builder.Type,
                    ResourceType = builder.ResourceType,
                    ResourceKeys = builder.ResourceKeys,
                    StartNow = builder.StartNow,
                    DuplicateIdHandling = builder.DuplicateIdHandling
                };
            }
        }

        await _taskManager.Enqueue(builder);
        _taskStates[definition.Id] = true;
    }

    private async Task DisableTaskAsync(PredefinedTaskDefinition definition)
    {
        _logger.LogInformation($"Disabling task: {definition.Id}");

        // Stop the task if it's running
        await _taskManager.Stop(definition.Id);

        // Remove from task manager
        await _taskManager.Clean(definition.Id);

        _taskStates[definition.Id] = false;
    }

    /// <summary>
    /// Register an options change handler for a specific options type
    /// </summary>
    private void RegisterOptionsChangeHandler(Type optionsType, PredefinedTaskDefinition definition)
    {
        try
        {
            // Get AspNetCoreOptionsManager<TOptions> using reflection
            var optionsManagerType = typeof(AspNetCoreOptionsManager<>).MakeGenericType(optionsType);
            var optionsManager = _serviceProvider.GetService(optionsManagerType);

            if (optionsManager == null)
            {
                _logger.LogWarning(
                    $"Could not find AspNetCoreOptionsManager for type {optionsType.Name}, task [{definition.Id}] will not respond to configuration changes");
                return;
            }

            // Get the OnChange method
            var onChangeMethod = optionsManagerType.GetMethod("OnChange", new[] { typeof(Action<>).MakeGenericType(optionsType) });

            if (onChangeMethod == null)
            {
                _logger.LogWarning(
                    $"Could not find OnChange method for type {optionsType.Name}, task [{definition.Id}] will not respond to configuration changes");
                return;
            }

            // Create callback delegate
            var callbackType = typeof(Action<>).MakeGenericType(optionsType);
            var callback = Delegate.CreateDelegate(
                callbackType,
                this,
                GetType().GetMethod(nameof(OnOptionsChanged), BindingFlags.NonPublic | BindingFlags.Instance)!
                    .MakeGenericMethod(optionsType));

            // Create a closure to capture the definition
            var parameters = new object[] { callback };
            var closureCallback = new Action<object>(_ =>
            {
                _ = EvaluateAndUpdateTaskAsync(definition);
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
                    $"Registered options change handler for task [{definition.Id}] on type {optionsType.Name}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                $"Error registering options change handler for task [{definition.Id}] on type {optionsType.Name}");
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
