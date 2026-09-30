using System;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.ResourceMove;
using Bootstrap.Components.Tasks;

namespace Bakabase.InsideWorld.Business.Components.ResourceMove;

/// <summary>Adapter around the existing journaled move protocol.</summary>
public sealed class LocalFilesResourceMoveExecutor : IResourceMoveExecutor
{
    public string Id => "local-files";
    public int Version => 1;
    public Task ExecuteOrResumeAsync(ResourceMoveExecutionState state, Func<Task> checkpoint,
        Func<int, Task> progress, PauseToken pause, CancellationToken cancellation,
        Func<string, string, bool> authorized) =>
        ResourceMoveSafeFileSystem.Move(state, checkpoint, progress, pause, cancellation, authorized);
    public void Cleanup(ResourceMoveExecutionState state) => ResourceMoveSafeFileSystem.Cleanup(state);
}
