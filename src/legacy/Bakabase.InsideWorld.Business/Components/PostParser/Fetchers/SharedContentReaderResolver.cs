using System.Collections.Generic;
using System.Linq;

namespace Bakabase.InsideWorld.Business.Components.PostParser.Fetchers;

/// <summary>
/// Picks the reader for a reference: the highest-priority one that says it can read it. Site-aware
/// readers sit above the general page reader, which sits above the pasted-text reader.
/// </summary>
public class SharedContentReaderResolver(IEnumerable<ISharedContentReader> readers)
{
    private readonly List<ISharedContentReader> _ordered =
        readers.OrderByDescending(r => r.Priority).ToList();

    public ISharedContentReader? Resolve(string reference) =>
        _ordered.FirstOrDefault(r => r.CanRead(reference));
}
