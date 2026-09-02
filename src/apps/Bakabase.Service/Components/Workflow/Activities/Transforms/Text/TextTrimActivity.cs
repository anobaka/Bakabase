using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Text;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain.Constants;
using Bakabase.Service.Components.Workflow.Text;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms.Text;

/// <summary>
/// Cleans up what removal leaves behind — runs of whitespace, empty wrapper pairs, stray edge
/// separators (docs/file-cleaning-workflow.html §3.4). Typically the last text node in a chain.
/// </summary>
public class TextTrimActivity : IWorkflowActivity
{
    public string Kind { get; } = TextWorkflowKinds.TransformTrim;
    public string DisplayName => "Trim leftovers";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Text;
    public Type? AcceptedItemInterface => typeof(ITextWorkpiece);

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        var workpiece = TextActivityHelpers.Workpiece(Kind, item);
        // An absent config means "all switches on" — trim's defaults are the point of the node.
        var config = ctx.GetConfig<Config>() ?? new Config();

        var newText = await TextActivityHelpers.Run(Kind, () => ctx.Services
            .GetRequiredService<ITextOps>()
            .Trim(workpiece.WorkingText,
                new TextTrimOptions(config.CollapseSpaces, config.TrimEnds, config.RemoveEmptyWrappers)));

        return newText == workpiece.WorkingText
            ? WorkflowItemOutcome.KeepItem
            : WorkflowItemOutcome.ReplaceWith(workpiece.WithWorkingText(newText));
    }

    public record Config
    {
        public bool CollapseSpaces { get; init; } = true;
        public bool TrimEnds { get; init; } = true;
        public bool RemoveEmptyWrappers { get; init; } = true;
    }
}
