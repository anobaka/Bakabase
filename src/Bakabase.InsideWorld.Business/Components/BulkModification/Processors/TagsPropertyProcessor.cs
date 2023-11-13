using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    internal class TagsPropertyProcessor : BulkModificationProcessor<List<TagDto>>
    {
        protected override List<TagDto> Merge(List<TagDto>? current, List<TagDto> @new)
        {
            if (current == null)
            {
                return @new;
            }

            return @new.Any() ? current.Merge(@new) : current;
        }
    }
}