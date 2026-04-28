namespace Bakabase.Modules.DataCard.Abstractions.Models.Domain;

public record DataCardInitialDataPreview
{
    public int ToCreate { get; set; }
    public int AlreadyExists { get; set; }
}
