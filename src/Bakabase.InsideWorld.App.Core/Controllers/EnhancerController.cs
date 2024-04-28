using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Enhancer;
using Bakabase.Abstractions.Components.Localization;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Abstractions.Models.Dto;
using Bakabase.InsideWorld.Business.Resources;
using Bakabase.Modules.Enhancer.Abstractions;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bootstrap.Extensions;
using Bootstrap.Models.ResponseModels;
using Microsoft.AspNetCore.Mvc;
using Swashbuckle.AspNetCore.Annotations;

namespace Bakabase.InsideWorld.App.Core.Controllers
{
    [Route("~/enhancer")]
    public class EnhancerController
    {
        private readonly IEnumerable<IEnhancerDescriptor> _descriptors;
        private readonly IBakabaseLocalizer _localizer;

        public EnhancerController(IEnumerable<IEnhancerDescriptor> descriptors, IBakabaseLocalizer localizer)
        {
            _descriptors = descriptors;
            _localizer = localizer;
        }

        [HttpGet("descriptor")]
        [SwaggerOperation(OperationId = "GetAllEnhancerDescriptors")]
        public ListResponse<EnhancerDescriptor> GetAllDescriptors()
        {
            var descriptors = _descriptors.Select(e => new EnhancerDescriptor(e.Id, _localizer.Enhancer_Name(e.Id),
                _localizer.Enhancer_Description(e.Id),
                e.Targets.Select(t => t with
                {
                    Name = _localizer.Enhancer_TargetName(e.Id, t.Id),
                    Description = _localizer.Enhancer_TargetDescription(e.Id, t.Id)
                }).ToArray()));

            return new ListResponse<EnhancerDescriptor>(descriptors);
        }
    }
}