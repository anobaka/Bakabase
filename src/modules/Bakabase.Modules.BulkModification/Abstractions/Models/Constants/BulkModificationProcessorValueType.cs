namespace Bakabase.Modules.BulkModification.Abstractions.Models.Constants;

public enum BulkModificationProcessorValueType
{
    /// <summary>
    /// Biz value
    /// </summary>
    Static = 1,

    /// <summary>
    /// Db value
    /// </summary>
    Dynamic = 2,

    /// <summary>
    /// Variable
    /// </summary>
    Variable = 3
}