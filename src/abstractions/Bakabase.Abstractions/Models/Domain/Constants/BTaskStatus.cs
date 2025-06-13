namespace Bakabase.Abstractions.Models.Domain.Constants;

public enum BTaskStatus
{
    NotStarted = 1,
    Running = 2,
    Paused = 3,
    Error = 4,
    Completed = 5,
    Cancelled = 6
}