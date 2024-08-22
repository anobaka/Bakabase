using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Lux.Models
{
    public record LuxVersion : DependentComponentVersion
    {
        public string DownloadUrl { get; set; } = null!;
    }
}
