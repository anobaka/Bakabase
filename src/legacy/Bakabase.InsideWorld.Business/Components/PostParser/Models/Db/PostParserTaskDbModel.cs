using System;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Db;

public record PostParserTaskDbModel
{
    public int Id { get; set; }
    public PostParserSource Source { get; set; }
    public string Link { get; set; } = null!;
    public string? Title { get; set; }
    public DateTime? ParsedAt { get; set; }
    public string? Items { get; set; }
    public string? Error { get; set; }
}