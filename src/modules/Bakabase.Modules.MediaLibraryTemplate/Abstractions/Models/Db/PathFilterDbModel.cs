using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.MediaLibraryTemplate.Abstractions.Models.Db;

public record PathFilterDbModel
{
    public HashSet<int>? ExtensionGroupIds { get; set; }
    public PathFilterFsType? FsType { get; set; }
    public HashSet<string>? Extensions { get; set; }
}