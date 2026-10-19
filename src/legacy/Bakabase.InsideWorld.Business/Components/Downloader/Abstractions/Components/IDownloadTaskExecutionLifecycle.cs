using System.Collections.Generic;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Downloader.Abstractions.Components;

/// <summary>Application adapters coordinate independently managed executions without coupling source downloaders to them.</summary>
public interface IDownloadTaskExecutionLifecycle
{
    Task OnStartedAsync(IReadOnlyCollection<int> taskIds);
    Task OnStoppedAsync(IReadOnlyCollection<int> taskIds);
}
