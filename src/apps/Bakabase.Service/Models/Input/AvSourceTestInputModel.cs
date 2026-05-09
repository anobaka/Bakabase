using System.Collections.Generic;

namespace Bakabase.Service.Models.Input;

public class AvSourceTestInputModel
{
    public string? Number { get; set; }

    /// <summary>
    /// When set, restricts the test to these source ids. Empty / null = test all known sources.
    /// </summary>
    public List<string>? Sources { get; set; }
}
