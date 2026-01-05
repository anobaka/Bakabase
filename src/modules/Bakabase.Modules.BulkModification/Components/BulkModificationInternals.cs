using System.Collections.Concurrent;
using Bakabase.Abstractions.Components.Property;
using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.BulkModification.Abstractions.Components;
using Bakabase.Modules.BulkModification.Components.Processors.Boolean;
using Bakabase.Modules.BulkModification.Components.Processors.DateTime;
using Bakabase.Modules.BulkModification.Components.Processors.Decimal;
using Bakabase.Modules.BulkModification.Components.Processors.Link;
using Bakabase.Modules.BulkModification.Components.Processors.ListListString;
using Bakabase.Modules.BulkModification.Components.Processors.ListString;
using Bakabase.Modules.BulkModification.Components.Processors.ListTag;
using Bakabase.Modules.BulkModification.Components.Processors.String;
using Bakabase.Modules.BulkModification.Components.Processors.Time;
using Bootstrap.Extensions;

namespace Bakabase.Modules.BulkModification.Components;

public class BulkModificationInternals
{
    private static readonly BulkModificationProcessDescriptor StringValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationStringProcessOperation),
            typeof(BulkModificationStringProcessOptions), typeof(BulkModificationStringProcessorOptions));

    private static readonly BulkModificationProcessDescriptor ListStringValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationListStringProcessOperation),
            typeof(BulkModificationListStringProcessOptions), typeof(BulkModificationListStringProcessorOptions));

    private static readonly BulkModificationProcessDescriptor DecimalValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationDecimalProcessOperation),
            typeof(BulkModificationDecimalProcessOptions), typeof(BulkModificationDecimalProcessorOptions));

    private static readonly BulkModificationProcessDescriptor BooleanValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationBooleanProcessOperation),
            typeof(BulkModificationBooleanProcessOptions), typeof(BulkModificationBooleanProcessorOptions));

    private static readonly BulkModificationProcessDescriptor DateTimeValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationDateTimeProcessOperation),
            typeof(BulkModificationDateTimeProcessOptions), typeof(BulkModificationDateTimeProcessorOptions));

    private static readonly BulkModificationProcessDescriptor TimeValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationTimeProcessOperation),
            typeof(BulkModificationTimeProcessOptions), typeof(BulkModificationTimeProcessorOptions));

    private static readonly BulkModificationProcessDescriptor LinkValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationLinkProcessOperation),
            typeof(BulkModificationLinkProcessOptions), typeof(BulkModificationLinkProcessorOptions));

    private static readonly BulkModificationProcessDescriptor ListListStringValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationListListStringProcessOperation),
            typeof(BulkModificationListListStringProcessOptions), typeof(BulkModificationListListStringProcessorOptions));

    private static readonly BulkModificationProcessDescriptor ListTagValueProcessDescriptor =
        new BulkModificationProcessDescriptor(typeof(BulkModificationListTagProcessOperation),
            typeof(BulkModificationListTagProcessOptions), typeof(BulkModificationListTagProcessorOptions));

    public static ConcurrentDictionary<PropertyType, BulkModificationProcessDescriptor>
        PropertyTypeProcessorDescriptorMap = new ConcurrentDictionary<PropertyType, BulkModificationProcessDescriptor>(
            new Dictionary<PropertyType, BulkModificationProcessDescriptor>()
            {
                // String
                {PropertyType.SingleLineText, StringValueProcessDescriptor},
                {PropertyType.MultilineText, StringValueProcessDescriptor},
                {PropertyType.Formula, StringValueProcessDescriptor},
                {PropertyType.SingleChoice, StringValueProcessDescriptor},
                // ListString
                {PropertyType.Attachment, ListStringValueProcessDescriptor},
                {PropertyType.MultipleChoice, ListStringValueProcessDescriptor},
                // Decimal
                {PropertyType.Number, DecimalValueProcessDescriptor},
                {PropertyType.Percentage, DecimalValueProcessDescriptor},
                {PropertyType.Rating, DecimalValueProcessDescriptor},
                // Boolean
                {PropertyType.Boolean, BooleanValueProcessDescriptor},
                // DateTime
                {PropertyType.Date, DateTimeValueProcessDescriptor},
                {PropertyType.DateTime, DateTimeValueProcessDescriptor},
                // Time
                {PropertyType.Time, TimeValueProcessDescriptor},
                // Link
                {PropertyType.Link, LinkValueProcessDescriptor},
                // ListListString
                {PropertyType.Multilevel, ListListStringValueProcessDescriptor},
                // ListTag
                {PropertyType.Tags, ListTagValueProcessDescriptor},
            });

    public static BulkModificationStringProcessor StringProcessor = new BulkModificationStringProcessor();
    public static BulkModificationListStringProcessor ListStringProcessor = new BulkModificationListStringProcessor();
    public static BulkModificationDecimalProcessor DecimalProcessor = new BulkModificationDecimalProcessor();
    public static BulkModificationBooleanProcessor BooleanProcessor = new BulkModificationBooleanProcessor();
    public static BulkModificationDateTimeProcessor DateTimeProcessor = new BulkModificationDateTimeProcessor();
    public static BulkModificationTimeProcessor TimeProcessor = new BulkModificationTimeProcessor();
    public static BulkModificationLinkProcessor LinkProcessor = new BulkModificationLinkProcessor();
    public static BulkModificationListListStringProcessor ListListStringProcessor = new BulkModificationListListStringProcessor();
    public static BulkModificationListTagProcessor ListTagProcessor = new BulkModificationListTagProcessor();

    public static ConcurrentDictionary<StandardValueType, IBulkModificationProcessor> ProcessorMap =
        new ConcurrentDictionary<StandardValueType, IBulkModificationProcessor>(
            new Dictionary<StandardValueType, IBulkModificationProcessor>()
            {
                {StandardValueType.String, StringProcessor},
                {StandardValueType.ListString, ListStringProcessor},
                {StandardValueType.Decimal, DecimalProcessor},
                {StandardValueType.Boolean, BooleanProcessor},
                {StandardValueType.DateTime, DateTimeProcessor},
                {StandardValueType.Time, TimeProcessor},
                {StandardValueType.Link, LinkProcessor},
                {StandardValueType.ListListString, ListListStringProcessor},
                {StandardValueType.ListTag, ListTagProcessor},
            });

    public static ConcurrentDictionary<PropertyPool, ConcurrentBag<int>> DisabledPropertyKeys =
        new ConcurrentDictionary<PropertyPool, ConcurrentBag<int>>(new Dictionary<PropertyPool, ConcurrentBag<int>>
        {
            {PropertyPool.Internal, new ConcurrentBag<int>(SpecificEnumUtils<InternalProperty>.Values.Cast<int>())}
        });
}
