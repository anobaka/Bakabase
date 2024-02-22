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
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    public class BmPublishersProcessor : BmCommonMultipleValueProcessor<PublisherDto>
    {
        protected override List<PublisherDto>? GetValues(ResourceDto resource) => resource.Publishers;

        protected override void SetValues(ResourceDto resource, List<PublisherDto>? values)
        {
            resource.Publishers = values;
        }

        protected override int GetValueKey(PublisherDto value) => value.Id;

        protected override PublisherDto CreateNewValue(int id) => new() {Id = id};

        protected override string GetValueBizKey(PublisherDto value) => value.Name;

        protected override PublisherDto CreateNewValue(string value) => new() {Name = value};

        protected override BmMultipleValueProcessorValue<int, string, TextProcessValue>
            ParseProcessorValue(BulkModificationProcess process) => process.ToTagProcessorValue();

        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Publisher;
    }
}