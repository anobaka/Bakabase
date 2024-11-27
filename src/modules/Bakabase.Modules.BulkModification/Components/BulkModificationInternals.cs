using System.Collections.Concurrent;
using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Components.Processors;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components;

public class BulkModificationInternals
{
    private static readonly BulkModificationProcessDescriptor TextValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(TextProcessingOperation),
            typeof(BulkModificationTextProcessOptions), typeof(BulkModificationTextProcessorOptions));

    public static ConcurrentDictionary<PropertyType, BulkModificationProcessDescriptor>
        PropertyTypeProcessorDescriptorMap = new ConcurrentDictionary<PropertyType, BulkModificationProcessDescriptor>(
            new Dictionary<PropertyType, BulkModificationProcessDescriptor>()
                {{PropertyType.SingleLineText, TextValueProcessDescriptor}});

    public static ConcurrentDictionary<StandardValueType, IBulkModificationProcessor> ProcessorMap =
        new ConcurrentDictionary<StandardValueType, IBulkModificationProcessor>(
            new Dictionary<StandardValueType, IBulkModificationProcessor>()
            {

            });
}