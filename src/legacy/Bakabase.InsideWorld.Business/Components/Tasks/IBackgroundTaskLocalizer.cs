using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.Tasks;

public interface IBackgroundTaskLocalizer
{
    string? Name(BackgroundTaskName name);
}