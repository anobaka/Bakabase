using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;

namespace Bakabase.InsideWorld.Business.Components.Dependency.Implementations.Updater.Models
{
    public record UpdaterVersion : DependentComponentVersion
    {
        public string OssPrefix { get; set; }
    }
}
