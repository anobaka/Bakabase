using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Workflow.Abstractions.Models.Input;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.FileProcessing;

public sealed record FileProcessingPlanPayload
{
    public string Directory { get; init; } = "";
    public IReadOnlyList<string> Files { get; init; } = [];
    public string ExtractionPlanJson { get; init; } = "";
    public bool AlreadyProcessed { get; init; }
    public int? ResourceId { get; init; }
    public string? BindingId { get; init; }
    public string? Title { get; init; }
}

/// <summary>One explicit resource binding per run, including a whole directory using one plan.</summary>
public sealed class FileProcessingPlanTrigger : IWorkflowTrigger
{
    public const string TriggerKind = "fs.processingPlan";
    public string Kind => TriggerKind;
    public string DisplayName => "Process a local folder using file-processing instructions";
    public string Description => "Run manually with a server-accessible directory and a saved file-processing plan. Optional files restrict the input; otherwise all files belong to this one resource. Each resource binding starts a separate run. No file watcher or timer starts this trigger.";
    public string DescriptionKey => "workflow.trigger.fsProcessingPlan.description";
    public WorkflowActivationMode ActivationMode => WorkflowActivationMode.Manual;
    public string SourceModule => "fs";
    public bool RequiresManualPayload => true;
    public Type PayloadType => typeof(FileProcessingPlanPayload);
    public bool Matches(object payload, string? triggerFilterJson) => false;
    public string ResolveOutputItemType(string? triggerFilterJson) => AcquisitionWorkflowKinds.ItemAcquisition;

    public object BuildManualPayload(string? triggerFilterJson, string? argsJson)
    {
        FileProcessingPlanPayload input;
        PostExtractionPlan plan;
        try
        {
            input = JsonSerializer.Deserialize<FileProcessingPlanPayload>(argsJson ?? "null", WorkflowJson.Options)
                    ?? throw new InvalidOperationException("Select a directory and a file-processing plan.");
            if (input.AlreadyProcessed)
                input = input with {ExtractionPlanJson = JsonSerializer.Serialize(
                    new PostExtractionPlan {Requirement = "notRequired"}, WorkflowJson.Options)};
            plan = JsonSerializer.Deserialize<PostExtractionPlan>(input.ExtractionPlanJson, WorkflowJson.Options)
                       ?? throw new InvalidOperationException("The file-processing plan is empty.");
            FileProcessingPlanExecutor.Validate(plan);
        }
        catch (JsonException ex) { throw new InvalidOperationException("The processing plan is not valid JSON.", ex); }
        if (!System.IO.Directory.Exists(input.Directory)) throw new InvalidOperationException("The selected directory does not exist on the server.");
        var root = Path.GetFullPath(input.Directory);
        var files = input.Files.Count == 0 ? FileProcessingFiles.Enumerate(root) :
            input.Files.Select(f => FileProcessingFiles.Within(f, root)).ToList();
        if (files.Count == 0 || files.Any(f => !File.Exists(f)))
            throw new InvalidOperationException("Select at least one existing file.");
        if (!input.AlreadyProcessed && plan.Steps.Any(s => s.Op == "extractArchive"))
        {
            files = FileProcessingFiles.ExpandVolumes(files).Select(f => FileProcessingFiles.Within(f, root)).ToList();
            FileProcessingFiles.ValidateVolumes(files);
        }
        return input with {Directory = root, Files = files};
    }

    public IReadOnlyList<object> ExtractItems(object payload)
    {
        if (payload is not FileProcessingPlanPayload input) return [];
        return [new AcquisitionWorkItem
        {
            ResourceId = input.ResourceId ?? 0, LeadKind = AcquisitionLeadKind.Manual,
            LeadValue = input.BindingId ?? input.Directory, Title = input.Title,
            WorkingDirectory = input.Directory, WorkingName = input.Title ?? Path.GetFileName(input.Directory),
            Files = input.Files, ExtractionPlanJson = input.ExtractionPlanJson,
            AlreadyProcessed = input.AlreadyProcessed
        }];
    }
}

public sealed class FileProcessingWorkflowSeeder(BakabaseDbContext db, IWorkflowDefinitionService definitions)
{
    public const string BuiltinName = "Process local files using extraction instructions";
    public async Task SeedAsync(CancellationToken ct = default)
    {
        if (await db.Set<WorkflowDefinitionDbModel>().AnyAsync(d => d.IsBuiltin &&
                d.TriggerKind == FileProcessingPlanTrigger.TriggerKind && d.Name == BuiltinName, ct)) return;
        var created = await definitions.CreateAsync(new WorkflowDefinitionCreationInputModel
        {
            Name = BuiltinName,
            Description = "Select the local files belonging to one resource and execute its saved file-processing instructions. Originals are preserved. Bind each resource separately when processing a mixed download folder.",
            DescriptionKey = "workflow.recipe.fileProcessingPlan.description",
            TriggerKind = FileProcessingPlanTrigger.TriggerKind,
            Enabled = true,
            Activities = [new WorkflowActivityInputModel {Kind = AcquisitionStepKinds.Unpack,
                ConfigJson = "{}", OnItemError = WorkflowActivityErrorBehavior.Fail}]
        }, ct);
        await db.Set<WorkflowDefinitionDbModel>().Where(d => d.Id == created.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.IsBuiltin, true), ct);
    }
}
