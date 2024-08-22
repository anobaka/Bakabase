using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.FileExplorer;
using Bakabase.InsideWorld.Models.Constants;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.BackgroundTask
{
    public class ResourceTaskInfo
    {
        public int Id { get; set; }
        public string Summary { get; set; }
        public ResourceTaskType Type { get; set; }
        public int Percentage { get; set; }
        public string Error { get; set; }
        public string BackgroundTaskId { get; set; }
        public ResourceTaskOperationOnComplete OperationOnComplete { get; set; }
    }
}
