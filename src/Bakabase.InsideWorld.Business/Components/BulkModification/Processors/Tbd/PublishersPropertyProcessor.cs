using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Tbd
{
    public class PublishersPropertyProcessor : BulkModificationProcessor<List<PublisherDto>>
    {
        protected override List<PublisherDto> Merge(List<PublisherDto>? current, List<PublisherDto> @new)
        {
            if (current == null)
            {
                return @new;
            }

            return @new.Any() ? current.Merge(@new) : current;
        }
    }
}