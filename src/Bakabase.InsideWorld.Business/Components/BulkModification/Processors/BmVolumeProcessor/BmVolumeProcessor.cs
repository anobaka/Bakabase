using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models;
using Bakabase.InsideWorld.Business.Components.BulkModification.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.BulkModification.Processors.Infrastructures;
using Bakabase.InsideWorld.Business.Components.TextProcessing;
using Bakabase.InsideWorld.Models.Models.Dtos;

namespace Bakabase.InsideWorld.Business.Components.BulkModification.Processors.BmVolumeProcessor
{
    public class BmVolumeProcessor : BmAbstractBmProcessor<BmVolumeProcessorValue>
    {
        protected override async Task ProcessInternal(BmVolumeProcessorValue value, ResourceDto resource, Dictionary<string, string?> variables, string? propertyKey)
        {
            switch (value.Operation)
            {
                case BmVolumeProcessorOperation.Modify:
                {
                    foreach (var (property, tv) in value.PropertyModifications!)
                    {
                        switch (property)
                        {
                            case BmVolumeProcessorProperty.Name:
                            {
                                var name = (resource.Volume ??= new VolumeDto()).Name;
                                resource.Volume.Name = name.Process(tv)!;
                                break;
                            }
                            case BmVolumeProcessorProperty.Title:
                            {
                                var title = (resource.Volume ??= new VolumeDto()).Title;
                                resource.Volume.Title = title.Process(tv)!;
                                break;
                            }
                            default:
                                throw new ArgumentOutOfRangeException();
                        }
                    }

                    break;
                }
                case BmVolumeProcessorOperation.Remove:
                {
                    resource.Volume = null;
                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        protected override BmVolumeProcessorValue ParseProcessorValue(BulkModificationProcess process) =>
            process.ToVolumeProcessorValue();

        protected override BulkModificationProperty TargetProperty => BulkModificationProperty.Volume;
    }
}
