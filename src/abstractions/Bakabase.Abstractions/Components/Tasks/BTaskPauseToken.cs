namespace Bakabase.Abstractions.Components.Tasks;

public struct BTaskPauseToken
{
    private bool _isPauseRequested;

    public void Pause()
    {
        _isPauseRequested = true;
    }

    public void Resume()
    {
        _isPauseRequested = false;
    }

    public async Task PauseIfRequested()
    {
        while (_isPauseRequested)
        {
            await Task.Delay(100);
        }
    }
}