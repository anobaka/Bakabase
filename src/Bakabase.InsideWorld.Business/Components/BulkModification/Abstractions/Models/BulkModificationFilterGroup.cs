using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public class BulkModificationFilterGroup
    {
        public BulkModificationFilterGroupOperation Operation { get; set; }
        public List<BulkModificationFilter>? Filters { get; set; }
        public List<BulkModificationFilterGroup>? Groups { get; set; }
    }
}