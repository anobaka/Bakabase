using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Configurations.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Configurations.Models.Input
{
    public class ResourceOptionsPatchInputModel
    {
        public AdditionalCoverDiscoveringSource[]? AdditionalCoverDiscoveringSources { get; set; }
        public ResourceOptions.CoverOptionsModel? CoverOptions { get; set; }
        public PropertyValueScope[]? PropertyValueScopePriority { get; set; }
    }
}
