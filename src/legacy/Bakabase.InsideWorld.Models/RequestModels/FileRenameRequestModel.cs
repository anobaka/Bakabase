using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.RequestModels
{
    public class FileRenameRequestModel
    {
        public string Fullname { get; set; } = string.Empty;
        public string NewName { get; set; } = string.Empty;
    }
}
