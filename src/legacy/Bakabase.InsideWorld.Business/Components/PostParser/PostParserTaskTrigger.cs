using System;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Components.PostParser.Services;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.PostParser;

public class PostParserTaskTrigger
{
    public const string TaskId = "ParseAllPosts";

    private readonly BTaskManager _btm;
    private readonly BTaskHandlerBuilder _taskBuilder;
    private readonly IBOptions<ThirdPartyOptions> _options;

    public PostParserTaskTrigger(
        BTaskManager btm,
        IBakabaseLocalizer localizer,
        IOptionsMonitor<ThirdPartyOptions> optionsMonitor, IBOptions<ThirdPartyOptions> options)
    {
        _btm = btm;
        _options = options;
        _taskBuilder = new BTaskHandlerBuilder
        {
            Id = TaskId,
            GetName = localizer.PostParser_ParseAll_TaskName,
            IsPersistent = true,
            Interval = TimeSpan.FromMinutes(1),
            Type = BTaskType.Any,
            Level = BTaskLevel.Default,
            ResourceType = BTaskResourceType.Any,
            Run = async (args) =>
            {
                var scope = args.RootServiceProvider.CreateAsyncScope();
                var service = scope.ServiceProvider.GetRequiredService<IPostParserTaskService>();
                await service.ParseAll(p => args.UpdateTask(x => x.Percentage = p),
                    p => args.UpdateTask(x => x.Process = p),
                    args.PauseToken,
                    args.CancellationToken);
            },
            StartNow = false,
        };

        optionsMonitor.OnChange(async o =>
        {
            if (o.AutomaticallyParsingPosts)
            {
                await Enable();
            }
            else
            {
                await Disable();
            }
        });
    }

    public async Task Initialize()
    {
        if (_options.Value.AutomaticallyParsingPosts)
        {
            await Enable();
        }
    }

    public async Task Start()
    {
        await _btm.Start(TaskId, () => _taskBuilder with {StartNow = true});
    }

    protected async Task Enable()
    {
        _btm.EnqueueSafely(_taskBuilder);
    }

    protected async Task Disable()
    {
        await _btm.Stop(TaskId);
        await _btm.Clean(TaskId);
    }
}