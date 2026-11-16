using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Only the current acquisition and revision can supply publisher evidence.
/// The model ranks numbered literals. Values, URLs, and raw markup stay local.</summary>
public sealed class AcquisitionArchivePasswordEvidence(BakabaseDbContext db, ILlmService llm,
    IEnumerable<IProcessingPlanSourceReader>? sourceReaders = null)
{
    internal sealed record Literal(string Value, bool Labelled, int LinesAfterShare, bool AfterShareFooter);
    internal const int MaximumCandidates = 3;
    private const int MaximumBodyChars = 131072;

    internal static Func<string, CancellationToken, Task<IReadOnlyList<string>>>? Bind(WorkflowExecutionContext context,
        string partId, string? selectedUrl)
    {
        if (context.WorkflowDefinitionId != 0 || string.IsNullOrWhiteSpace(selectedUrl)) return null;
        var service = context.Services.GetService<AcquisitionArchivePasswordEvidence>();
        return service?.BindSource(context.RunId, partId, selectedUrl);
    }

    private Func<string, CancellationToken, Task<IReadOnlyList<string>>> BindSource(long runId, string partId, string selectedUrl)
    {
        Task<AcquisitionCachedSourceEvidence.Evidence?>? read = null;
        Task<IReadOnlyList<string>>? ranked = null;
        return async (archive, ct) =>
        {
            read ??= AcquisitionCachedSourceEvidence.ReadAsync(db, sourceReaders, runId, partId, selectedUrl, ct);
            var source = await read;
            if (source == null) return [];
            if (source.PasswordReference != null && source.Passwords.All(password => !password.Labelled))
                return PublisherPasswordReference.Resolve(source.PasswordReference, archive, source.PartTitle,
                    source.ExcludedCodes!);
            return await (ranked ??= SelectAsync(source.Passwords, llm, ct));
        };
    }

    internal static IReadOnlyList<Literal> Inspect(string html, IReadOnlyList<string> sourceUrls, IReadOnlySet<string> excluded, bool singlePart)
    {
        if (html.Length > MaximumBodyChars) return [];
        var values = AcquisitionCachedSourceEvidence.InspectText(ProcessingPlanSourceText.Visible(html), sourceUrls, excluded, singlePart);
        return values.Count <= MaximumCandidates ? values : [];
    }

    internal static async Task<IReadOnlyList<string>> SelectAsync(IReadOnlyList<Literal> literals, ILlmService llm, CancellationToken ct)
    {
        if (literals.Count is 0 or > MaximumCandidates) return [];
        var labelled = literals.Where(v => v.Labelled).ToArray();
        if (labelled.Length > 0) return labelled.Select(v => v.Value).ToArray();
        var evidence = literals.Select((v, i) => new {id = i + 1, v.LinesAfterShare, v.AfterShareFooter,
            length = v.Value.Length, isolatedLine = true, containsLettersAndDigits = true});
        const string prompt = "Choose at most one likely archive password from numbered literal lines in a saved publisher post. " +
            "They appear after the selected file-sharing URL. Link access codes were excluded. A separate alphanumeric line after a share footer " +
            "may be the publisher's unlabeled archive password. Reply with JSON {\"id\":number}; choose 0 when ambiguous. Do not generate passwords or instructions.";
        var response = await llm.CompleteForFeatureAsync(AiFeature.FileProcessor,
            [new(ChatRole.System, prompt), new(ChatRole.User, JsonSerializer.Serialize(evidence))],
            new() {UseJsonResponseFormat = true, Temperature = 0, JsonResponseSchema = JsonSerializer.Deserialize<JsonElement>(
                """{"type":"object","properties":{"id":{"type":"integer","minimum":0,"maximum":3}},"required":["id"],"additionalProperties":false}""")}, ct);
        if (response.FinishReason == ChatFinishReason.Length || response.Text is not {Length: > 0 and <= 512}) return [];
        using var answer = JsonDocument.Parse(response.Text);
        return answer.RootElement.ValueKind == JsonValueKind.Object && answer.RootElement.EnumerateObject().Count() == 1 &&
               answer.RootElement.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.Number && id.TryGetInt32(out var number) &&
               number >= 1 && number <= literals.Count ? [literals[number-1].Value] : [];
    }
}
