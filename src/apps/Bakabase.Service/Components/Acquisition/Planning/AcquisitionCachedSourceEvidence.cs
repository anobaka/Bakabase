using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.InsideWorld.Business;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain;
using Bakabase.Modules.Workflow.Abstractions.Components;
using Bakabase.Modules.Workflow.Abstractions.Models.Domain;
using Bakabase.Service.Components.Acquisition.ProcessingPlans;
using Bakabase.InsideWorld.Business.Components.Artifacts;
using Microsoft.EntityFrameworkCore;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>Selected-source, revision-bound cached evidence. Readers never refresh or purchase content.</summary>
internal static class AcquisitionCachedSourceEvidence
{
    internal const int MaximumSourceChars = 131072;
    internal const int MaximumExcerptChars = 3200;
    internal sealed record Evidence(WorkflowSourceRef Source, string Fingerprint, IReadOnlyList<string> Excerpts,
        IReadOnlyList<AcquisitionArchivePasswordEvidence.Literal> Passwords);

    internal static async Task<Evidence?> ReadAsync(BakabaseDbContext db, IEnumerable<IProcessingPlanSourceReader>? readers,
        long runId, string partId, string selectedUrl, CancellationToken ct)
    {
        try
        {
            var tasks = await db.AcquisitionTasks.AsNoTracking().Where(t => t.WorkflowRunId == runId && t.DeletedAt == null)
                .Select(t => new {t.PlanJson, t.SourceRefsJson}).Take(2).ToArrayAsync(ct);
            if (tasks.Length != 1) return null;
            var plan = JsonSerializer.Deserialize<AcquisitionPlan>(tasks[0].PlanJson ?? "null", WorkflowJson.Options);
            var parts = plan?.Parts.Where(p => p.Id == partId).ToArray();
            if (parts is not {Length: 1}) return null;
            var part = parts[0];
            var sources = part.Sources.Where(s => s.Id == part.SelectedSourceId && s.Url == selectedUrl).ToArray();
            if (sources is not {Length: 1}) return null;
            var source = sources[0];
            var refs = JsonSerializer.Deserialize<WorkflowSourceRef[]>(tasks[0].SourceRefsJson ?? "[]", WorkflowJson.Options) ?? [];
            var declared = source.ProcessingSource ?? part.ProcessingSource;
            var candidates = (declared == null ? refs.Where(r => r.ResultKey != null) : [declared])
                .Where(r => r.ResultKey != null && refs.Any(o => o.Module == r.Module && o.EntityKind == r.EntityKind &&
                    o.EntityId == r.EntityId && o.Revision == r.Revision)).Distinct().ToArray();
            if (candidates.Length != 1) return null;
            var providers = (readers ?? [new PostProcessingPlanSourceReader(db)]).Where(r => r.Supports(candidates[0])).ToArray();
            if (providers.Length != 1) return null;
            var snapshot = await providers[0].ReadAsync(candidates[0], ct);
            if (snapshot.Source != candidates[0] || snapshot.SourceLocator != selectedUrl && snapshot.SourceLocator != source.OriginalUrl)
                return null;
            var texts = snapshot.EvidenceTexts;
            if (texts.Count > 1000 || texts.Sum(t => (long)t.Length) > MaximumSourceChars) return null;
            var urls = new[] {selectedUrl, source.OriginalUrl ?? selectedUrl};
            var codes = plan!.Parts.SelectMany(p => p.Sources).Select(s => s.AccessCode).OfType<string>().ToHashSet(StringComparer.Ordinal);
            // Only the first saved document can provide unlinked main-body evidence.
            // An unrelated unlinked comment must not introduce a password candidate.
            var literals = texts.SelectMany((t, i) => InspectText(t, urls, codes, i == 0 && plan.Parts.Count == 1)).ToArray();
            var passwords = literals.GroupBy(l => l.Value, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(v => v.Labelled).First()).ToArray();
            var excerpts = Excerpts(texts, urls, codes);
            return new(snapshot.Source, ArtifactManifestFiles.Hash(snapshot.SourceFingerprint), excerpts,
                passwords.Length <= AcquisitionArchivePasswordEvidence.MaximumCandidates ? passwords : []);
        }
        catch (Exception error) when (error is JsonException or ArgumentException or InvalidOperationException)
        {
            // Optional missing/stale evidence cannot prevent processing current files.
            return null;
        }
    }

