using System;
using System.ComponentModel.DataAnnotations;
using Bakabase.Modules.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.Modules.BulkModification.Models.Db
{
    public record BulkModificationDbModel
    {
        [Key] public int Id { get; set; }
        public string Name { get; set; } = null!;
        public bool IsActive { get; set; }
        /// <summary>
        /// 搜索条件 JSON（存储 ResourceSearchDbModel）
        /// </summary>
        public string? SearchJson { get; set; }
        public string? Processes { get; set; }
        /// <summary>
        /// JSON-serialized List&lt;PropertyValueScopePreference&gt;: per (PropertyPool, PropertyId) priority chain
        /// applied to every filtered resource on Apply. Each item with empty Priorities clears the preference.
        /// </summary>
        public string? ScopePreferenceConfigs { get; set; }
        public bool DeleteResources { get; set; }
        public bool DeleteFiles { get; set; }
        public string? Variables { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public string? FilteredResourceIds { get; set; }
        public DateTime? AppliedAt { get; set; }
    }
}
