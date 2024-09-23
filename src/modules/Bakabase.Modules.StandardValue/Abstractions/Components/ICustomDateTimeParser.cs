namespace Bakabase.Modules.StandardValue.Abstractions.Components;

public interface ICustomDateTimeParser
{
    public Task<DateTime?> TryToParseDateTime(string? str);
}