using Bakabase.InsideWorld.Models.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants
{
    public enum BulkModificationFilterProperty
    {
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        Category,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        MediaLibrary,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<string>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<string>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Contains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotContains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.StartsWith, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.EndsWith, typeof(string))]
        Name,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<string>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<string>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Contains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotContains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.StartsWith, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.EndsWith, typeof(string))]
        RawName,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Contains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotContains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.StartsWith, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.EndsWith, typeof(string))]
        RawFullname,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThanOrEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThanOrEquals, typeof(DateTime))]
        ReleaseDt,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThanOrEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThanOrEquals, typeof(DateTime))]
        CreateDt,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThanOrEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThanOrEquals, typeof(DateTime))]
        FileCreateDt,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<DateTime>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThanOrEquals, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThan, typeof(DateTime))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThanOrEquals, typeof(DateTime))]
        FileModifyDt,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        Publisher,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        Language,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        Volume,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        Original,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        Series,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        Tag,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Contains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotContains, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.StartsWith, typeof(string))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.EndsWith, typeof(string))]
        Introduction,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<decimal>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<decimal>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(decimal))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(decimal))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThan, typeof(decimal))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.GreaterThanOrEquals, typeof(decimal))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThan, typeof(decimal))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.LessThanOrEquals, typeof(decimal))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        Rate,

        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.In, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotIn, typeof(HashSet<int>))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.Equals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.NotEquals, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.IsNotNull, null)]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.StartsWith, typeof(int))]
        [BulkModificationFilterPropertyOperation(BulkModificationFilterOperation.EndsWith, typeof(int))]
        CustomProperty,
    }
}