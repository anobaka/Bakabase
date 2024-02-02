using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants
{
    public enum BulkModificationVariableSource
    {
        None = 1,
        FileName,
        FileNameWithoutExtension,
        FullPath,
        DirectoryName,
        Name,
    }
}
