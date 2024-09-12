using System.Threading.Tasks;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Modules.CustomProperty.Abstractions.Services;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;

namespace Bakabase.Service.Controllers
{
    [Route("~/property")]
    public class PropertyController(ICustomPropertyService customPropertyService) : Controller
    {
        private ICustomPropertyService _customPropertyService = customPropertyService;
    }
}
