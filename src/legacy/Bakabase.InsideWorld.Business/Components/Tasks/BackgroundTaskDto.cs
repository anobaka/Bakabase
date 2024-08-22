using System;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Tasks
{
    public record BackgroundTaskDto
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public DateTime StartDt { get; set; }
        public BackgroundTaskStatus Status { get; set; } = BackgroundTaskStatus.Running;
        public string Message { get; set; }
        public int Percentage { get; set; }
        public string CurrentProcess { get; set; }
        public BackgroundTaskLevel Level { get; set; }
    }
}