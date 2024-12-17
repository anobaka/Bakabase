using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Tasks.Abstractions;

public interface IBackgroundTaskExecutor
{
    Task Execute(object? model);
}