using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.CustomProperty.Abstractions.Components;

namespace Bakabase.Modules.CustomProperty.Abstractions.Models.Domain.Constants
{
    public enum CustomPropertyType
    {
        [CustomProperty(StandardValueType.String, StandardValueType.String)]
        SingleLineText = 1,

        [CustomProperty(StandardValueType.String, StandardValueType.String)]
        MultilineText,

        [CustomProperty(StandardValueType.String, StandardValueType.String)]
        SingleChoice,

        [CustomProperty(StandardValueType.ListString, StandardValueType.ListString)]
        MultipleChoice,

        [CustomProperty(StandardValueType.Decimal, StandardValueType.Decimal)]
        Number,

        [CustomProperty(StandardValueType.Decimal, StandardValueType.Decimal)]
        Percentage,

        [CustomProperty(StandardValueType.Decimal, StandardValueType.Decimal)]
        Rating,

        [CustomProperty(StandardValueType.Boolean, StandardValueType.Boolean)]
        Boolean,

        [CustomProperty(StandardValueType.Link, StandardValueType.Link)]
        Link,

        [CustomProperty(StandardValueType.ListString, StandardValueType.ListString)]
        Attachment,

        [CustomProperty(StandardValueType.DateTime, StandardValueType.DateTime)]
        Date,

        [CustomProperty(StandardValueType.DateTime, StandardValueType.DateTime)]
        DateTime,

        [CustomProperty(StandardValueType.Time, StandardValueType.Time)]
        Time,

        [CustomProperty(StandardValueType.String, StandardValueType.String)]
        Formula,

        [CustomProperty(StandardValueType.ListString, StandardValueType.ListListString)]
        Multilevel,

        [CustomProperty(StandardValueType.ListString, StandardValueType.ListTag)]
        Tags,
    }
}