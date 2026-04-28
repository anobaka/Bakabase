using System.Collections.Generic;

namespace Bakabase.Service.Models.View;

public class UnknownResourcesCountViewModel
{
    public int UnknownMediaLibraryCount { get; set; }
    public Dictionary<int, int> UnknownPathCountByMediaLibraryId { get; set; } = new();
}


