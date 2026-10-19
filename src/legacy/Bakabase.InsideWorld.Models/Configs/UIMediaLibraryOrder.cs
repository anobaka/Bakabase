using System;
using System.Collections.Generic;
using System.Linq;

namespace Bakabase.InsideWorld.Models.Configs;

/// <summary>Display-only order of owner-qualified media-library choices on this install.</summary>
public class UIMediaLibraryOrder
{
    public List<string> Keys { get; set; } = [];

    public string? Validate()
    {
        if (Keys == null || Keys.Count > 10000)
            return "Media-library order must contain at most 10000 entries.";
        if (Keys.Any(key => string.IsNullOrWhiteSpace(key) || key.Length > 512 || key.Any(char.IsControl)) ||
            Keys.Distinct(StringComparer.Ordinal).Count() != Keys.Count)
            return "Media-library order contains an invalid or duplicate key.";
        return null;
    }
}
