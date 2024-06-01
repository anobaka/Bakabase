using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bakabase.Modules.CustomProperty.Models.Domain.Constants;
using CsQuery.ExtensionMethods.Internal;

namespace Bakabase.InsideWorld.Business.Models.Domain
{
    public record Resource : Abstractions.Models.Domain.Resource
    {
        public Resource? Parent { get; set; }

        public Dictionary<ResourcePropertyType, Dictionary<int, Property>>? Properties { get; set; }

        public record Property(
            string? Name,
            StandardValueType ValueType,
            StandardValueType BizValueType,
            List<Property.PropertyValue>? Values)
        {
            public string? Name { get; set; } = Name;
            public List<PropertyValue>? Values { get; set; } = Values;
            public StandardValueType ValueType { get; set; } = ValueType;
            public StandardValueType BizValueType { get; set; } = BizValueType;

            public record PropertyValue(
                int Scope,
                object? Value,
                object? BizValue,
                object? AliasAppliedBizValue)
            {
                public int Scope { get; set; } = Scope;
                public object? Value { get; set; } = Value;
                public object? BizValue { get; set; } = BizValue ?? Value;
                public object? AliasAppliedBizValue { get; set; } = AliasAppliedBizValue ?? BizValue ?? Value;
            }
        }

        public Category? Category { get; set; }
        [Obsolete] public List<TagDto>? Tags { get; set; }
        [Obsolete] public string? Introduction { get; set; }
        [Obsolete] public Dictionary<string, List<CustomResourceProperty>>? CustomProperties { get; set; }
        [Obsolete] public SeriesDto? Series { get; set; }
        [Obsolete] public decimal Rate { get; set; }
        [Obsolete] public string? Name { get; set; }
        [Obsolete] public VolumeDto? Volume { get; set; }
        [Obsolete] public ResourceLanguage Language { get; set; } = ResourceLanguage.NotSet;
        [Obsolete] public List<PublisherDto>? Publishers { get; set; }
        [Obsolete] public List<OriginalDto>? Originals { get; set; }
        [Obsolete] public DateTime? ReleaseDt { get; set; }
    }
}