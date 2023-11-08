using System;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models
{
    public record BulkModificationFilter
    {
        public BulkModificationProperty Property { get; set; }
        public string? PropertyKey { get; set; }
        public BulkModificationFilterOperation Operation { get; set; }
        public string? Target { get; set; }

        public override string ToString()
        {
            return $"[p]{Property},[k]{PropertyKey},[o]{Operation},[t]{Target}";
        }
    }
}