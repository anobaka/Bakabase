using Bootstrap.Components.Configuration.SystemProperty;
using Bootstrap.Components.Configuration.SystemProperty.Services;

namespace Bakabase.InsideWorld.App.Controllers
{
    public class SystemPropertyController : Bootstrap.Components.Configuration.SystemProperty.Controllers.SystemPropertyController
    {
        public SystemPropertyController(SystemPropertyService service) : base(service)
        {
        }
    }
}
