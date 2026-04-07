using System.ComponentModel.DataAnnotations;

namespace Bakabase.Abstractions.Models.Db
{
    public class PasswordDbModel
    {
        [Key] [MaxLength(64)] public string Text { get; set; } = null!;
        public int UsedTimes { get; set; }
        public DateTime LastUsedAt { get; set; }
    }
}