using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Service.Components.Tasks.Predefined;

public class EnhancementBTask(IBakabaseLocalizer localizer) : IPredefinedBTask
{
    private const string Key = "Enhancement";
    public BTaskDescriptorBuilder DescriptorBuilder { get; } = new BTaskDescriptorBuilder
    {
        GetName = () => localizer.BTask_Name(Key),
        GetDescription = null,
        GetMessageOnInterruption = null,
        OnProcessChange = null,
        OnPercentageChange = null,
        OnStatusChange = null,
        CancellationToken = null,
        Id = Key,
        Key = Key,
        Run = null,
        Args = new object?[]
        {
        },
        ConflictKeys = null,
        Level = (BTaskLevel) 0,
        Interval = null
    };
}