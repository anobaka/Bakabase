namespace Bakabase.Modules.AI.Models.Domain;

public enum LlmCallStatus
{
    Success = 1,
    Error = 2,
    Timeout = 3,
    Cancelled = 4
}
