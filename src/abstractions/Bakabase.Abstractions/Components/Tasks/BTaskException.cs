namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskException(string? briefMessage, string? message) : Exception(message)
{
    public string? BriefMessage { get; set; } = briefMessage;
}