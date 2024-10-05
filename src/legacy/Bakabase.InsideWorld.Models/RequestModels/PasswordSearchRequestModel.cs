using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants.Aos;
using Bootstrap.Models.RequestModels;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public record PasswordSearchRequestModel : SearchRequestModel
    {
        public PasswordSearchOrder? Order { get; set; }
    }
}
