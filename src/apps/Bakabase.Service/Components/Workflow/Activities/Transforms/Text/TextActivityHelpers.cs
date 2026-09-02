using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Workflow.Activities.Transforms.Text;

internal static class TextActivityHelpers
{
    public static ITextWorkpiece Workpiece(string kind, object item)
    {
        if (item is not ITextWorkpiece workpiece)
        {
            // The runner's contract check makes this unreachable; reaching it means an engine bug.
            throw new InvalidOperationException(
                $"{kind} received a {item.GetType().Name}, which does not implement {nameof(ITextWorkpiece)}.");
        }

        return workpiece;
    }

    /// <summary>
    /// Runs a vocabulary-backed operation, converting its own validation failures — a deleted
    /// text type, a wrappers reference that is not DelimiterPair-shaped — into
    /// <see cref="WorkflowActivityConfigException"/>: they mean the step's configuration no
    /// longer describes reality, which must fail the run even under the Skip policy rather
    /// than "skip" every single item for the same reason.
    /// </summary>
    public static async Task<string> Run(string kind, Func<Task<string>> op)
    {
        try
        {
            return await op();
        }
        // KeyNotFound: the referenced text type was deleted; InvalidOperation: it has the
        // wrong shape for the parameter. Both are configuration staleness, not item trouble.
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException)
        {
            throw new WorkflowActivityConfigException($"{kind}: {ex.Message}", ex);
        }
    }
}
