using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.InsideWorld.Business;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Business.Components.PostParser.Workflow;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Models.View;
using Bakabase.Modules.Workflow.Abstractions.Services;
using Bakabase.Modules.Workflow.Artifacts;
using Bakabase.Service.Components.Acquisition;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Service.Components.FileProcessing;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.DownloadIntents;

public sealed record PostLocalProcessingRequest
{
    public int TaskId { get; init; }
    public int Revision { get; init; }
    public int ResourceIndex { get; init; }
    public int WorkflowDefinitionId { get; init; }
    public List<string> Files { get; init; } = [];
    public bool AlreadyProcessed { get; init; }
}

/// <summary>Post ownership remains at this boundary; the filesystem trigger has no post-specific conventions.</summary>
public sealed class PostLocalProcessingService(BakabaseDbContext db, PostParserTaskExecutionGate gate,
    PostParserWorkflowService<BakabaseDbContext> posts, IWorkflowDefinitionService workflows, IUserStoragePolicy storage, SourceProcessingPlanService? processingPlans = null)
{
    public async Task<WorkflowRunViewModel> StartAsync(PostLocalProcessingRequest input, string directory, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory) || input.Files == null || input.Files.Count > 100000)
            throw new ArgumentException("Choose an absolute server directory and a valid file selection.");
        storage.EnsurePathAllowed(directory);
        foreach (var file in input.Files)
        {
            var path = FileProcessingFiles.Within(file, directory);
            storage.EnsurePathAllowed(path);
        }
        await gate.Semaphore.WaitAsync(ct);
        try
        {
            await posts.RefreshTaskUnderGateAsync(input.TaskId, ct);
            var task = await db.PostParserTasks.AsNoTracking().SingleOrDefaultAsync(t => t.Id == input.TaskId && !t.IsDeleted, ct)
                ?? throw new ArgumentException("The saved parsing result no longer exists.");
            if (task.Revision != input.Revision) throw new InvalidOperationException("The post was parsed again. Refresh its results before processing local files.");
            if (!PostParserSavedResults.HasCompletedDownloadResult(task) || task.Error != null && task.Error != PostParserSavedResults.MissingWorkflowRunError)
                throw new ArgumentException("Complete parsing before processing this saved result.");
            var resources = PostParserAcquisitionService.ReadResources(task.Results!);
            if (input.ResourceIndex < 0 || input.ResourceIndex >= resources.Count)
                throw new ArgumentException("Choose a source from the current saved result.");
            var definition = await workflows.GetAsync(input.WorkflowDefinitionId)
                ?? throw new ArgumentException("Choose an existing file-processing workflow.");
            if (definition.TriggerKind != FileProcessingPlanTrigger.TriggerKind)
                throw new ArgumentException("This workflow does not accept local file-processing input.");
            var resource = resources[input.ResourceIndex];
            FileProcessingPlan plan = resource.Extraction ?? new FileProcessingPlan();
            if (processingPlans != null)
            {
                await PostProcessingPlanSourceReader.EnsureStableAsync(db, task, ct);
                plan = (await processingPlans.ResolveAsync([PostProcessingPlanSourceReader.Snapshot(task, input.ResourceIndex)], ct))[0]
                    .EffectivePlan ?? new FileProcessingPlan();
            }
            plan = plan with {Steps = plan.Steps.Select(step => step.Op == "extractArchive" && step.Password == null
                ? step with {Password = resource.Password} : step).ToList()};
            var payload = new FileProcessingPlanPayload
            {
                Directory = Path.GetFullPath(directory), Files = input.Files,
                ExtractionPlanJson = JsonSerializer.Serialize(plan, WorkflowJson.Options),
                AlreadyProcessed = input.AlreadyProcessed,
                BindingId = $"post:{task.Id}:{task.Revision}:{input.ResourceIndex}",
                Title = $"{task.Title ?? "Post"} · {input.ResourceIndex + 1}"
            };
            var source = new WorkflowSourceRef("postParser", "task", task.Id.ToString(CultureInfo.InvariantCulture),
                task.Revision.ToString(CultureInfo.InvariantCulture));
            var run = await workflows.RunManuallyWithSourcesAsync(definition.Id, JsonSerializer.Serialize(payload, WorkflowJson.Options),
                [source, source with {ResultKey = input.ResourceIndex.ToString(CultureInfo.InvariantCulture)}], ct);
            return WorkflowRunViewModel.From(run);
        }
        finally {gate.Semaphore.Release();}
    }
}
