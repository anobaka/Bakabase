﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public record DownloadTaskStartRequestModel
    {
        public int[] Ids { get; set; } = Array.Empty<int>();
        public bool StopConflicts { get; set; }
    }
}
