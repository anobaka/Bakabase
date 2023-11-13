using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    public interface IBulkModificationProcessor
    {
        ResourceDiff? Preview(BulkModificationProcess process, ResourceDto resource);
        void Process(BulkModificationProcess process, ResourceDto resource);
    }
}
