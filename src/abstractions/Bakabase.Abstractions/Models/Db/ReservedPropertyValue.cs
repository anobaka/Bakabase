﻿namespace Bakabase.Abstractions.Models.Db;

public record ReservedPropertyValue
{
    public int Id { get; set; }
    public int ResourceId { get; set; }
    public int Scope { get; set; }
    public decimal? Rating { get; set; }
    public string? Introduction { get; set; }

    /// <summary>
    /// Standard value (attachment) serialized
    /// </summary>
    public string? CoverPaths { get; set; }
}