using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.FileExplorer
{
    public enum IwFsEntryChangeType
    {
        Created = 1,
        Renamed = 2,
        Changed = 3,
        Deleted = 4,
        TaskChanged = 5
    }
}
