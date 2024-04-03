﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    public interface IBulkModificationDiffHandler
    {
        void Apply(Business.Models.Domain.Resource resource, BulkModificationDiff diff);
        void Revert(Business.Models.Domain.Resource resource, BulkModificationDiff diff);
    }
}