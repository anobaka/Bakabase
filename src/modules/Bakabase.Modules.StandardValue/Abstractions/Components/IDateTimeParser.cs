namespace Bakabase.Modules.StandardValue.Abstractions.Components;

public interface IDateTimeParser
{
    public Task<DateTime?> TryToParseDateTime(string? str);
}