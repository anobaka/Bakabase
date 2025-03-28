using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.View;

namespace Bakabase.Abstractions.Components.Tasks;

public interface IBTaskEventHandler
{
    Task OnTaskChange(BTaskViewModel task);
    Task OnAllTasksChange(IEnumerable<BTaskViewModel> tasks);
    Task OnTaskManagerStatusChange(bool isRunning);
}