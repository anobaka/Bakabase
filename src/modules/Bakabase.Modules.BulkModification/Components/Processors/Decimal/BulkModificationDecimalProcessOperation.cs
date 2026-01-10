namespace Bakabase.Modules.BulkModification.Components.Processors.Decimal;

public enum BulkModificationDecimalProcessOperation
{
    Delete = 1,
    SetWithFixedValue = 2,
    Add = 3,
    Subtract = 4,
    Multiply = 5,
    Divide = 6,
    Round = 7,
    Ceil = 8,
    Floor = 9
}
