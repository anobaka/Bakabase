namespace Bakabase.Modules.BulkModification.Abstractions.Components;

public record BulkModificationProcessDescriptor(Type OperationType, Type ProcessOptionsType, Type ProcessorOptionsType);