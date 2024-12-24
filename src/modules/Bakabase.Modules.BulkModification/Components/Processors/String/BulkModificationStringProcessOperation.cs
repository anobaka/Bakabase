using Bakabase.Abstractions.Components.TextProcessing;

namespace Bakabase.Modules.BulkModification.Components.Processors.String;

public enum BulkModificationStringProcessOperation
{
    Delete = TextProcessingOperation.Delete,
    SetWithFixedValue = TextProcessingOperation.SetWithFixedValue,
    AddToStart = TextProcessingOperation.AddToStart,
    AddToEnd = TextProcessingOperation.AddToEnd,
    AddToAnyPosition = TextProcessingOperation.AddToAnyPosition,
    RemoveFromStart = TextProcessingOperation.RemoveFromStart,
    RemoveFromEnd = TextProcessingOperation.RemoveFromEnd,
    RemoveFromAnyPosition = TextProcessingOperation.RemoveFromAnyPosition,
    ReplaceFromStart = TextProcessingOperation.ReplaceFromStart,
    ReplaceFromEnd = TextProcessingOperation.ReplaceFromEnd,
    ReplaceFromAnyPosition = TextProcessingOperation.ReplaceFromAnyPosition,
    ReplaceWithRegex = TextProcessingOperation.ReplaceWithRegex,
}