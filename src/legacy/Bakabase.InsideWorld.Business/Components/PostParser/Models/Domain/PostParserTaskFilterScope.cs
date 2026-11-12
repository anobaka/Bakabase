namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

/// <summary>The same keyword, status and attention filters used by paging and confirmed bulk actions.</summary>
public sealed record PostParserTaskFilterScope
{
    public string? Keyword { get; set; }
    public string? Status { get; set; }
    public string? ReasonCode { get; set; }
}
