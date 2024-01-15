using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Models.Constants
{
    public enum DownloadTaskActionOnConflict
    {
        NotSet = 0,
        StopOthers = 1,
        Ignore = 2,
    }
}