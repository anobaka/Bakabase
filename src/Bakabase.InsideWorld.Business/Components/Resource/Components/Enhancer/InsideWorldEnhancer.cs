using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer.Infrastructures;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.InsideWorld.Models.Constants;
using Bakabase.InsideWorld.Models.Extensions;
using Bakabase.InsideWorld.Models.Models.Dtos;
using Bootstrap.Components.Cryptography;
using Bootstrap.Extensions;
using Bootstrap.Models;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Resource.Components.Enhancer
{
    [Enhancer(new object[]
        {
            ReservedResourceProperty.Name,
            ReservedResourceProperty.Series,
            ReservedResourceProperty.Publisher,
            ReservedResourceProperty.ReleaseDt,
            ReservedResourceProperty.Volume,
            ReservedResourceProperty.Original,
            ReservedResourceProperty.Language,
        }, null,
        Description =
            "Parse name which likes [(20)191202][Publisher1(Publisher2,Publisher3),Publisher4(Publisher5),Publisher6]Title Part 2(Original)[CN]",
        Version = "1.0.1")]
    public class InsideWorldEnhancer : IEnhancer
    {
        private readonly ResourceService _resourceService;
        private readonly SpecialTextService _specialTextService;

        public InsideWorldEnhancer(ResourceService resourceService, SpecialTextService specialTextService)
        {
            _resourceService = resourceService;
            _specialTextService = specialTextService;
        }
        public Task<string> Validate()
        {
            return Task.FromResult((string) null);
        }

        public async Task<Enhancement[]> Enhance(Business.Models.Domain.Resource resource)
        {
            
        }

        
    }
}