namespace Bakabase.Modules.BulkModification.Components.Processors.DateTime;

public enum BulkModificationDateTimeProcessOperation
{
    Delete = 1,
    SetWithFixedValue = 2,
    AddDays = 3,
    SubtractDays = 4,
    AddMonths = 5,
    SubtractMonths = 6,
    AddYears = 7,
    SubtractYears = 8,
    SetToNow = 9
}
