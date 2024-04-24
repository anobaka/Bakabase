using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Aliyun.Api.LogService.Infrastructure.Serialization.Protobuf;
using Bakabase.InsideWorld.Business.Components.Enhancement.Abstractions.Models.Domain;
using Bakabase.InsideWorld.Business.Components.StandardValue;
using Bakabase.InsideWorld.Business.Components.StandardValue.Abstractions;
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
    /// <typeparam name="TContext"></typeparam>
    /// <typeparam name="TEnhancerOptions">You can pass <see cref="object?"/> if there isn't any options.</typeparam>
    public abstract class AbstractEnhancer<TEnumTarget, TContext, TEnhancerOptions> : IEnhancer
        where TEnumTarget : Enum where TEnhancerOptions : class? where TContext : class?
    {
        protected readonly IEnumerable<IStandardValueHandler> ValueConverters;
        protected readonly ILogger Logger;

        protected AbstractEnhancer(IEnumerable<IStandardValueHandler> valueConverters,
            ILoggerFactory loggerFactory)
        {
            ValueConverters = valueConverters;
            Logger = loggerFactory.CreateLogger(GetType());
        }

        protected abstract Task<TContext> BuildContext(Bakabase.Abstractions.Models.Domain.Resource resource);

        public abstract EnhancerId Id { get; }

        public async Task<List<EnhancementRawValue>?> CreateEnhancements(Bakabase.Abstractions.Models.Domain.Resource resource)
        {
            var context = await BuildContext(resource);
            if (context == null)
            {
                return null;
            }

            Logger.LogInformation($"Got context: {resource.ToJson()}");

            var targetValues = await ConvertContextByTargets(context);
            if (targetValues == null)
            {
                return null;
            }

            var enhancements = new List<EnhancementRawValue>();
            foreach (var (target, value) in targetValues)
            {
                if (value != null)
                {
                    var targetAttr = target.GetAttribute<EnhancerTargetAttribute>();
                    var intTarget = (int) (object) target;
                    var vt = targetAttr.ValueType;
                    var vc = ValueConverters.FirstOrDefault(x => x.Type == vt)!;
                    var isValid = vc.ValidateType(value);
                    var e = new EnhancementRawValue
                    {
                        Target = intTarget,
                        Value = value,
                        ValueType = vt,
                    };

                    enhancements.Add(e);
                }
            }

            return enhancements;
        }

        protected abstract Task<Dictionary<TEnumTarget, object?>?> ConvertContextByTargets(TContext context);
    }
}