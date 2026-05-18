using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Service.Models.Input;

public class BindPropertyToMatchingProfilesInputModel
{
    public PropertyPool Pool { get; set; }
    public int Id { get; set; }
}
