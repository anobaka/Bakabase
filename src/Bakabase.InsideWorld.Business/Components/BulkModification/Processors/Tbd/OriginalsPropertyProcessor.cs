using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Tbd
{
    internal class OriginalsPropertyProcessor : BulkModificationProcessor<List<OriginalDto>>
    {
        protected override List<OriginalDto> Merge(List<OriginalDto>? current, List<OriginalDto> @new)
        {
            if (current == null)
            {
                return @new;
            }

            return @new.Any() ? current.Merge(@new) : current;
        }
    }
}
