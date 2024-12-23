using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Tasks
{
    public class BackgroundTask : IDisposable
    {
        public BackgroundTask(string name, CancellationTokenSource cts,
            BackgroundTaskLevel level = BackgroundTaskLevel.Default,
            Func<int, Task> onProgressChange = null, Func<Task> onComplete = null,
            Func<BackgroundTask, Task> onFail = null)
        {
            Name = name;
            Cts = cts;
            Level = level;
            _onProgressChange = onProgressChange;
            _onComplete = onComplete;
            _onFail = onFail;
            cts.Token.Register(() => { Status = BackgroundTaskStatus.Failed; });
        }

        public string Id { get; } = Guid.NewGuid().ToString();
        public DateTime StartDt { get; } = DateTime.Now;
        public string Name { get; }
        public BackgroundTaskLevel Level { get; }
        public CancellationTokenSource Cts { get; }
        private readonly Func<int, Task> _onProgressChange;
        private readonly Func<Task> _onComplete;
        private readonly Func<BackgroundTask, Task> _onFail;

        private BackgroundTaskStatus _status = BackgroundTaskStatus.Running;

        public BackgroundTaskStatus Status
        {
            get => _status;
            set
            {
                if (_status != value)
                {
                    _status = value;
                    OnChange?.Invoke();
                    OnStatusChange?.Invoke();
                    switch (_status)
                    {
                        case BackgroundTaskStatus.Running:
                            break;
                        case BackgroundTaskStatus.Complete:
                            _onComplete?.Invoke();
                            break;
                        case BackgroundTaskStatus.Failed:
                            _onFail?.Invoke(this);
                            break;
                        default:
                            throw new ArgumentOutOfRangeException();
                    }
                }
            }
        }

        private int _percentage;

        public int Percentage
        {
            get => _percentage;
            set
            {
                if (_percentage != value)
                {
                    _percentage = value;
                    OnChange?.Invoke();
                    _onProgressChange?.Invoke(_percentage);
                }
            }
        }

        private string _currentProcess;

        public string CurrentProcess
        {
            get => _currentProcess;
            set
            {
                if (_currentProcess != value)
                {
                    _currentProcess = value;
                    OnChange?.Invoke();
                }
            }
        }

        public Exception? Exception { get; set; }
        public string Message { get; set; }
        public event Func<Task> OnChange;
        public event Func<Task> OnStatusChange;

        public async Task WaitAsync()
        {
            while (Status == BackgroundTaskStatus.Running)
            {
                await Task.Delay(10);
            }
        }

        public void Dispose()
        {
            Cts?.Dispose();
        }
    }
}