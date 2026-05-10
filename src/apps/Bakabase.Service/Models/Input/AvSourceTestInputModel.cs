using System.Collections.Generic;

namespace Bakabase.Service.Models.Input;

public class AvSourceTestInputModel
{
    public string? Number { get; set; }

    /// <summary>
    /// When set, restricts the test to these source ids. Empty / null = test all known sources.
    /// </summary>
    public List<string>? Sources { get; set; }

    /// <summary>
    /// Frontend i18n locale (e.g. "zh-CN", "en-US"). Used by sources whose URL embeds the
    /// language (airav, iqqtv, javlibrary). Null = use each source's default.
    /// </summary>
    public string? Language { get; set; }
}
