using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Tbd
{
    internal class
        CustomPropertiesPropertyProcessor : BulkModificationProcessor<Dictionary<string, List<CustomResourceProperty>>>
    {
        protected override Dictionary<string, List<CustomResourceProperty>> Merge(
            Dictionary<string, List<CustomResourceProperty>>? current,
            Dictionary<string, List<CustomResourceProperty>> @new)
        {
            if (current == null)
            {
                return @new;
            }

            return @new.Any() ? current.Merge(@new) : current;
        }
    }
}