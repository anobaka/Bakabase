using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Db;

public record PathFilterDbModel: PathPropertyExtractor
{
    public HashSet<int>? ExtensionGroupIds { get; set; }
    public PathFilterFsType? FsType { get; set; }
    public HashSet<string>? Extensions { get; set; }
}