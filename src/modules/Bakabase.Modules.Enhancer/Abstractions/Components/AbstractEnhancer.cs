using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tracing;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Models.Domain.Constants;
using Bakabase.Modules.Property.Abstractions.Components;
using Bakabase.Modules.StandardValue.Abstractions.Components;
using Bootstrap.Components.Cryptography;
using Bootstrap.Extensions;
using Humanizer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.Enhancer.Abstractions.Components
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
        protected readonly ILogger Logger;
        private readonly IFileManager _fileManager;
        protected readonly IEnhancerLocalizer Localizer;
        protected readonly IPropertyLocalizer PropertyLocalizer;
        protected readonly IServiceProvider ServiceProvider;
        protected BakaTracingContext? TracingContext => ScopedBakaTracingContextAccessor.Current.Value;

        protected AbstractEnhancer(ILoggerFactory loggerFactory, IFileManager fileManager, IServiceProvider serviceProvider)
        {
            _fileManager = fileManager;
            ServiceProvider = serviceProvider;
            Logger = loggerFactory.CreateLogger(GetType());
            Localizer = serviceProvider.GetRequiredService<IEnhancerLocalizer>();
            PropertyLocalizer = serviceProvider.GetRequiredService<IPropertyLocalizer>();
        }

        public async Task<object?> BuildContext(Resource resource, EnhancerFullOptions options,
            CancellationToken ct) => await BuildContextInternal(resource, options, ct);
        
        protected abstract Task<TContext?> BuildContextInternal(Resource resource, EnhancerFullOptions options,
            CancellationToken ct);

        public int Id => (int) TypedId;
        protected abstract EnhancerId TypedId { get; }

        public async Task<List<EnhancementRawValue>?> CreateEnhancements(Resource resource, EnhancerFullOptions options,
            CancellationToken ct)
        {
            Logger.LogInformation("Building context for resource [{ResourceId}:{ResourcePath}]", resource.Id, resource.Path);

            TContext? context;
            try
            {
                context = await BuildContextInternal(resource, options, ct);
                if (context == null)
                {
                    return null;
                }
            }
            catch (Exception ex)
            {
                throw new Exception(
                    $"Failed to build context for enhancer {GetType().Name}. This is usually caused by unreachable external data sources or sites, or by unsupported data returned from those sources.",
                    ex
                );

            }

            Logger.LogInformation($"Got context: {context.ToJson()}");

            var targetValues = await ConvertContextByTargets(context, ct);
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
        /// Save files for enhancer
        /// </summary>
        /// <param name="fileName">{EnhancerId}/ will always be added to prefix.</param>
        /// <param name="data"></param>
        /// <returns></returns>
        protected async Task<string> SaveFile(string fileName, byte[] data)
        {
            var path = BuildFilePath(fileName);
            return await _fileManager.Save(path, data, new CancellationToken());
        }

        /// <summary>
        /// Save files for enhancer
        /// </summary>
        /// <param name="resource"></param>
        /// <param name="fileName">{EnhancerId}/{resourceId}/ will always be added to prefix.</param>
        /// <param name="data"></param>
        /// <returns></returns>
        protected async Task<string> SaveFile(Resource resource, string fileName, byte[] data)
        {
            var path = BuildFilePath(resource, fileName);
            return await _fileManager.Save(path, data, new CancellationToken());
        }

        protected string BuildFilePath(string fileName) =>
            _fileManager.BuildAbsolutePath(nameof(Enhancer).Camelize(), Id.ToString(), fileName);

        protected string BuildFilePath(Resource resource, string fileName)
        {
            var idPart = resource.Id > 0
                ? resource.Id.ToString()
                : $"tmp-{CryptographyUtils.Md5(resource.Path).Substring(0, 6)}";
            return _fileManager.BuildAbsolutePath(nameof(Enhancer).Camelize(), Id.ToString(), idPart, fileName);
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="context"></param>
        /// <param name="ct"></param>
        /// <returns>
        /// The value of the dictionary MUST be the standard value, which can be generated safely via <see cref="IStandardValueBuilder{TValue}"/>
        /// </returns>
        protected abstract Task<List<EnhancementTargetValue<TEnumTarget>>> ConvertContextByTargets(TContext context,
            CancellationToken ct);
    }
}