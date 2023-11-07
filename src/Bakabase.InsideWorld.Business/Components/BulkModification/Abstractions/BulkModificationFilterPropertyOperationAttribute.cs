using System;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public class BulkModificationFilterPropertyOperationAttribute : Attribute
    {
        public BulkModificationFilterPropertyOperationAttribute(BulkModificationFilterOperation operation,
            Type? targetType)
        {
            Operation = operation;
            TargetType = targetType;
        }

        public BulkModificationFilterOperation Operation { get; }
        public Type? TargetType { get; }
    }
}