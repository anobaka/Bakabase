namespace Bakabase.Modules.BulkModification.Models.Db
{
    public record BulkModificationDiffDbModel
    {
        public int Id { get; set; }
        public int BulkModificationId { get; set; }

        /// <summary>
        /// redundancy. Null when the resource has no local files — it is known to Bakabase but
        /// not materialized on disk yet.
        /// </summary>
        public string? ResourcePath { get; set; }

        public int ResourceId { get; set; }

        public string Diffs { get; set; } = null!;
    }
}