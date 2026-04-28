using System.Collections.Generic;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Property.Models.View;

namespace Bakabase.Service.Models.View
{
    public record ResourceSearchFilterViewModel
    {
        public PropertyPool? PropertyPool { get; set; }
        public int? PropertyId { get; set; }
        public SearchOperation? Operation { get; set; }

        /// <summary>
        /// Serialized
        /// </summary>
        public string? DbValue { get; set; }

        /// <summary>
        /// Serialized
        /// </summary>
        public string? BizValue { get; set; }
        public bool Disabled { get; set; }

        public List<SearchOperation>? AvailableOperations { get; set; }

        public PropertyViewModel? Property { get; set; }
        public PropertyViewModel? ValueProperty { get; set; }
    }
}