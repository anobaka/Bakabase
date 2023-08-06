﻿using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class DownloadTaskCreateRequestModel
    {
        [Required] public ThirdPartyId ThirdPartyId { get; set; }
        [Required] public int Type { get; set; }
        public Dictionary<string, string>? KeyAndNames { get; set; }
        public long? Interval { get; set; }
        public int? StartPage { get; set; }
        public int? EndPage { get; set; }
        public string? Checkpoint { get; set; }

        public bool ForceCreating { get; set; }
        [Required] public string DownloadPath { get; set; } = string.Empty;
    }
}