﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class FileDecompressRequestModel
    {
        public string[] Paths { get; set; } = Array.Empty<string>();
        public string? Password { get; set; }
    }
}
