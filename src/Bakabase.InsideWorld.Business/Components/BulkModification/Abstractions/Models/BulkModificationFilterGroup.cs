using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Text;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public class BulkModificationFilterGroup
    {
        public BulkModificationFilterGroupOperation Operation { get; set; }
        public List<BulkModificationFilter>? Filters { get; set; }
        public List<BulkModificationFilterGroup>? Groups { get; set; }

        public override string ToString()
        {
            var sb = new StringBuilder();
            if (Filters?.Any() == true)
            {
                sb.Append($"fs:{string.Join('|', Filters.Select(f => f.ToString()))}");
            }

            if (Groups?.Any() == true)
            {
                sb.Append($"gs:{string.Join('|', Groups.Select(g => g.ToString()))}");
            }

            return sb.ToString();
        }
    }
}