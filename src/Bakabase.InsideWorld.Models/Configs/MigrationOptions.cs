using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options()]
    public record MigrationOptions
    {
        public List<PropertyMigrationOptions>? Properties { get; set; }

        public record PropertyMigrationOptions
        {
            public ResourceProperty Property { get; set; }
            public string? PropertyKey { get; set; }
        }
    }
}