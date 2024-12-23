using System;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.InsideWorld.Business.Models.Domain.Constants;
using Bakabase.InsideWorld.Models.Models.Aos;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Tasks
{
    public static class BackgroundTaskExtensions
    {
        public static BackgroundTaskDto GetInformation(this BackgroundTask t,
            IBackgroundTaskLocalizer? localizer = null)
        {
            var ti = new BackgroundTaskDto
            {
                Id = t.Id,
                Message = t.Message,
                Name = Enum.TryParse<BackgroundTaskName>(t.Name, out var en) ? (localizer?.Name(en) ?? t.Name) : t.Name,
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