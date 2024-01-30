using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Components.Configuration.Abstractions;

namespace Bakabase.InsideWorld.Models.Configs
{
    [Options]
    public class UIOptions
    {
        public UIResourceOptions Resource { get; set; }
        public StartupPage StartupPage { get; set; }

        public class UIResourceOptions
        {
            public int ColCount { get; set; }
            public bool ShowBiggerCoverWhileHover { get; set; }
        }
    }
}