using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.ResponseModels
{
    public record ThirdPartyOverviewState
    {
        public string Id { get; set; } = null!;
        public string DisplayName { get; set; } = null!;
    }
}