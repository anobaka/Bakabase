using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bakabase.InsideWorld.Models.Models.Entities;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors
{
    /// <summary>
    /// todo: For now, this processor will ignore processing if there are multiple custom properties with the same key.
    /// </summary>
    public class BmCustomPropertiesProcessor : BmTextProcessor
    {
        protected override TextProcessValue ParseProcessorValue(BulkModificationProcess process) => process.ToCustomPropertyProcessorValue();

        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.CustomProperty;

        protected override string? GetValue(ResourceDto resource, string? propertyKey)
        {
            var properties = resource.CustomProperties!?.GetValueOrDefault(propertyKey!);
            return !(properties?.Count > 1) ? properties?.FirstOrDefault()?.Value : null;
        }

        protected override void SetValue(ResourceDto resource, string? propertyKey, string? value)
        {
            var properties = resource.CustomProperties!?.GetValueOrDefault(propertyKey!);
            if (properties?.Count > 1)
            {
                return;
            }

            if (string.IsNullOrEmpty(value))
            {
                if (properties != null)
                {
                    resource.CustomProperties?.Remove(propertyKey!);
                }
            }
            else
            {
                if (properties == null)
                {
                    properties = new List<CustomResourceProperty>
                    {
                        new CustomResourceProperty
                        {
                            Key = propertyKey!,
                            Value = value!,
                            // todo: customize type
                            ValueType = CustomDataType.String
                        }
                    };
                }
                else
                {
                    properties[0].Value = value;
                }
            }
        }
    }
}