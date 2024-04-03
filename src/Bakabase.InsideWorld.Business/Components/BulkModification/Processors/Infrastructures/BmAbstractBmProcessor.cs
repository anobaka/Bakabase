
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures
{
    public abstract class BmAbstractBmProcessor<TProcessorValue> : IBulkModificationProcessor
    {
        public async Task Process(BulkModificationProcess process, Models.Domain.Resource resource,
            Dictionary<string, string?> variables)
        {
            if (process.Property != TargetProperty)
            {
                throw new InvalidOperationException(
                    $"The processor {GetType().Name} does not support the property {process.Property}");
            }

            var value = ParseProcessorValue(process);

            if (value == null)
            {
                throw new InvalidDataException("Value of process can not be null");
            }

            await ProcessInternal(value, resource, variables, process.PropertyKey);
        }

        protected abstract Task ProcessInternal(TProcessorValue value, Models.Domain.Resource resource,
            Dictionary<string, string?> variables, string? propertyKey);

        protected abstract TProcessorValue ParseProcessorValue(BulkModificationProcess process);
        protected abstract BulkModificationFilterableProperty TargetProperty { get; }
    }
}