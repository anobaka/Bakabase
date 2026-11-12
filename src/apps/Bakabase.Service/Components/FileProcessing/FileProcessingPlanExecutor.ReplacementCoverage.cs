using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace Bakabase.Service.Components.FileProcessing;

public sealed partial class FileProcessingPlanExecutor
{
    /// <summary>One proof context for one already-read immutable checkpoint. Never cache
    /// across a new read, state mutation, executor gate or cleanup phase. File selection,
    /// current bytes/identity and external references remain independently checked.</summary>
    internal sealed class ReplacementCoverage
    {
        private readonly State state;
        private readonly CancellationToken ct;
        private readonly bool supported;
        private readonly HashSet<string> retained = new(FileProcessingFiles.PathComparer);
        private readonly HashSet<string> visiting = new(FileProcessingFiles.PathComparer);
        private readonly Dictionary<string, bool> answers = new(FileProcessingFiles.PathComparer);

        // Deterministic work counts for large-checkpoint regressions, not elapsed-time gates.
        internal int IndexedFinalEntries { get; }
        internal int IndexedActiveEntries { get; private set; }
        internal int VisitedEntries { get; private set; }

        internal ReplacementCoverage(State state, CancellationToken ct)
        {
            this.state = state; this.ct = ct;
            supported = state.LineageVersion == 1 && state.FinalManifest != null;
            if (!supported) return;
            ct.ThrowIfCancellationRequested();
            var final = state.FinalManifest!.Entries.ToDictionary(e => e.RelativePath, StringComparer.Ordinal);
            IndexedFinalEntries = final.Count;
            foreach (var artifact in state.Active)
            {
                ct.ThrowIfCancellationRequested(); IndexedActiveEntries++;
                var key = ContentKey(state, artifact.Path);
                if (state.FileContents.TryGetValue(key, out var contents) &&
                    final.TryGetValue(artifact.RelativePath.Replace('\\', '/'), out var output) && SameContent(contents, output))
                    retained.Add(key);
            }
        }

        internal bool IsComplete(string path)
        {
            ct.ThrowIfCancellationRequested();
            return supported && Visit(ContentKey(state, path));
        }

        private bool Visit(string candidate)
        {
            ct.ThrowIfCancellationRequested();
            if (answers.TryGetValue(candidate, out var answer)) return answer;
            if (!state.FileContents.ContainsKey(candidate) || !visiting.Add(candidate)) return false;
            VisitedEntries++;
            var covered = retained.Contains(candidate) || state.FileTransitions.TryGetValue(candidate, out var outputs) &&
                outputs.Count > 0 && outputs.All(Visit);
            visiting.Remove(candidate);
            return answers[candidate] = covered;
        }
    }
}
