namespace Bakabase.Modules.BulkModification.Models.Db
{
    public record BulkModificationDiffDbModel
    {
        public int Id { get; set; }
        public int BulkModificationId { get; set; }

        /// <summary>
        /// redundancy
        /// </summary>
        public string ResourcePath { get; set; } = null!;

        public int ResourceId { get; set; }

        public string Diffs { get; set; } = null!;
    }
}