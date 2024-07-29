using Bakabase.Abstractions.Components.Enhancer;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Extensions;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Abstractions
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

        protected abstract Task<TContext?> BuildContext(Resource resource);
        public int Id => (int) TypedId;
        protected abstract EnhancerId TypedId { get; }

        public async Task<List<EnhancementRawValue>?> CreateEnhancements(Resource resource)
        {
            var context = await BuildContext(resource);
            if (context == null)
            {
                return null;
            }

            Logger.LogInformation($"Got context: {context.ToJson()}");

            var targetValues = await ConvertContextByTargets(context);
            if (targetValues?.Any(x => x.ValueBuilder != null) != true)
            {
                return null;
            }

            var enhancements = new List<EnhancementRawValue>();
            foreach (var tv in targetValues)
            {
                var value = tv.ValueBuilder?.Value;
                if (value != null)
                {
                    var targetAttr = tv.Target.GetAttribute<EnhancerTargetAttribute>();
                    var intTarget = (int) (object) tv.Target;
                    var vt = targetAttr.ValueType;
                    var e = new EnhancementRawValue
                    {
                        Target = intTarget,
                        DynamicTarget = tv.DynamicTarget,
                        Value = value,
                        ValueType = vt
                    };

                    enhancements.Add(e);
                }
            }

            return enhancements;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <returns>
        /// The value of the dictionary MUST be the standard value, which can be generated safely via <see cref="IStandardValueBuilder{TValue}"/>
        /// </returns>
        protected abstract Task<List<EnhancementTargetValue<TEnumTarget>>> ConvertContextByTargets(TContext context);
    }
}