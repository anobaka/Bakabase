namespace Bakabase.Modules.DataCard.Abstractions.Models.Domain;

public record DataCardDisplayTemplate
{
    public int Cols { get; set; } = 6;
    public int Rows { get; set; } = 4;
    public List<DataCardDisplayTemplateItem>? Layout { get; set; }
}

public record DataCardDisplayTemplateItem
{
    public int PropertyId { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int W { get; set; } = 1;
    public int H { get; set; } = 1;
    public bool HideLabel { get; set; }
    public bool HideEmpty { get; set; }
}
