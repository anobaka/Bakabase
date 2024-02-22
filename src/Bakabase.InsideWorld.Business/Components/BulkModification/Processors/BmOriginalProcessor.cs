using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmMultipleValueProcessor;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    public class BmOriginalProcessor : BmCommonMultipleValueProcessor<OriginalDto>
    {
        protected override List<OriginalDto>? GetValues(ResourceDto resource) => resource.Originals;

        protected override void SetValues(ResourceDto resource, List<OriginalDto>? values)
        {
            resource.Originals = values;
        }

        protected override int GetValueKey(OriginalDto value) => value.Id;

        protected override OriginalDto CreateNewValue(int id) => new() {Id = id};

        protected override string GetValueBizKey(OriginalDto value) => value.Name;

        protected override OriginalDto CreateNewValue(string value) => new() {Name = value};
        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Original;

        protected override BmMultipleValueProcessorValue<int, string, TextProcessValue>
            ParseProcessorValue(BulkModificationProcess process) => process.ToOriginalProcessorValue();
    }
}