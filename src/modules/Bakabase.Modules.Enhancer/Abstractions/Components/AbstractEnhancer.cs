using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Abstractions.Components.Tracing;
using Bakabase.Abstractions.Extensions;
using Bakabase.Abstractions.Models.Domain;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Enhancer.Abstractions.Attributes;
using Bakabase.Modules.Enhancer.Abstractions.Models.Domain;
using Bakabase.Modules.Enhancer.Components;
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
            EnhancementLogCollector logCollector, CancellationToken ct) =>
            await BuildContextInternal(resource, (TEnhancerOptions)(object)options, logCollector, ct);

        protected abstract Task<TContext?> BuildContextInternal(Resource resource, TEnhancerOptions options,
            EnhancementLogCollector logCollector, CancellationToken ct);

        public int Id => (int) TypedId;
        protected abstract EnhancerId TypedId { get; }

        public async Task<EnhancementResult?> CreateEnhancements(Resource resource, EnhancerFullOptions options,
            EnhancementLogCollector logCollector, CancellationToken ct)
        {
            logCollector.LogInfo(EnhancementLogEvent.Started,
                $"Starting enhancement for resource [{resource.Id}:{resource.Path}]",
                new { ResourceId = resource.Id, ResourcePath = resource.Path });

            logCollector.LogInfo(EnhancementLogEvent.Configuration,
                "Using enhancement options",
                new
                {
                    EnhancerId = options.EnhancerId,
                    KeywordProperty = options.KeywordProperty,
                    PretreatKeyword = options.PretreatKeyword,
                    TargetOptionsCount = options.TargetOptions?.Count ?? 0,
                    ExpressionsCount = options.Expressions?.Count ?? 0
                });

            Logger.LogInformation("Building context for resource [{ResourceId}:{ResourcePath}]", resource.Id, resource.Path);

            TContext? context;
            try
            {
                context = await BuildContextInternal(resource, (TEnhancerOptions)(object)options, logCollector, ct);
                if (context == null)
                {
                    logCollector.LogWarning(EnhancementLogEvent.ContextBuilt,
                        "No context built - no data found or empty result");
                    return new EnhancementResult { Logs = logCollector.GetLogs() };
                }
            }
            catch (Exception ex)
            {
                var errorMessage =
                    $"Failed to build context for enhancer {GetType().Name}. This is usually caused by unreachable external data sources or sites, or by unsupported data returned from those sources.";
                logCollector.LogError(EnhancementLogEvent.Error, errorMessage,
                    new { ExceptionType = ex.GetType().Name, ex.Message, ex.StackTrace });

                return new EnhancementResult
                {
                    Logs = logCollector.GetLogs(),
                    ErrorMessage = $"{errorMessage} Details: {ex.Message}"
                };
            }

            logCollector.LogInfo(EnhancementLogEvent.ContextBuilt, "Context built successfully");
            Logger.LogInformation($"Got context: {context.ToJson()}");

            var targetValues = await ConvertContextByTargets(context, logCollector, ct);
            if (targetValues?.Any(x => x.ValueBuilder != null) != true)
            {
                logCollector.LogWarning(EnhancementLogEvent.TargetConverted,
                    "No target values converted");
                return new EnhancementResult { Logs = logCollector.GetLogs() };
            }

            var enhancements = new List<EnhancementRawValue>();
            foreach (var tv in targetValues)
            {
                var value = tv.ValueBuilder?.Value;
                if (value != null)
                {
                    var targetAttr = tv.Target.GetAttribute<EnhancerTargetAttribute>();
                    var intTarget = (int)(object)tv.Target;
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

            logCollector.LogInfo(EnhancementLogEvent.Completed,
                $"Enhancement completed with {enhancements.Count} values",
                new { ValueCount = enhancements.Count });

            return new EnhancementResult
            {
                Values = enhancements,
                Logs = logCollector.GetLogs()
            };
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
        /// Converts the context to target values.
        /// </summary>
        /// <param name="context">The context built from external data.</param>
        /// <param name="logCollector">The log collector for recording conversion steps.</param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// The value of the dictionary MUST be the standard value, which can be generated safely via <see cref="IStandardValueBuilder{TValue}"/>
        /// </returns>
        protected abstract Task<List<EnhancementTargetValue<TEnumTarget>>> ConvertContextByTargets(TContext context,
            EnhancementLogCollector logCollector, CancellationToken ct);
    }
}