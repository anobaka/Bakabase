namespace Bakabase.Abstractions.Components.Text;

public interface ITextParser
{
    Task<DateTime?> ParseDateTime(string text);
}