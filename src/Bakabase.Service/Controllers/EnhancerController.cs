using Bakabase.Modules.Enhancer.Abstractions.Components;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.Service.Controllers
{
    [Route("~/enhancer")]
    public class EnhancerController
    {
        private readonly IEnhancerDescriptors _descriptors;
        private readonly IEnhancerLocalizer _localizer;

        public EnhancerController(IEnhancerDescriptors descriptors, IEnhancerLocalizer localizer)
        {
            _descriptors = descriptors;
            _localizer = localizer;
        }

        [HttpGet("descriptor")]
        [SwaggerOperation(OperationId = "GetAllEnhancerDescriptors")]
        public ListResponse<IEnhancerDescriptor> GetAllDescriptors()
        {
            return new ListResponse<IEnhancerDescriptor>(_descriptors.Descriptors);
        }
    }
}