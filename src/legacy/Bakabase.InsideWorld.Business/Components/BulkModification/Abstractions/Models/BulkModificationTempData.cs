using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public class BulkModificationTempData
    {
        [Key] public int BulkModificationId { get; set; }
        public string ResourceIds { get; set; } = null!;

        private const char Separator = ',';
        public void SetResourceIds(IEnumerable<int> ids) => ResourceIds = string.Join(Separator, ids);
        public List<int> GetResourceIds() => ResourceIds.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
    }
}