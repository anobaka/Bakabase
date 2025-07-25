﻿using System;
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
            public bool DisableMediaPreviewer { get; set; }
            public bool DisableCache { get; set; }
            public CoverFit CoverFit { get; set; } = CoverFit.Contain;
            public ResourceDisplayContent DisplayContents { get; set; } = ResourceDisplayContent.All;
            public bool DisableCoverCarousel { get; set; }
            public bool DisplayResourceId { get; set; }
            public bool HideResourceTimeInfo { get; set; }
        }
    }
}