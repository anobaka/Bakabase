using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Tasks
{
    public static class BackgroundTaskExtensions
    {
        public static BackgroundTaskDto GetInformation(this BackgroundTask t)
        {
            if (t == null)
            {
                return null;
            }

            var ti = new BackgroundTaskDto
            {
                Id = t.Id,
                Message = t.Message,
                Name = t.Name,
                StartDt = t.StartDt,
                Status = t.Status,
                Percentage = t.Percentage,
                CurrentProcess = t.CurrentProcess,
                Level = t.Level
            };
            if (t.Exception != null)
            {
                ti.Message += $"[Exception]{t.Exception.BuildFullInformationText()}";
            }

            return ti;
        }
    }
}