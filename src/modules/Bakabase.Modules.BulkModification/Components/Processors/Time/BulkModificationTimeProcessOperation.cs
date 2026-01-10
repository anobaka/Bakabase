namespace Bakabase.Modules.BulkModification.Components.Processors.Time;

public enum BulkModificationTimeProcessOperation
{
    Delete = 1,
    SetWithFixedValue = 2,
    AddHours = 3,
    SubtractHours = 4,
    AddMinutes = 5,
    SubtractMinutes = 6,
    AddSeconds = 7,
    SubtractSeconds = 8
}
