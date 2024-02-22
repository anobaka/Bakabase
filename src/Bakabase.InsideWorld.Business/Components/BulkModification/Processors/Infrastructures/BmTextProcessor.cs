using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures
{
    public abstract class BmTextProcessor : BmAbstractBmProcessor<TextProcessValue>
    {
        protected override Task ProcessInternal(TextProcessValue value, ResourceDto resource,
            Dictionary<string, string?> variables, string? propertyKey)
        {
            var currentValue = GetValue(resource, propertyKey);
            var newValue = currentValue.Process(value);
            SetValue(resource, propertyKey, newValue);
            return Task.CompletedTask;
        }

        protected abstract string? GetValue(ResourceDto resource, string? propertyKey);
        protected abstract void SetValue(ResourceDto resource, string? propertyKey, string? value);
    }
}