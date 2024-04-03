using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.DiffHandlers
{
    public abstract class BmAbstractDiffHandler<TValue> : IBulkModificationDiffHandler
    {
        protected abstract BulkModificationFilterableProperty Property { get; }
        protected abstract void SetProperty(Models.Domain.Resource resource, TValue? value, BulkModificationDiff diff);
        public void Apply(Models.Domain.Resource resource, BulkModificationDiff diff) => ApplyOrRevert(resource, diff, true);
        public void Revert(Models.Domain.Resource resource, BulkModificationDiff diff) => ApplyOrRevert(resource, diff, false);

        /// <summary>
        /// 
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="diff"></param>
        /// <param name="apply">True for applying, false for reverting</param>
        protected virtual void ApplyOrRevert(Models.Domain.Resource resource, BulkModificationDiff diff, bool apply)
        {
            if (diff.Property != Property)
            {
                throw new Exception(
                    $"Diff with property [{diff.Property}] can not be handled by current handler [{GetType().Name}]");
            }

            var newValue = apply ? diff.NewValue : diff.CurrentValue;

            // todo: deserialization should be consistent with how value is serialized during diff creation.
            var value = string.IsNullOrEmpty(newValue)
                ? default
                : JsonConvert.DeserializeObject<TValue>(newValue);

            SetProperty(resource, value, diff);
        }
    }
}