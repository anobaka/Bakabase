using Bakabase.Abstractions.Components.StandardValue;

namespace Bakabase.Abstractions.Models.Domain.Constants
{
    public enum CustomPropertyType
    {
        [StandardValue(StandardValueType.String)]
        SingleLineText = 1,

        [StandardValue(StandardValueType.String)]
        MultilineText,

        [StandardValue(StandardValueType.String)]
        SingleChoice,

        [StandardValue(StandardValueType.ListString)]
        MultipleChoice,

        [StandardValue(StandardValueType.Decimal)]
        Number,

        [StandardValue(StandardValueType.Decimal)]
        Percentage,

        [StandardValue(StandardValueType.Decimal)]
        Rating,

        [StandardValue(StandardValueType.Boolean)]
        Boolean,

        [StandardValue(StandardValueType.Link)]
        Link,

        [StandardValue(StandardValueType.ListString)]
        Attachment,

        [StandardValue(StandardValueType.DateTime)]
        Date,

        [StandardValue(StandardValueType.DateTime)]
        DateTime,

        [StandardValue(StandardValueType.Time)]
        Time,

        [StandardValue(StandardValueType.String)]
        Formula,

        [StandardValue(StandardValueType.ListListString)]
        Multilevel,
    }
}