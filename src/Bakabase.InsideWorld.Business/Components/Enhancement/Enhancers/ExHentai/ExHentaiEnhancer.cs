using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions;
using Bakabase.InsideWorld.Business.Components.StandardValue;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions.Models;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Enhancers.ExHentai
{
    public class ExHentaiEnhancer : AbstractEnhancer<ExHentaiEnhancerTarget, ExHentaiRawData, object?>
    {
        public ExHentaiEnhancer(IEnhancerDescriptor descriptor, IEnumerable<IStandardValueHandler> valueConverters,
            ILoggerFactory loggerFactory) : base(descriptor, valueConverters, loggerFactory)
        {
        }

        protected override async Task<Dictionary<ExHentaiEnhancerTarget, object?>?> ConvertRawDataByTargets(
            ExHentaiRawData rawData)
        {
            var targetValues = new Dictionary<ExHentaiEnhancerTarget, object?>();
            foreach (var target in SpecificEnumUtils<ExHentaiEnhancerTarget>.Values)
            {
                var v = BuildTargetValue(rawData, target);
                if (v != null)
                {
                    targetValues.Add(target, v);
                }
            }

            return targetValues;
        }

        protected override async Task<ExHentaiRawData> GetRawData(Bakabase.Abstractions.Models.Domain.Resource resource)
        {
            throw new NotImplementedException();
        }

        protected object? BuildTargetValue(ExHentaiRawData data, ExHentaiEnhancerTarget target)
        {
            switch (target)
            {
                case ExHentaiEnhancerTarget.Rating:
                {
                    if (data.Rating.HasValue)
                    {
                        return StandardValueCreator.CreateRatingValue(data.Rating.Value, 5);
                    }

                    break;
                }
                case ExHentaiEnhancerTarget.Tags:
                {
                    var value = data.Tags?.Select(t => new MultilevelValue
                            {Value = t.Key, Children = t.Value.Select(x => new MultilevelValue {Value = x}).ToList()})
                        .ToArray();
                    if (value?.Length > 0)
                    {
                        return StandardValueCreator.CreateMultilevelValue(value);
                    }

                    break;
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, null);
            }

            return null;
        }
    }
}