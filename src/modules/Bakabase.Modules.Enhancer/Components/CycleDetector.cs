namespace Bakabase.Modules.Enhancer.Components;

/// <summary>
/// Generic depth-first cycle detector over a directed graph defined by a node
/// set and a dependency-edge function. Extracted from
/// <c>EnhancerService.BuildContextCreationTasks</c> so the algorithm can be
/// unit-tested in isolation.
///
/// Semantics match the original site:
/// - Visit every node; skip nodes already fully visited.
/// - On encountering a node currently on the DFS stack, build a label string
///   in the order <c>start -&gt; ... -&gt; start</c> and stop.
/// - Self-edges (a node listing itself as a dependency) are <b>not</b> filtered
///   here — callers that want to ignore them must strip them before calling.
/// </summary>
public static class CycleDetector
{
    /// <summary>
    /// Returns the first cycle found, formatted as <c>label1-&gt;label2-&gt;...-&gt;label1</c>,
    /// or <c>null</c> when the graph is acyclic.
    /// </summary>
    public static string? DetectFirstCycle<T>(
        IEnumerable<T> nodes,
        Func<T, IEnumerable<T>> getDependencies,
        Func<T, string> labelOf)
        where T : class
    {
        var visited = new HashSet<T>();
        var stack = new Stack<T>();
        string? detected = null;

        foreach (var node in nodes)
        {
            if (visited.Contains(node)) continue;
            Dfs(node);
            if (detected != null) break;
        }

        return detected;

        void Dfs(T node)
        {
            if (detected != null) return;

            if (stack.Contains(node))
            {
                // Walk the stack (top-first) until we hit the matching node;
                // those are the nodes participating in the cycle. Reverse so
                // the label reads from the start of the cycle, then close it.
                var cycleNodes = new List<T>();
                foreach (var s in stack)
                {
                    cycleNodes.Add(s);
                    if (s == node) break;
                }

                cycleNodes.Reverse();
                cycleNodes.Add(node);
                detected = string.Join("->", cycleNodes.Select(labelOf));
                return;
            }

            if (!visited.Add(node)) return;

            stack.Push(node);
            foreach (var dep in getDependencies(node))
            {
                Dfs(dep);
            }

            stack.Pop();
        }
    }
}
