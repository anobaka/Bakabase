using System.Threading;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Models.Configs;
using Bootstrap.Components.Configuration.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Bakabase.Service.Components.Notices;

/// <summary>
/// Opens a fresh install's notice baseline, so its UI can keep upgrade-only notices from
/// greeting an install that never ran the version they compare against
/// (<see cref="UIOptions.UINoticeOptions.BaselinePending"/>).
/// </summary>
/// <remarks>
/// <para>
/// Whether a start is an install's first is read from the version the install last ran,
/// <see cref="AppOptions.Version"/> — <see cref="AppOptions.IsNotInitialized"/> when it never
/// ran at all — which the host replaces with the running version once the start is under way
/// (today from <see cref="IHostApplicationLifetime.ApplicationStarted"/>, after migrations).
/// </para>
/// <para>
/// So it is read in the constructor, not in <see cref="StartAsync"/>: the host constructs
/// every hosted service before it starts any of them, so the answer does not depend on where
/// the host records the running version, as long as that happens once the host is running —
/// a lifetime callback, or any hosted service's start, whatever its place in the order. Only
/// recording it before the host is built could change it, and the data migrations, which
/// read the same value while the host runs, could not survive that either.
/// </para>
/// <para>
/// A first start that fails before the version is recorded is simply first again next time;
/// opening an open baseline changes nothing.
/// </para>
/// </remarks>
public sealed class NoticeBaselineInitializer : IHostedService
{
    private readonly IBOptionsManager<UIOptions> _uiOptions;
    private readonly ILogger<NoticeBaselineInitializer> _logger;
    private readonly bool _firstStart;

    public NoticeBaselineInitializer(
        IBOptions<AppOptions> appOptions,
        IBOptionsManager<UIOptions> uiOptions,
        ILogger<NoticeBaselineInitializer> logger)
    {
        _uiOptions = uiOptions;
        _logger = logger;
        _firstStart = appOptions.Value.IsNotInitialized();
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_firstStart || _uiOptions.Value.Notices?.BaselinePending == true)
        {
            return;
        }

        await _uiOptions.SaveAsync(options =>
        {
            options.Notices ??= new UIOptions.UINoticeOptions();
            options.Notices.BaselinePending = true;
        });
        _logger.LogInformation("First start of this install: its UI will record the upgrade-only notices it ships with as read.");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
