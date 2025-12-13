namespace Bakabase.Abstractions.Models.Domain;

public record MediaLibraryV2
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    [Obsolete]
    public List<string> Paths { get; set; } = [];
    [Obsolete]
    public int? TemplateId { get; set; }
    public int ResourceCount { get; set; }
    public string? Color { get; set; }
    [Obsolete]
    public string? SyncVersion { get; set; }
    [Obsolete]
    public List<MediaLibraryPlayer>? Players { get; set; }
    [Obsolete]
    public MediaLibraryTemplate? Template { get; set; }

    [Obsolete]
    public bool SyncMayBeOutdated
    {
        get
        {
            if (Template != null)
            {
                return SyncVersion != Template?.GetSyncVersion();
            }

            return false;
        }
    }
}