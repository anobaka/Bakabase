using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Components;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;

namespace Bakabase.Modules.Workflow.Services;

public sealed class WorkflowValidationService(
    IWorkflowTriggerRegistry triggers,
    IWorkflowActivityRegistry activities,
    IWorkflowItemTypeRegistry itemTypes,
    IServiceProvider services) : IWorkflowValidationService
{
    public WorkflowValidationResult ValidateStructure(WorkflowValidationInputModel input)
    {
        var result = new WorkflowValidationResult();
        HashSet<int> unresolved;
        try { input = PrepareParameters(input, false, out unresolved, out var missing); AddMissingParameters(result, missing); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            Add("invalidParameters", ex.Message);
            return result;
        }
        string? currentType = null;
        if (string.IsNullOrWhiteSpace(input.TriggerKind) || !triggers.TryGet(input.TriggerKind, out var trigger))
            Add("unknownTrigger", $"Unknown trigger kind: {input.TriggerKind}", kind: input.TriggerKind);
        else
        {
            try { currentType = trigger.ResolveOutputItemType(input.TriggerFilterJson); }
            catch (Exception ex)
            {
                Add("invalidTriggerConfig", $"Trigger configuration is invalid: {ex.Message}", kind: input.TriggerKind);
            }
        }

        for (var i = 0; i < input.Activities.Count; i++)
        {
            var node = input.Activities[i];
            if (string.IsNullOrWhiteSpace(node.Kind) || !activities.TryGet(node.Kind, out var impl))
            {
                Add("unknownActivity", $"Unknown activity kind: {node.Kind}", i, node);
                currentType = null;
                continue;
            }

            var contract = impl.AcceptedItemInterface;
            if (contract is not null && impl.OutputBehavior != WorkflowItemTypeBehavior.Passthrough)
                Add("invalidContract", $"Activity {node.Kind} declares a capability contract ({contract.Name}) but is not " +
                    "Passthrough — a contract-accepting activity works on whatever type arrives and " +
                    "cannot change it. The activity's declaration is broken.", i, node);

            var accepted = impl.AcceptedInputItemTypes;
            var acceptsByContract = currentType is not null && contract is not null &&
                                    itemTypes.Get(currentType)?.ClrType is { } clr && contract.IsAssignableFrom(clr);
            if (currentType is not null && (accepted.Count > 0 || contract is not null) &&
                !accepted.Contains(currentType) && !acceptsByContract)
                Add("typeMismatch", $"Activity {node.Kind} (index {i}) accepts [{string.Join(", ", accepted)}]" +
                    (contract is null ? "" : $" or any type implementing {contract.Name}") +
                    $" but the item type at that position is \"{currentType}\". " +
                    "Insert a transform that produces a compatible type before it.", i, node);

            if (impl.IsDestructive && i > 0 &&
                !string.IsNullOrWhiteSpace(input.Activities[i - 1].Kind) &&
                activities.TryGet(input.Activities[i - 1].Kind, out var previous) &&
                previous.OutputBehavior == WorkflowItemTypeBehavior.AdaptToNext)
                Add("destructiveAfterAdapt", $"Activity {node.Kind} (index {i}) is destructive and cannot directly consume " +
                    "model-generated items — put a validating step between them.", i, node);

            try
            {
                currentType = impl.OutputBehavior switch
                {
                    WorkflowItemTypeBehavior.Passthrough => currentType,
                    WorkflowItemTypeBehavior.Fixed => impl.FixedOutputItemType
                        ?? throw new InvalidOperationException($"Activity {impl.Kind} declares Fixed output but no FixedOutputItemType"),
                    WorkflowItemTypeBehavior.AdaptToNext => impl.ResolveAdaptedOutputType(
                            node.ConfigJson, PeekNextSingleAcceptedType(i, input.Activities))
                        ?? throw new InvalidOperationException($"Activity {impl.Kind} (index {i}) needs a target item type — " +
                            "configure one explicitly, or follow it with an activity that accepts a single type"),
                    _ => throw new InvalidOperationException($"Unhandled OutputBehavior {impl.OutputBehavior} on {impl.Kind}"),
                };
            }
            catch (Exception ex)
            {
                Add("missingOutputType", ex.Message, i, node, warning: unresolved.Contains(i));
                currentType = null;
            }
        }

        return result;

        void Add(string code, string message, int? index = null, WorkflowActivityInputModel? node = null, string? kind = null, bool warning = false) =>
            result.Diagnostics.Add(new WorkflowValidationDiagnostic
            {
                Code = code, Message = message, MessageKey = $"workflow.validation.{code}",
                NodeIndex = index, NodeId = node?.NodeId, Kind = node?.Kind ?? kind,
                Severity = warning ? "warning" : "error", DependsOnPayload = warning,
            });
    }

    public async Task<WorkflowValidationResult> ValidateAsync(WorkflowValidationInputModel input,
        bool isExecution = false, object? payload = null, CancellationToken ct = default, int startNodeIndex = 0)
    {
        var result = ValidateStructure(input);
        if (result.Diagnostics.Any(d => d.Code == "invalidParameters")) return result;
        HashSet<int> unresolved;
        try { input = PrepareParameters(input, isExecution, out unresolved, out _); }
        catch (Exception ex) when (ex is ArgumentException or InvalidDataException)
        {
            result.Diagnostics.Add(new WorkflowValidationDiagnostic { Code = "invalidParameters", Message = ex.Message,
                MessageKey = "workflow.validation.invalidParameters", Severity = "error" });
            return result;
        }
        for (var i = Math.Max(0, startNodeIndex); i < input.Activities.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var node = input.Activities[i];
            if (unresolved.Contains(i) || string.IsNullOrWhiteSpace(node.Kind) || !activities.TryGet(node.Kind, out var impl)) continue;
            var config = string.IsNullOrEmpty(node.ConfigJson) ? "{}" : node.ConfigJson;
            try
            {
                using var document = JsonDocument.Parse(config);
            }
            catch (JsonException)
            {
                Add(new WorkflowValidationIssue { Code = "invalidJson", Message = "Node configuration must be valid JSON.", MessageKey = "workflow.validation.invalidJson" });
                continue;
            }

            try
            {
                var issues = await impl.ValidateConfigAsync(new WorkflowValidationContext
                {
                    Services = services, ConfigJson = config, TriggerKind = input.TriggerKind,
                    TriggerFilterJson = input.TriggerFilterJson, IsExecution = isExecution, Payload = payload,
                }, ct);
                foreach (var issue in issues) Add(issue);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                Add(new WorkflowValidationIssue { Code = "validationFailed", Message = $"Node configuration check failed: {ex.Message}", MessageKey = "workflow.validation.validationFailed" });
            }

            void Add(WorkflowValidationIssue issue) => result.Diagnostics.Add(new WorkflowValidationDiagnostic
            {
                NodeId = node.NodeId, NodeIndex = i, Kind = node.Kind,
                Code = issue.Code, Message = issue.Message, MessageKey = issue.MessageKey,
                DependsOnPayload = issue.DependsOnPayload,
                // Unknown severity must never let a failing check silently permit execution.
                Severity = issue.Severity == "warning" ? "warning" : "error",
            });
        }

        return result;
    }

    public Task<WorkflowValidationResult> ValidateAsync(WorkflowDefinition definition,
        bool isExecution = false, object? payload = null, CancellationToken ct = default, int startNodeIndex = 0) =>
        ValidateAsync(new WorkflowValidationInputModel
        {
            TriggerKind = definition.TriggerKind, TriggerFilterJson = definition.TriggerFilterJson,
            Parameters = definition.ParameterSnapshot == null ? definition.Parameters.ToList() : [],
            Activities = definition.Activities.OrderBy(a => a.Order).Select(a => new WorkflowActivityInputModel
            {
                NodeId = a.NodeId ?? a.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), Kind = a.Kind,
                ConfigJson = a.ConfigJson, Notes = a.Notes, OnItemError = a.OnItemError,
                ParameterBindings = definition.ParameterSnapshot == null ? a.ParameterBindings.ToList() : [],
            }).ToList(),
        }, isExecution, payload, ct, startNodeIndex);

    private static WorkflowValidationInputModel PrepareParameters(WorkflowValidationInputModel input, bool requireValues,
        out HashSet<int> unresolved, out IReadOnlyList<string> missing)
    {
        unresolved = [];
        missing = [];
        if (input.Parameters == null || input.Activities.Any(a => a.ParameterBindings == null))
            throw new ArgumentException("Workflow parameters and bindings must be lists.");
        if (input.Parameters.Count == 0 && input.ParameterValues?.Count is not > 0 &&
            input.Activities.All(a => a.ParameterBindings.Count == 0)) return input;
        var plan = new WorkflowRunPlan
        {
            Name = "Validation", TriggerKind = string.IsNullOrWhiteSpace(input.TriggerKind) ? "validation" : input.TriggerKind,
            Parameters = input.Parameters,
            Nodes = input.Activities.Select((a, i) => new WorkflowRunPlanNode
            {
                NodeId = "validation:" + i, Kind = a.Kind, ConfigJson = a.ConfigJson,
                ParameterBindings = a.ParameterBindings, Notes = a.Notes, OnItemError = a.OnItemError
            }).ToArray()
        };
        var invocation = new WorkflowInvocationInput { Overrides = input.ParameterValues };
        var prepared = requireValues ? WorkflowInvocationResolver.Resolve(plan, invocation) : WorkflowInvocationResolver.Preview(plan, invocation);
        var available = prepared.ParameterSnapshot!.Values.Select(v => v.Key).ToHashSet(StringComparer.Ordinal);
        missing = input.Parameters.Where(p => p.Required && !available.Contains(p.Key)).Select(p => p.Key).ToArray();
        for (var i = 0; i < input.Activities.Count; i++)
            if (input.Activities[i].ParameterBindings.Any(b => !available.Contains(b.ParameterKey))) unresolved.Add(i);
        return input with { Activities = input.Activities.Select((node, i) => node with { ConfigJson = prepared.Nodes[i].ConfigJson }).ToList() };
    }

    private static void AddMissingParameters(WorkflowValidationResult result, IReadOnlyList<string> missing)
    {
        foreach (var key in missing)
            result.Diagnostics.Add(new WorkflowValidationDiagnostic
            {
                Code = "parameterRequired", Message = $"Workflow parameter '{key}' must be supplied when starting the run.",
                MessageKey = "workflow.validation.parameterRequired", Severity = "warning", DependsOnPayload = true
            });
    }

    private string? PeekNextSingleAcceptedType(int index, IReadOnlyList<WorkflowActivityInputModel> nodes)
    {
        if (index + 1 >= nodes.Count || string.IsNullOrWhiteSpace(nodes[index + 1].Kind) ||
            !activities.TryGet(nodes[index + 1].Kind, out var next)) return null;
        return next.AcceptedInputItemTypes.Count == 1 ? next.AcceptedInputItemTypes[0] : null;
    }
}
