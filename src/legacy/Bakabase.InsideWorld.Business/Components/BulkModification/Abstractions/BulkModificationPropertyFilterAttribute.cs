using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class BulkModificationPropertyFilterAttribute : Attribute
    {
        public BulkModificationPropertyFilterAttribute(BulkModificationFilterOperation[] availableOperations)
        {
            AvailableOperations = availableOperations.ToHashSet();
        }

        public HashSet<BulkModificationFilterOperation> AvailableOperations { get; }
    }
}