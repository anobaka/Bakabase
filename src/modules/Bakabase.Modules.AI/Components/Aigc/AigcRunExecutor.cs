using System.Globalization;
using System.Text.Json;
using Bakabase.Abstractions.Components.FileSystem;
using Bakabase.Modules.AI.Models.Db;
using Bakabase.Modules.AI.Models.Domain;
using Bootstrap.Components.Orm.Infrastructures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Bakabase.Modules.AI.Components.Aigc;

public class AigcRunExecutor<TDbContext>(
    ResourceService<TDbContext, AigcGenerationRunDbModel, int> runOrm,
    ResourceService<TDbContext, AigcGeneratorDbModel, int> generatorOrm,
    ResourceService<TDbContext, AigcProviderConfigDbModel, int> providerOrm,
    AigcArtifactPipeline<TDbContext> pipeline,
    IFileManager fileManager,
    IEnumerable<IAigcProviderInvoker> invokers,
    ILogger<AigcRunExecutor<TDbContext>> logger
) : IAigcRunExecutor where TDbContext : DbContext
{
    private readonly Dictionary<AigcProviderKind, IAigcProviderInvoker> _invokerMap =
        invokers.ToDictionary(i => i.Kind);

    public async Task ExecuteAsync(int runId, Func<int, string?, CancellationToken, Task>? onProgress,
        CancellationToken ct)
    {
        var run = await runOrm.GetByKey(runId)
                  ?? throw new InvalidOperationException($"Aigc run {runId} not found");
        var generator = await generatorOrm.GetByKey(run.GeneratorId)
                        ?? throw new InvalidOperationException($"Aigc generator {run.GeneratorId} not found");
        var provider = await providerOrm.GetByKey(generator.ProviderId)
                       ?? throw new InvalidOperationException($"Aigc provider {generator.ProviderId} not found");
        if (!_invokerMap.TryGetValue(provider.Kind, out var invoker))
            throw new InvalidOperationException($"No invoker registered for {provider.Kind}");

        run.Status = AigcGenerationStatus.Running;
        await runOrm.Update(run);

        try
        {
            // Build invocation request
            var parameters = ParseParameters(generator.ParametersJson);
            var request = new AigcInvocationRequest
            {
                Prompt = run.Prompt,
                NegativePrompt = run.NegativePrompt,
                MediaType = generator.MediaType,
                Parameters = parameters,
                OnProgress = onProgress
            };

            if (onProgress is not null) await onProgress(5, "Calling provider", ct);

            var result = await invoker.InvokeAsync(provider, request, ct);

            run.RequestPayload = result.RawRequestPayload;
            run.ResponsePayload = result.RawResponsePayload;

            if (result.Outputs.Count == 0)
            {
                throw new InvalidOperationException("Provider returned no outputs.");
            }

            // Land outputs to disk
            if (onProgress is not null) await onProgress(70, $"Saving {result.Outputs.Count} files", ct);

            var runDir = fileManager.GetAigcRunDir(generator.Id, run.Id);
            Directory.CreateDirectory(runDir);

            var landed = new List<AigcLandedFile>();
            for (var i = 0; i < result.Outputs.Count; i++)
            {
                var output = result.Outputs[i];
                var fileName = BuildFileName(generator.FilenameTemplate, run.Id, i, output.SuggestedExtension);
                var absPath = Path.Combine(runDir, fileName);
                await File.WriteAllBytesAsync(absPath, output.Content, ct);
                landed.Add(new AigcLandedFile
                {
                    Ordinal = i,
                    AbsolutePath = absPath,
                    RelativePath = fileManager.GetAigcArtifactRelativePath(generator.Id, run.Id, fileName)
                });
            }

            if (onProgress is not null) await onProgress(85, "Registering artifacts", ct);
            await pipeline.RegisterAsync(generator, run, landed, ct);

            run.Status = AigcGenerationStatus.Succeeded;
            run.CompletedAt = DateTime.Now;
            await runOrm.Update(run);

            if (onProgress is not null) await onProgress(100, "Done", ct);
        }
        catch (OperationCanceledException)
        {
            run.Status = AigcGenerationStatus.Failed;
            run.ErrorMessage = "Cancelled";
            run.CompletedAt = DateTime.Now;
            await runOrm.Update(run);
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Aigc run {RunId} failed", run.Id);
            run.Status = AigcGenerationStatus.Failed;
            run.ErrorMessage = ex.Message;
            run.CompletedAt = DateTime.Now;
            await runOrm.Update(run);
            throw;
        }
    }

    private static Dictionary<string, object?> ParseParameters(string? json)
    {
        if (string.IsNullOrEmpty(json)) return new Dictionary<string, object?>();
        try
        {
            var dict = JsonSerializer.Deserialize<Dictionary<string, object?>>(json);
            return dict ?? new Dictionary<string, object?>();
        }
        catch
        {
            return new Dictionary<string, object?>();
        }
    }

    private static string BuildFileName(string template, int runId, int ordinal, string extension)
    {
        var ts = DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        var name = template
            .Replace("{run}", runId.ToString())
            .Replace("{ordinal}", ordinal.ToString())
            .Replace("{timestamp}", ts);
        // Sanitize filename
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '_');
        }
        return name + "." + extension;
    }
}
