using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Abstractions.Models;

public record BulkModificationProcessStep
{
    public int Operation { get; set; }
    public IBulkModificationProcessOptions? Options { get; set; }
}

public record BulkModificationProcessStep<TOperation, TOptions> : BulkModificationProcessStep
    where TOptions : class, IBulkModificationProcessOptions where TOperation : Enum
{
    public new TOptions? Options
    {
        get => base.Options as TOptions;
        set => base.Options = value;
    }

    public new TOperation Operation
    {
        get => (TOperation) (object) base.Operation;
        set => base.Operation = (int) (object) value;
    }
}