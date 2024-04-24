namespace Bakabase.Abstractions.Components.StandardValue;

public interface IStandardValueBuilder
{
    object? Value { get; }
}

public interface IStandardValueBuilder<out TValue>: IStandardValueBuilder
{
    new TValue? Value { get; }
    object? IStandardValueBuilder.Value => Value;
}