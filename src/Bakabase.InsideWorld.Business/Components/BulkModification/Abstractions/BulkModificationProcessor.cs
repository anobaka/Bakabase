using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Extensions;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions
{
    public class BulkModificationProcessor<T> : IBulkModificationProcessor
    {
        protected virtual T Merge(T? current, T @new)
        {
            return @new;
        }

        private (T? Current, T? New) GetValues(BulkModificationProcess process, ResourceDto resource)
        {
            var current = (T?) process.Property.GetGetter()(resource);

            var newValue = string.IsNullOrEmpty(process.NewValue)
                ? default
                : JsonConvert.DeserializeObject<T>(process.NewValue);

            if (process.Property.IsListProperty() && process.Operation == BulkModificationDiffOperation.Merge)
            {
                newValue = Merge(current, newValue!);
            }

            return (current, newValue);
        }

        public ResourceDiff? Preview(BulkModificationProcess process, ResourceDto resource)
        {
            if (process.Operation is BulkModificationDiffOperation.None or BulkModificationDiffOperation.Ignore)
            {
                return null;
            }

            var (current, newValue) = GetValues(process, resource);

            return process.Property.Compare(current, newValue);
        }

        public virtual void Process(BulkModificationProcess process, ResourceDto resource)
        {
            if (process.Operation is BulkModificationDiffOperation.None or BulkModificationDiffOperation.Ignore)
            {
                return;
            }

            var (current, newValue) = GetValues(process, resource);

            process.Property.GetSetter()(resource, newValue);
        }
    }
}