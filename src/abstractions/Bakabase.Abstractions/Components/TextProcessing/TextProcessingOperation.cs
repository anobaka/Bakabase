namespace Bakabase.Abstractions.Components.TextProcessing
{
    public enum TextProcessingOperation
    {
        Delete = 1,
        SetWithFixedValue,
        AddToStart,
        AddToEnd,
        AddToAnyPosition,
        RemoveFromStart,
        RemoveFromEnd,
        RemoveFromAnyPosition,
        ReplaceFromStart,
        ReplaceFromEnd,
        ReplaceFromAnyPosition,
        ReplaceWithRegex,
    }
}