using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Bakabase.Modules.Workflow.Abstractions.Models;
using Bakabase.Modules.Workflow.Artifacts;

namespace Bakabase.Service.Components.FileProcessing;

/// <summary>Reusable public storage contract. Callers supply values; file nodes own their meaning.</summary>
public static class ArtifactWorkflowParameters
{
    internal const string ProcessingWorkspaceParameter = "storage.processingWorkspaceDirectory";
    private static readonly string[] Members = ["downloadDirectoryTemplate", "processingDirectoryTemplate",
        "processingWorkspaceDirectory", "cleanupIntermediates", "sourceCleanup", "materialization"];

    public static IReadOnlyList<WorkflowParameterDefinition> Declarations() =>
        Values(new()).Select(entry => new WorkflowParameterDefinition
        {
            Key = entry.Key, Type = entry.Value.ValueKind is JsonValueKind.True or JsonValueKind.False ? "boolean" : "string",
            Format = entry.Key == ProcessingWorkspaceParameter ? "directory" : null,
            DefaultValue = entry.Value, DisplayInRun = true,
            AllowedValues = entry.Key == "storage.sourceCleanup" ? new[] {"keep", "quarantine", "delete"}
                .Select(value => JsonSerializer.SerializeToElement(value)).ToArray() : entry.Key == "storage.materialization"
                ? new[] {"auto", "copy"}.Select(value => JsonSerializer.SerializeToElement(value)).ToArray() : null
        }).ToArray();

    public static IReadOnlyDictionary<string, JsonElement> Values(ArtifactStoragePolicy policy)
    {
        ArtifactStoragePolicies.Validate(policy);
        return new Dictionary<string, JsonElement>(StringComparer.Ordinal)
        {
            ["storage.downloadDirectoryTemplate"] = JsonSerializer.SerializeToElement(policy.DownloadDirectoryTemplate),
            ["storage.processingDirectoryTemplate"] = JsonSerializer.SerializeToElement(policy.ProcessingDirectoryTemplate),
            [ProcessingWorkspaceParameter] = JsonSerializer.SerializeToElement(policy.ProcessingWorkspaceDirectory),
            ["storage.cleanupIntermediates"] = JsonSerializer.SerializeToElement(policy.CleanupIntermediates),
            ["storage.sourceCleanup"] = JsonSerializer.SerializeToElement(policy.SourceCleanup),
            ["storage.materialization"] = JsonSerializer.SerializeToElement(policy.Materialization)
        };
    }

    public static IReadOnlyList<WorkflowParameterBinding> Bindings(ArtifactStoragePolicyOverride? fixedValues = null) =>
        Members.Where(member => !IsFixed(member, fixedValues)).Select(member => new WorkflowParameterBinding
            {ParameterKey = "storage." + member, ConfigPath = "/storagePolicy/" + member}).ToArray();

    // These built-in configurations consume ArtifactStoragePolicy as allocation policy.
    // A user/unknown activity binding the same parameter to an input is not this contract.
    internal static bool IsAllocationRootBinding(string kind, WorkflowParameterBinding binding) =>
        binding.ParameterKey == ProcessingWorkspaceParameter &&
        binding.ConfigPath == "/storagePolicy/processingWorkspaceDirectory" &&
        kind is "download.selectSources" or "acquisition.plan.continueFiles" or "acquisition.plan.process" or
            "fs.processArtifacts" or "download.processFiles";

    private static bool IsFixed(string member, ArtifactStoragePolicyOverride? value) => member switch
    {
        "downloadDirectoryTemplate" => value?.DownloadDirectoryTemplate != null,
        "processingDirectoryTemplate" => value?.ProcessingDirectoryTemplate != null,
        "processingWorkspaceDirectory" => value?.ProcessingWorkspaceDirectory != null,
        "cleanupIntermediates" => value?.CleanupIntermediates != null,
        "sourceCleanup" => value?.SourceCleanup != null,
        "materialization" => value?.Materialization != null,
        _ => false
    };
}
