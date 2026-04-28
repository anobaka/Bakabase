using Bootstrap.Components.Logging.LogService.Services;

namespace Bakabase.Service.Controllers
{
    public class LogController : Bootstrap.Components.Logging.LogService.Controllers.LogController
    {
        public LogController(LogService service) : base(service)
        {
        }
    }
}
