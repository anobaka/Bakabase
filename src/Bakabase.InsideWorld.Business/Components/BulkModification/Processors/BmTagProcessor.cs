using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using System.Collections;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures.BmMultipleValueProcessor;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    public class BmTagProcessor : BmCommonMultipleValueProcessor<TagDto>
    {

        protected override BmMultipleValueProcessorValue<int, string, TextProcessValue>
            ParseProcessorValue(BulkModificationProcess process) => process.ToTagProcessorValue();

        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Tag;
        protected override List<TagDto>? GetValues(ResourceDto resource) => resource.Tags;

        protected override void SetValues(ResourceDto resource, List<TagDto>? values)
        {
            resource.Tags = values;
        }

        protected override int GetValueKey(TagDto value) => value.Id;

        protected override TagDto CreateNewValue(int id) => new() {Id = id};

        protected override string GetValueBizKey(TagDto value) => value.Name;

        protected override TagDto CreateNewValue(string value) => new() {Name = value};
    }
}