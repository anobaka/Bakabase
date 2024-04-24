using Bakabase.InsideWorld.Models.Constants;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.CustomProperty;
using Bakabase.InsideWorld.Business.Extensions;
using Bakabase.Modules.CustomProperty.Extensions;
using Bootstrap.Extensions;
using Bakabase.Modules.CustomProperty.Properties.Text;
using Bakabase.Abstractions.Components.StandardValue;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Services
{
    public class ConversionService
    {
        private readonly SpecialTextService _specialTextService;
        private readonly Dictionary<StandardValueType, IStandardValueHandler> _valueConverters;

        public ConversionService(SpecialTextService specialTextService, IEnumerable<IStandardValueHandler> valueConverters)
        {
            _specialTextService = specialTextService;
            _valueConverters = valueConverters.ToDictionary(d => d.Type, d => d);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="data"></param>
        /// <param name="fromType"></param>
        /// <param name="toType"></param>
        /// <returns>Loss information</returns>
        /// <exception cref="ArgumentOutOfRangeException"></exception>
        public async Task<(object? NewValue, StandardValueConversionLoss? Loss)> CheckConversionLoss(object? data, StandardValueType fromType,
            StandardValueType toType)
        {
            var converter = _valueConverters[fromType];
            return await converter.Convert(data, toType);
        }
    }
}