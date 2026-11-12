using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.AI.Models.Domain;
using Bakabase.Modules.AI.Services;
using Bakabase.Modules.PostParser.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using CsQuery;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Only the current acquisition and revision can supply publisher evidence.
/// The model ranks numbered literals. Values, URLs, and raw markup stay local.</summary>
public sealed class AcquisitionArchivePasswordEvidence(BakabaseDbContext db, ILlmService llm)
{
    internal sealed record Literal(string Value, bool Labelled, int LinesAfterShare, bool AfterShareFooter);
    internal const int MaximumCandidates = 3;
    private const int MaximumBodyChars = 131072;

    internal static Func<CancellationToken, Task<IReadOnlyList<string>>>? Bind(WorkflowExecutionContext context,
        string partId, string? selectedUrl)
    {
        if (context.WorkflowDefinitionId != 0 || string.IsNullOrWhiteSpace(selectedUrl)) return null;
        var service = context.Services.GetService<AcquisitionArchivePasswordEvidence>();
        if (service == null) return null;
        Task<IReadOnlyList<string>>? read = null;
        return ct => read ??= service.ReadAsync(context.RunId, partId, selectedUrl, ct);
    }

    private async Task<IReadOnlyList<string>> ReadAsync(long runId, string partId, string selectedUrl, CancellationToken ct)
    {
        var task = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId == runId && t.DeletedAt == null)
            .Select(t => new {t.PlanJson, t.SourceRefsJson}).SingleOrDefaultAsync(ct);
        if (task?.PlanJson == null || task.SourceRefsJson == null) return [];
        var plan = JsonSerializer.Deserialize<AcquisitionPlan>(task.PlanJson, WorkflowJson.Options);
        var part = plan?.Parts.SingleOrDefault(p => p.Id == partId);
        var source = part?.Sources.SingleOrDefault(s => s.Id == part.SelectedSourceId && s.Url == selectedUrl);
        if (source == null) return [];
        var origins = (JsonSerializer.Deserialize<List<WorkflowSourceRef>>(task.SourceRefsJson, WorkflowJson.Options) ?? [])
            .Where(r => r.Module == "postParser" && r.EntityKind == "task").Select(r => (r.EntityId, r.Revision)).Distinct().ToArray();
        if (origins.Length != 1 || !int.TryParse(origins[0].EntityId, out var id) || !int.TryParse(origins[0].Revision, out var revision)) return [];
        var snapshot = await db.PostParserTasks.AsNoTracking().Where(p => p.Id == id && p.Revision == revision && !p.IsDeleted &&
                p.ContentSnapshotJson != null && p.ContentSnapshotJson.Length <= 1048576)
            .Select(p => p.ContentSnapshotJson).SingleOrDefaultAsync(ct);
        if (snapshot == null) return [];
        var content = JsonSerializer.Deserialize<PostContent>(snapshot, WorkflowJson.Options);
        if (content?.MainHtml is not {Length: > 0 and <= MaximumBodyChars}) return [];
        var excluded = plan!.Parts.SelectMany(p => p.Sources).Select(s => s.AccessCode).OfType<string>().ToHashSet(StringComparer.Ordinal);
        return await SelectAsync(Inspect(content.MainHtml, [source.Url, source.OriginalUrl ?? source.Url], excluded, plan.Parts.Count == 1), llm, ct);
    }

    private static readonly Regex Address = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Explicit = new(@"^(?:解[压壓](?:[缩縮])?(?:密[码碼]|[码碼])|(?:archive|extraction|unzip) password|解凍パスワード)\s*[:：=]\s*(\S{1,128})$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AccessCode = new(@"(?:提取[码碼]|访问[码碼]|訪問[码碼]|access code|[?&]pwd=)\s*[:：=]?\s*([a-zA-Z0-9_-]{1,64})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Trailer = new(@"^[a-zA-Z0-9_.!@#-]{5,64}$", RegexOptions.CultureInvariant);

    internal static IReadOnlyList<Literal> Inspect(string html, IReadOnlyList<string> sourceUrls, IReadOnlySet<string> excluded, bool singlePart)
    {
        if (html.Length > MaximumBodyChars) return [];
        var lines = VisibleText(html).Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var expected = sourceUrls.Where(url => Uri.TryCreate(url, UriKind.Absolute, out _)).Select(url => new Uri(url)).ToArray();
        var candidates = new List<Literal>();
        var codes = new HashSet<string>(excluded, StringComparer.Ordinal);
        foreach (var line in lines)
            foreach (Match match in AccessCode.Matches(line)) codes.Add(match.Groups[1].Value);
        var unlinked = singlePart && !lines.Any(l => Address.IsMatch(l));
        var lastShare = -100;
        var footer = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var links = Address.Matches(line);
            if (links.Count != 0)
            {
                // Anchor the surrounding lines to the selected source only.
                lastShare = links.All(m => Uri.TryCreate(m.Value.TrimEnd('。', '，'), UriKind.Absolute, out var uri) &&
                    expected.Any(e => MatchesShare(e, uri))) ? i : -100;
                footer = false;
                continue;
            }
            var distance = i - lastShare;
            if (distance is > 0 and <= 3 && line.StartsWith("--", StringComparison.Ordinal) && line.Contains("分享")) footer = true;
            var labelled = Explicit.Match(line);
            string? candidate = null;
            if (labelled.Success && (unlinked || distance is > 0 and <= 3)) candidate = labelled.Groups[1].Value;
            else if (distance is > 0 and <= 3 && Trailer.IsMatch(line) && line.Any(char.IsAsciiLetter) && line.Any(char.IsAsciiDigit) &&
                     !Regex.IsMatch(line, @"^(?:RJ|BJ|VJ)\d+$", RegexOptions.IgnoreCase)) candidate = line;
            if (candidate != null && !codes.Contains(candidate) && !candidate.Any(char.IsControl))
                candidates.Add(new(candidate, labelled.Success, distance, footer));
        }
        var unique = candidates.GroupBy(c => c.Value, StringComparer.Ordinal).Select(g => g.OrderByDescending(c => c.Labelled).First()).ToArray();
        return unique.Length <= MaximumCandidates ? unique : [];
    }

    private static bool MatchesShare(Uri expected, Uri observed)
    {
        if (expected.Scheme != observed.Scheme || expected.Authority != observed.Authority || expected.AbsolutePath != observed.AbsolutePath ||
            expected.Fragment != observed.Fragment) return false;
        static string WithoutCode(Uri uri) => string.Join("&", uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(v => !v.StartsWith("pwd=", StringComparison.OrdinalIgnoreCase)));
        return WithoutCode(expected) == WithoutCode(observed);
    }

    private static string VisibleText(string html)
    {
        var text = new StringBuilder();
        var nodes = new Stack<(IDomObject Node, bool End)>(CQ.CreateFragment(html).Reverse().Select(n => (n, false)));
        while (nodes.TryPop(out var frame))
        {
            if (frame.End) {text.Append('\n'); continue;}
            var node = frame.Node;
            if (node.NodeType == NodeType.TEXT_NODE) {text.Append(node.NodeValue); continue;}
            if (node.NodeType != NodeType.ELEMENT_NODE) continue;
            var tag = node.NodeName.ToLowerInvariant();
            if (tag is "script" or "style" or "noscript" or "template" or "iframe" || node.HasAttribute("hidden") ||
                node.GetAttribute("aria-hidden") == "true" || Regex.IsMatch(node.GetAttribute("style") ?? "", @"(?:display\s*:\s*none|visibility\s*:\s*hidden)", RegexOptions.IgnoreCase)) continue;
            if (tag is "br" or "p" or "div" or "blockquote" or "li" or "tr")
            {
                text.Append('\n');
                if (tag != "br") nodes.Push((node, true));
            }
            if (tag == "a" && Uri.TryCreate(node.GetAttribute("href"), UriKind.Absolute, out var href) && href.Scheme is "http" or "https")
                text.Append(' ').Append(href.AbsoluteUri).Append(' ');
            foreach (var child in node.ChildNodes.Reverse()) nodes.Push((child, false));
        }
        return text.ToString();
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
