using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aliyun.Api.LogService.Infrastructure.Serialization.Protobuf;
using Bakabase.InsideWorld.Business.Components.StandardValue;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions.Models;
using Bakabase.InsideWorld.Models.Constants;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions
{
    /// <summary>
    /// 
    /// </summary>
    /// <typeparam name="TEnumTarget"></typeparam>
    /// <typeparam name="TRawData"></typeparam>
    /// <typeparam name="TEnhancerOptions">You can pass <see cref="object?"/> if there isn't any options.</typeparam>
    public abstract class AbstractEnhancer<TEnumTarget, TRawData, TEnhancerOptions> : IEnhancer
        where TEnumTarget : Enum where TEnhancerOptions : class? where TRawData : class?
    {
        protected readonly IEnhancerDescriptor Descriptor;
        protected readonly IEnumerable<IStandardValueHandler> ValueConverters;
        protected readonly ILogger Logger;

        protected AbstractEnhancer(IEnhancerDescriptor descriptor, IEnumerable<IStandardValueHandler> valueConverters,
            ILoggerFactory loggerFactory)
        {
            Descriptor = descriptor;
            ValueConverters = valueConverters;
            Logger = loggerFactory.CreateLogger(GetType());
        }

        protected abstract Task<TRawData> GetRawData(Bakabase.Abstractions.Models.Domain.Resource resource);

        public async Task<List<Enhancement>?> Enhance(Bakabase.Abstractions.Models.Domain.Resource resource,
            object? options)
        {
            var rawData = await GetRawData(resource);
            if (rawData == null)
            {
                return null;
            }

            Logger.LogInformation($"Got raw data: {resource.ToJson()}");

            var targetValues = await ConvertRawDataByTargets(rawData);
            if (targetValues == null)
            {
                return null;
            }

            var enhancements = new List<Enhancement>();
            foreach (var (target, value) in targetValues)
            {
                if (value != null)
                {
                    var intTarget = (int) (object) target;
                    var vt = Descriptor.TargetValueTypeMap[intTarget];
                    var vc = ValueConverters.FirstOrDefault(x => x.Type == vt)!;
                    var isValid = vc.ValidateType(value);
                    var e = new Enhancement
                    {
                        Target = intTarget,
                        Value = ApplyOptions(target, value, options as TEnhancerOptions),
                        ValueType = vt,
                    };

                    enhancements.Add(e);
                }
            }

            return enhancements;
        }

        protected abstract Task<Dictionary<TEnumTarget, object?>?> ConvertRawDataByTargets(TRawData rawData);

        protected virtual object? ApplyOptions(TEnumTarget target, object? initValue, TEnhancerOptions? options) =>
            initValue;
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
}