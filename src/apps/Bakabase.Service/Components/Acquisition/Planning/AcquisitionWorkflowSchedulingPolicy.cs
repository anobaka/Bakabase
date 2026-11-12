using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Modules.Acquisition.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.CloudDownloads;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Dynamic acquisition plans share the configured execution pool across starts,
/// retries and startup recovery. A waiting workflow releases its BTask slot naturally.</summary>
public sealed class AcquisitionWorkflowSchedulingPolicy(IBOptions<AcquisitionOptions> options)
    : IWorkflowRunSourceSchedulingPolicy
{
    public const string Group = "acquisition.workflow";

    public bool AppliesTo(string triggerKind, IReadOnlyList<WorkflowSourceRef> sources) =>
        triggerKind == CloudWorkflow.Trigger && sources.Any(s => s.Module == "acquisition" &&
            s.EntityKind == "task" && int.TryParse(s.EntityId, NumberStyles.None,
                CultureInfo.InvariantCulture, out var id) && id > 0);

    public BTaskHandlerBuilder Configure(BTaskHandlerBuilder builder) =>
        builder.WithConcurrencyLimit(Group, () => Math.Max(1, options.Value.Concurrency));
}
