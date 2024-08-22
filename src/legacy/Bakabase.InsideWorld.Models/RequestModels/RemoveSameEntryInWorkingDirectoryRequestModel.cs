using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class RemoveSameEntryInWorkingDirectoryRequestModel
    {
        public string WorkingDir { get; set; } = string.Empty;
        public string EntryPath { get; set; } = string.Empty;
    }   
}
