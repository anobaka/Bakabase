using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class FileMoveRequestModel
    {
        public string DestDir { get; set; } = string.Empty;
        public string[] EntryPaths { get; set; } = Array.Empty<string>();
    }
}