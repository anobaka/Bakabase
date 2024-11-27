using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.Modules.BulkModification.Models.Db;

namespace Bakabase.Modules.BulkModification.Abstractions.Components
{
    public interface IBulkModificationDiffHandler
    {
        void Apply(Bakabase.Abstractions.Models.Domain.Resource resource, BulkModificationDiffDbModel diff);
        void Revert(Bakabase.Abstractions.Models.Domain.Resource resource, BulkModificationDiffDbModel diff);
    }
}