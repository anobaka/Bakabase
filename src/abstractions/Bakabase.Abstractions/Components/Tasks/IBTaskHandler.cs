using Bakabase.Abstractions.Models.Domain.Constants;
using Quartz;

namespace Bakabase.Abstractions.Components.Tasks;

public interface IBTaskHandler : IJob
{
    string Key { get; }
    Task Start();
    Task Stop();
    Task Pause();
    string? Error { get; }
    BTaskStatus Status { get; }
    int? Percentage { get; }
    string? CurrentProcess { get; set; }
}