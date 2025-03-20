namespace Bakabase.Abstractions.Components.Tasks;

public class BTaskPauseToken(CancellationToken ct)
{
    private bool _isPauseRequested;
    private bool _isPaused;
    private const int WaitInterval = 100;

    public async Task Pause()
    {
        _isPauseRequested = true;
        while (!_isPaused)
        {
            await Task.Delay(WaitInterval, ct);
        }
    }

    public Task Resume()
    {
        _isPauseRequested = false;
        _isPaused = false;
        return Task.CompletedTask;
    }

    public async Task PauseIfRequested()
    {
        if (_isPauseRequested)
        {
            _isPaused = true;
        }

        while (_isPauseRequested)
        {
            await Task.Delay(WaitInterval, ct);
        }
    }
}