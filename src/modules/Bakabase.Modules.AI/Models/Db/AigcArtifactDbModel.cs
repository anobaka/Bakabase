using System.ComponentModel.DataAnnotations;

namespace Bakabase.Modules.AI.Models.Db;

public record AigcArtifactDbModel
{
    [Key]
    public int Id { get; set; }

    public int RunId { get; set; }
    public int GeneratorId { get; set; }
    public int OrdinalInRun { get; set; }

    /// <summary>
    /// Path relative to <see cref="Bakabase.Abstractions.Components.FileSystem.IFileManager.BaseDir"/>.
    /// Resolved at read time via IAppDataPathRelocator. Never store an absolute path here.
    /// </summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Optional: id of the Resource that owns this artifact's file.</summary>
    public int? ResourceId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
