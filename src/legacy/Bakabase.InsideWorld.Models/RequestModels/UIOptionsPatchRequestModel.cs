using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using static Bakabase.InsideWorld.Models.Configs.UIOptions;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class UIOptionsPatchRequestModel
    {
        public UIResourceOptions? Resource { get; set; }
        public StartupPage? StartupPage { get; set; }
    }
}
