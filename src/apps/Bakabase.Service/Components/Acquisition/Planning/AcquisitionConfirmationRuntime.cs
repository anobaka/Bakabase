using System;
using System.Collections.Generic;
using System.Threading;
using Bakabase.Modules.Acquisition.Abstractions.Models.Db;
using Bakabase.Modules.Workflow.Abstractions.Components;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>In-process observations from accepted operations before the workflow can report progress.</summary>
public sealed class AcquisitionConfirmationRuntime
{
    private readonly object sync = new();
    private readonly Dictionary<(int Task, int Revision, int? Run), LinkedList<WorkflowActivityRuntimeSnapshot>> active = new();

    internal IDisposable Observe(AcquisitionTaskDbModel task, WorkflowActivityRuntimeSnapshot snapshot, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var key = (task.Id, task.PlanRevision, task.WorkflowRunId);
        var observation = new LinkedListNode<WorkflowActivityRuntimeSnapshot>(snapshot);
        lock (sync)
        {
            if (!active.TryGetValue(key, out var entries)) active.Add(key, entries = new());
            entries.AddLast(observation);
        }
        void Remove()
        {
            lock (sync)
            {
                if (observation.List is not { } entries) return;
                entries.Remove(observation);
                if (entries.Count == 0) active.Remove(key);
            }
        }
        return new Scope(Remove, ct.Register(Remove));
    }

    internal WorkflowActivityRuntimeSnapshot? Read(int taskId, int revision, int? runId)
    {
        lock (sync)
            return active.TryGetValue((taskId, revision, runId), out var entries) ? entries.Last?.Value : null;
    }

    private sealed class Scope(Action remove, CancellationTokenRegistration cancellation) : IDisposable
    {
        public void Dispose()
        {
            cancellation.Dispose();
            remove();
        }
    }
}
