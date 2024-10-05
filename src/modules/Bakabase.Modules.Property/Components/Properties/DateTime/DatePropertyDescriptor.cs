using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Modules.Property.Components.Properties.DateTime;

public class DatePropertyDescriptor : DateTimePropertyDescriptor
{
    public override PropertyType Type => PropertyType.Date;
}