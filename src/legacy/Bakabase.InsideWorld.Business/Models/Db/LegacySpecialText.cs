using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Bakabase.InsideWorld.Business.Models.Db;

/// <summary>
/// The retired <c>SpecialTexts</c> table, kept mapped so
/// <c>Bakabase.Migrations.V240.TextSystemMigrator</c> can copy its rows into the unified text
/// vocabulary. Nothing else reads or writes it.
///
/// It lives here rather than in <c>Bakabase.Abstractions</c> so that modules — which all reference
/// abstractions — no longer see a dead model, and <see cref="Type"/> is a raw <see cref="int"/>
/// rather than the old <c>SpecialTextType</c> enum: the enum's only remaining job was to be read
/// once by the migrator, and while it existed it was still emitted into the frontend's generated
/// <c>constants.ts</c>.
/// </summary>
[Table("SpecialTexts")]
public record LegacySpecialText
{
    [Key] public int Id { get; set; }

    [Required, MaxLength(64)] public string Value1 { set; get; } = null!;

    [MaxLength(64)] public string? Value2 { set; get; }

    /// <summary>
    /// The old <c>SpecialTextType</c> value. Its members were carried over verbatim into
    /// <see cref="Bakabase.Abstractions.Models.Domain.Constants.WellKnownTextType"/>, so the
    /// migrator maps it by numeric value.
    /// </summary>
    public int Type { set; get; }
}
