﻿using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
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
        private readonly IEnumerable<EnhancerDescriptor> _descriptors;
        private readonly IEnhancerLocalizer _localizer;

        public EnhancerController(IEnumerable<EnhancerDescriptor> descriptors, IEnhancerLocalizer localizer)
        {
            _descriptors = descriptors;
            _localizer = localizer;
        }

        [HttpGet("descriptor")]
        [SwaggerOperation(OperationId = "GetAllEnhancerDescriptors")]
        public ListResponse<EnhancerDescriptor> GetAllDescriptors()
        {
            return new ListResponse<EnhancerDescriptor>(_descriptors);
        }
    }
}