using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Acquisition.Components.Workflow;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Bounds active acquisition runs without interpreting their activities. Each invoked
/// capability owns its execution policy and queue independently of this orchestration limit.</summary>
public sealed class AcquisitionWorkflowSchedulingPolicy(IBOptions<AcquisitionOptions> options)
    : IWorkflowRunSchedulingPolicy, IWorkflowRunSourceSchedulingPolicy
{
    public const string Group = "acquisition.workflow";

    public bool AppliesTo(string triggerKind) => triggerKind == AcquisitionWorkflowKinds.TriggerRequested;
    public bool AppliesTo(string triggerKind, IReadOnlyList<WorkflowSourceRef> sources) =>
        sources.Any(s => s.Module == "acquisition" && s.EntityKind == "task" &&
            int.TryParse(s.EntityId, NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0);
    public bool ResumeOnStartup => true;

    // Recipes share a definition, but each run owns a different acquisition task.
    BTaskHandlerBuilder IWorkflowRunSchedulingPolicy.Configure(BTaskHandlerBuilder builder) =>
        Configure(builder.ConflictsWith(builder.Id));

    public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) =>
        builder.WithConcurrencyLimit(Group, () => Math.Clamp(options.Value.Concurrency,
            AcquisitionOptions.MinimumConcurrency, AcquisitionOptions.MaximumConcurrency));
}
