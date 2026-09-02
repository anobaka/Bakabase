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
/// Removes wrapped segments — "[SubGroup]", "(1080p)" — whose content matches a text set
/// (docs/file-cleaning-workflow.html §3.4). The wrappers reference must be a DelimiterPair-shaped
/// type; the set is any type whose entries are matched by the configured mode. First member of
/// the text family: accepts by the <see cref="ITextWorkpiece"/> contract, not by item type.
/// </summary>
public class TextRemoveWrappedActivity : IWorkflowActivity
{
    public string Kind { get; } = TextWorkflowKinds.TransformRemoveWrapped;
    public string DisplayName => "Remove wrapped segments";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Text;
    public Type? AcceptedItemInterface => typeof(ITextWorkpiece);

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        var workpiece = TextActivityHelpers.Workpiece(Kind, item);
        var config = ctx.GetConfig<Config>();
        if (config is not {WrappersTypeId: > 0, SetTypeId: > 0})
        {
            throw new WorkflowActivityConfigException(
                $"{Kind} needs both a wrappers type and a text set configured.");
        }

        var newText = await TextActivityHelpers.Run(Kind, () => ctx.Services
            .GetRequiredService<ITextOps>()
            .RemoveWrapped(workpiece.WorkingText, config.WrappersTypeId, config.SetTypeId, config.Mode));

        return newText == workpiece.WorkingText
            ? WorkflowItemOutcome.KeepItem
            : WorkflowItemOutcome.ReplaceWith(workpiece.WithWorkingText(newText));
    }

    public record Config
    {
        /// <summary>A DelimiterPair-shaped text type (ITextOps validates the shape).</summary>
        public int WrappersTypeId { get; init; }

        /// <summary>The type whose entries the wrapped content is matched against.</summary>
        public int SetTypeId { get; init; }

        public TextMatchMode Mode { get; init; } = TextMatchMode.EqualsAny;
    }
}
