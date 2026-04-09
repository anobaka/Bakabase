using System;
using System.Collections.Generic;
using Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Models.Domain;

public record PostParserTask
{
    public int Id { get; set; }
    public PostParserSource Source { get; set; }
    public string Link { get; set; } = null!;
    public string? Title { get; set; }
    public string? Content { get; set; }
    public List<PostParseTarget> Targets { get; set; } = [];
    public Dictionary<PostParseTarget, PostParseTargetResult>? Results { get; set; }
    public bool IsDeleted { get; set; }
}

public record PostParseTargetResult
{
    public object? Data { get; set; }
    public DateTime? ParsedAt { get; set; }
    public string? Error { get; set; }
}
