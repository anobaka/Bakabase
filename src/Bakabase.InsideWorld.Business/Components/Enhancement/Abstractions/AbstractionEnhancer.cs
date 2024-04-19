using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business.Components.Conversion.Value.Abstractions;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    public abstract class AbstractionEnhancer<TEnumTarget> : IEnhancer where TEnumTarget : Enum
    {
        protected readonly IEnhancerDescriptor Descriptor;
        protected readonly IEnumerable<IValueConverter> ValueConverters;

        protected AbstractionEnhancer(IEnhancerDescriptor descriptor, IEnumerable<IValueConverter> valueConverters)
        {
            Descriptor = descriptor;
            this.ValueConverters = valueConverters;
        }

        public async Task<List<Enhancement>> Enhance()
        {
            var targetValues = await EnhanceTargets();
            var enhancements = new List<Enhancement>();
            foreach (var (target, value) in targetValues)
            {
                if (value != null)
                {
                    var intTarget = (int) (object) target;
                    var vt = Descriptor.TargetValueTypeMap[intTarget];
                    var vc = ValueConverters.FirstOrDefault(x => x.Type == vt)!;
                    var (isValid, requiredType) = vc.ValidateType(value);
                    var e = new Enhancement
                    {
                        Target = intTarget,
                        Value = value,
                        ValueType = vt
                    };
                    ;
                    if (!isValid)
                    {
                        e.Error =
                            $"Type mismatch, this is likely an error caused by the developer. Required type is {requiredType.FullName}, actual type is {value.GetType().FullName}";
                    }

                    enhancements.Add(e);
                }
            }

            return enhancements;
        }

        protected abstract Task<Dictionary<TEnumTarget, object?>> EnhanceTargets();
    }

    public enum ExHentaiEnhancerTarget
    {
        Rating = 1,
        Tags = 2,
    }

    public interface IEnhancerDescriptor
    {
        public Dictionary<int, StandardValueType> TargetValueTypeMap { get; set; }
    }

    public class ExHentaiEnhancer : AbstractionEnhancer<ExHentaiEnhancerTarget>
    {
        public ExHentaiEnhancer(IEnhancerDescriptor descriptor, IEnumerable<IValueConverter> valueConverters) : base(
            descriptor, valueConverters)
        {
        }

        protected override async Task<Dictionary<ExHentaiEnhancerTarget, object?>> EnhanceTargets()
        {
            var rawData = await GetRawData();
            var targetValues = new Dictionary<ExHentaiEnhancerTarget, object?>();
            foreach (var target in SpecificEnumUtils<ExHentaiEnhancerTarget>.Values)
            {
                targetValues.Add(target, BuildTargetValue(rawData, target));
            }

            return targetValues;
        }

        protected object? BuildTargetValue(ExHentaiRawData data, ExHentaiEnhancerTarget target)
        {
            switch (target)
            {
                case ExHentaiEnhancerTarget.Rating:
                    break;
                case ExHentaiEnhancerTarget.Tags:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(target), target, null);
            }
        }

        protected Task<ExHentaiRawData> GetRawData()
        {

        }
    }

    public class ExHentaiRawData
    {
        public decimal Rating { get; set; }
        public Dictionary<string, List<string>>? Tags { get; set; }
    }
}