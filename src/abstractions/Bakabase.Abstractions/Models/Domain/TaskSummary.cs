namespace Bakabase.Abstractions.Models.Domain;

public record TaskSummary
{
    public int Completed { get; set; }
    public int Failed { get; set; }
    public int Total { get; set; }
}
