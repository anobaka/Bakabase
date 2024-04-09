using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Domain;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Models.View
{
    public record MigrationTargetViewModel : MigrationTarget
    {
        public List<PropertyTypeCandidate> TargetCandidates { get; set; } = null!;

        public record PropertyTypeCandidate
        {
            public CustomPropertyType Type { get; set; }
            public Dictionary<int, List<string>>? LossData { get; set; }
        }
    }
}