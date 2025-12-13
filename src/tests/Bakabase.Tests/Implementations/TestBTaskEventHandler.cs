using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.View;

namespace Bakabase.Tests.Implementations;

public class TestBTaskEventHandler : IBTaskEventHandler
{
    public Task OnTaskChange(BTaskViewModel task) => Task.CompletedTask;
    public Task OnAllTasksChange(IEnumerable<BTaskViewModel> tasks) => Task.CompletedTask;
    public Task OnTaskManagerStatusChange(bool isRunning) => Task.CompletedTask;
}