    private static readonly Regex Address = new(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex PasswordLabel = new(@"^(?:解[压壓](?:[缩縮])?(?:密[码碼]|[码碼])|密[码碼]|pw|password|(?:archive|extraction|unzip) password|解凍パスワード)\s*(?:[:：=↓↴]|如下|在下方)\s*(.*?)\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex AccessCode = new(@"(?:提取[码碼]|访问[码碼]|訪問[码碼]|access code|[?&]pwd=)\s*[:：=]?\s*([a-zA-Z0-9_-]{1,64})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Trailer = new(@"^[a-zA-Z0-9_.!@#-]{5,64}$", RegexOptions.CultureInvariant);
    private static readonly Regex IndirectPassword = new(@"(?:楼主|樓主|作者|发帖|發帖|发布|發布|上传|上傳).{0,16}(?:名字|名称|名稱|昵称|暱稱|用户名|用戶名|\bid\b)|(?:见|見|查看|在).{0,12}(?:网盘|網盤|原帖|图片|圖片)|(?:author|uploader|publisher).{0,16}(?:name|id)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex Technical = new(@"解[压壓]|密[码碼]|后缀|後綴|改名|重命名|分卷|补丁|補丁|本体|本體|原版|字幕|保留|删除|刪除|\bpw\b|password|extract|archive|rename", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static IReadOnlyList<AcquisitionArchivePasswordEvidence.Literal> InspectText(string text,
        IReadOnlyList<string> urls, IReadOnlySet<string> excluded, bool singlePart)
    {
        if (text.Length > MaximumSourceChars) return [];
        var lines = Lines(text);
        var codes = Codes(lines, excluded);
        var result = new List<AcquisitionArchivePasswordEvidence.Literal>();
        var unlinked = singlePart && !lines.Any(l => Address.IsMatch(l));
        var lastShare = -100; var pendingLabel = -100; var footer = false;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index]; var links = Address.Matches(line);
            if (links.Count > 0)
            {
                lastShare = links.All(m => MatchesShare(urls, m.Value)) ? index : -100;
                pendingLabel = -100; footer = false; continue;
            }
            var distance = index - lastShare;
            if (distance is > 0 and <= 3 && line.StartsWith("--", StringComparison.Ordinal) && line.Contains("分享")) footer = true;
            var label = PasswordLabel.Match(line);
            string? candidate = null; var labelled = false;
            if (label.Success && (unlinked || distance is > 0 and <= 6))
            {
                var value = label.Groups[1].Value;
                if (value.Length == 0) {pendingLabel = index; continue;}
                candidate = LiteralValue(value); labelled = true; pendingLabel = -100;
            }
            else if (index - pendingLabel is > 0 and <= 3 && (unlinked || distance is > 0 and <= 9))
            {
                // A visible price/purchase heading between a password label and an
                // unlocked quote is not itself a literal. Stop at another source link.
                if (Regex.IsMatch(line, @"(?:售价|售價|购买|購買|price|purchased)", RegexOptions.IgnoreCase)) continue;
                candidate = LiteralValue(line); labelled = true; pendingLabel = -100;
            }
            else if (distance is > 0 and <= 3 && Trailer.IsMatch(line) && line.Any(char.IsAsciiLetter) && line.Any(char.IsAsciiDigit) &&
                     !Regex.IsMatch(line, @"^(?:RJ|BJ|VJ)\d+$", RegexOptions.IgnoreCase)) candidate = line;
            if (candidate != null && !codes.Contains(candidate)) result.Add(new(candidate, labelled, distance, footer));
        }
        return result.GroupBy(c => c.Value, StringComparer.Ordinal).Select(g => g.OrderByDescending(c => c.Labelled).First()).ToArray();
    }

    private static string? LiteralValue(string text)
    {
        var value = text.Trim();
        if (value.Length >= 2 && (value[0], value[^1]) is ('"', '"') or ('\'', '\'') or ('「', '」') or ('“', '”'))
            value = value[1..^1];
        else if (value.Any(char.IsWhiteSpace)) return null;
        return value.Length is > 0 and <= 128 && !value.Any(char.IsControl) && !Address.IsMatch(value) &&
               !IndirectPassword.IsMatch(value) && !AccessCode.IsMatch(value) ? value : null;
    }

    private static IReadOnlyList<string> Excerpts(IReadOnlyList<string> texts, IReadOnlyList<string> urls, IReadOnlySet<string> excluded)
    {
        var excerpts = new List<string>(); var remaining = MaximumExcerptChars;
        foreach (var text in texts)
        {
            var lines = Lines(text); var codes = Codes(lines, excluded); var active = -100; var includeFollowing = 0;
            for (var index = 0; index < lines.Length && remaining > 0; index++)
            {
                var line = lines[index]; var links = Address.Matches(line);
                if (links.Count > 0) {active = links.All(m => MatchesShare(urls, m.Value)) ? index : -100; includeFollowing = 0; continue;}
                if (index - active is <= 0 or > 12) continue;
                var technical = Technical.IsMatch(line);
                if (!technical && includeFollowing-- <= 0) continue;
                if (PasswordLabel.IsMatch(line)) includeFollowing = 3;
                var clean = AccessCode.Replace(line, "[access code omitted]");
                foreach (var code in codes.Where(c => c.Length > 0))
                    if (clean == code) clean = "[access code omitted]";
                if (IndirectPassword.IsMatch(clean)) clean += " [reference only; literal value unavailable]";
                clean = clean[..Math.Min(clean.Length, Math.Min(600, remaining))];
                if (!string.IsNullOrWhiteSpace(clean) && !excerpts.Contains(clean, StringComparer.Ordinal))
                {excerpts.Add(clean); remaining -= clean.Length;}
            }
        }
        return excerpts;
    }
    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
    private static HashSet<string> Codes(IEnumerable<string> lines, IReadOnlySet<string> excluded)
    {
        var result = new HashSet<string>(excluded, StringComparer.Ordinal);
        foreach (var line in lines) foreach (Match match in AccessCode.Matches(line)) result.Add(match.Groups[1].Value);
        return result;
    }
    private static bool MatchesShare(IReadOnlyList<string> expected, string observed)
    {
        if (!Uri.TryCreate(observed.TrimEnd('。', '，'), UriKind.Absolute, out var value)) return false;
        static string Query(Uri uri) => string.Join("&", uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Where(v => !v.StartsWith("pwd=", StringComparison.OrdinalIgnoreCase)));
        return expected.Any(e => Uri.TryCreate(e, UriKind.Absolute, out var uri) && uri.Scheme == value.Scheme &&
            uri.Authority == value.Authority && uri.AbsolutePath == value.AbsolutePath && uri.Fragment == value.Fragment && Query(uri) == Query(value));
    }
}
