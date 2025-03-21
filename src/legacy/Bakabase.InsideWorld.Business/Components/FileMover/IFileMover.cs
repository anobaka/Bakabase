using Bakabase.Abstractions.Components.Tasks;
using Bakabase.InsideWorld.Business.Components.FileMover.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bootstrap.Components.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileMover
{
    public interface IFileMover
    {
        Task MovingFiles(Func<int, Task>? onProgressChange, PauseToken pt, CancellationToken ct);
        ConcurrentDictionary<string, FileMovingProgress> Progresses { get; }
    }
}
