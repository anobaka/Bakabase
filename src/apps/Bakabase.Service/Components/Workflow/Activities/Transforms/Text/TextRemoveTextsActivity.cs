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
/// Removes bare occurrences of a text set's entries from the working text — ad slogans, release
/// tags and other junk that appears without wrappers (docs/file-cleaning-workflow.html §3.4).
/// </summary>
public class TextRemoveTextsActivity : IWorkflowActivity
{
    public string Kind { get; } = TextWorkflowKinds.TransformRemoveTexts;
    public string DisplayName => "Remove matched texts";
    public WorkflowActivityCategory Category => WorkflowActivityCategory.Transform;
    public string Group => WorkflowActivityGroups.Text;
    public Type? AcceptedItemInterface => typeof(ITextWorkpiece);

    public async Task<WorkflowItemOutcome> ProcessItemAsync(WorkflowExecutionContext ctx, object item,
        CancellationToken ct)
    {
        var workpiece = TextActivityHelpers.Workpiece(Kind, item);
        var config = ctx.GetConfig<Config>();
        if (config is not {SetTypeId: > 0})
        {
            throw new WorkflowActivityConfigException($"{Kind} needs a text set configured.");
        }

        var newText = await TextActivityHelpers.Run(Kind, () => ctx.Services
            .GetRequiredService<ITextOps>()
            .RemoveTexts(workpiece.WorkingText, config.SetTypeId, config.Mode));

        return newText == workpiece.WorkingText
            ? WorkflowItemOutcome.KeepItem
            : WorkflowItemOutcome.ReplaceWith(workpiece.WithWorkingText(newText));
    }

    public record Config
    {
        /// <summary>The type whose entries are removed from the text.</summary>
        public int SetTypeId { get; init; }

        public TextMatchMode Mode { get; init; } = TextMatchMode.EqualsAny;
    }
}
