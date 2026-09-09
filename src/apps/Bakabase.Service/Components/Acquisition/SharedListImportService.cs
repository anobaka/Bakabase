using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Services;
using Bakabase.Modules.Acquisition.Abstractions.Models.Domain.Constants;
using Bakabase.Modules.Acquisition.Abstractions.Services;
using Bakabase.Modules.Acquisition.Components;
using Bakabase.Modules.Acquisition.Models.Input;
using Bakabase.InsideWorld.Business.Services;
using Bakabase.Modules.Collection.Abstractions.Services;
using Microsoft.Extensions.Logging;
using NPOI.SS.UserModel;

namespace Bakabase.Service.Components.Acquisition;

/// <param name="Title">What the row called it.</param>
/// <param name="Url">Where to get it, when the row said.</param>
/// <param name="Password">An archive password, when the row said.</param>
/// <param name="LineNumber">Which row it came from.</param>
/// <param name="AlreadyKnown">
/// Whether this link is already attached to a resource. Not an error — a list is often mostly
/// things you have — but it is what the user wants to see before pressing import.
/// </param>
public record SharedListPreviewRow(string? Title, string? Url, string? Password, int LineNumber,
    bool AlreadyKnown);

/// <param name="Created">Resources that did not exist before.</param>
/// <param name="Matched">Rows that turned out to be things already known.</param>
/// <param name="Started">Acquisitions started, when the caller asked for that.</param>
/// <param name="Problems">Rows that could not be imported, and why.</param>
public record SharedListImportResult(int Created, int Matched, int Started, List<string> Problems);

/// <summary>
/// Importing somebody's list of things to get.
/// <para>
/// This is how sharing actually circulates: a message with twenty lines, a spreadsheet a group
/// keeps up. Typing them in one at a time is the reason those lists sit unread, and reading one
/// wrongly is worse than not reading it — hence a preview whose only job is "did I understand
/// your file", before anything is created.
/// </para>
/// </summary>
public class SharedListImportService(
    IPlaceholderResourceService placeholders,
    IAcquisitionLeadService leads,
    IAcquisitionService acquisitions,
    ICollectionService collections,
    PasswordService passwords,
    ILogger<SharedListImportService> logger)
{
    /// <summary>
    /// Reads a file or some pasted text into rows, and says which of them are already known.
    /// Nothing is created — the preview is for checking the reading, not for committing to it.
    /// </summary>
    public async Task<List<SharedListPreviewRow>> PreviewAsync(Stream content, string? fileName,
        CancellationToken ct = default)
    {
        var rows = await ReadAsync(content, fileName, ct);
        var preview = new List<SharedListPreviewRow>(rows.Count);

        foreach (var row in rows)
        {
            var known = row.Url != null &&
                        await leads.FindByValue(AcquisitionLeadKind.SharedPage, row.Url) != null;

            preview.Add(new SharedListPreviewRow(row.Title, row.Url, row.Password, row.LineNumber,
                known));
        }

        return preview;
    }

    /// <summary>
    /// Turns rows into resources: a link becomes a lead on whatever it turns out to be about, a
    /// title alone becomes something you know you are missing.
    /// </summary>
    /// <param name="collectionId">Put them all in this collection, when there is one.</param>
    /// <param name="startAcquiring">Start getting the ones that have a link.</param>
    public async Task<SharedListImportResult> ImportAsync(IReadOnlyList<SharedListRow> rows,
        int? collectionId, bool startAcquiring, CancellationToken ct = default)
    {
        var created = 0;
        var matched = 0;
        var started = 0;
        var problems = new List<string>();

        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                // A link says more than a title: it identifies the thing and says where to get it,
                // so it is the better of the two when a row has both.
                var result = row.Url != null
                    ? await placeholders.CreateOrMatchBySharedUrl(row.Url,
                        new KnownItemDetail(row.Title), ct)
                    : await placeholders.CreateByTitle(row.Title!, ct);

                if (result.Created) created++;
                else matched++;

                if (collectionId is { } id) await collections.AddMembers(id, [result.ResourceId], ct: ct);

                if (row.Password != null) await RememberPassword(row.Password);

                if (startAcquiring && row.Url != null)
                {
                    started += await StartAcquiring(result.ResourceId, row.Url, collectionId, problems, ct);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "[SharedList] Row {Line} could not be imported", row.LineNumber);
                problems.Add($"Line {row.LineNumber}: {ex.Message}");
            }
        }

        return new SharedListImportResult(created, matched, started, problems);
    }

    /// <summary>
    /// Reads whatever was handed over. The extension decides how, because a spreadsheet is not
    /// text and reading one as text produces gibberish rather than an error.
    /// </summary>
    public async Task<List<SharedListRow>> ReadAsync(Stream content, string? fileName,
        CancellationToken ct = default)
    {
        var extension = Path.GetExtension(fileName ?? "").ToLowerInvariant();

        if (extension is ".xlsx" or ".xls")
        {
            return ReadWorkbook(content);
        }

        using var reader = new StreamReader(content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var text = await reader.ReadToEndAsync(ct);

        return extension is ".csv" or ".tsv"
            ? SharedListReader.ReadDelimited(text)
            : SharedListReader.ReadText(text);
    }

    /// <summary>The first sheet, one row at a time. A list with two sheets is not a thing anyone sends.</summary>
    private static List<SharedListRow> ReadWorkbook(Stream content)
    {
        var workbook = WorkbookFactory.Create(content);
        var sheet = workbook.GetSheetAt(0);
        var rows = new List<SharedListRow>();

        if (sheet == null) return rows;

        for (var i = sheet.FirstRowNum; i <= sheet.LastRowNum; i++)
        {
            var sheetRow = sheet.GetRow(i);

            if (sheetRow == null) continue;

            var cells = new List<string>();

            for (var c = 0; c < sheetRow.LastCellNum; c++)
            {
                var cell = sheetRow.GetCell(c);

                cells.Add(cell == null ? "" : cell.ToString() ?? "");
            }

            var row = SharedListReader.FromCells(cells, i + 1);

            if (row.Title != null || row.Url != null) rows.Add(row);
        }

        return rows;
    }

    /// <summary>
    /// A password from a list is worth keeping: unpacking tries every password it knows, and one
    /// that came with the link is the likeliest of them to be the right one.
    /// </summary>
    private async Task RememberPassword(string password)
    {
        try
        {
            await passwords.AddUsedTimes(password);
        }
        catch (Exception ex)
        {
            // A password that would not save is a smaller problem than an import that stopped
            // because of one; unpacking will ask for it if it turns out to be needed.
            logger.LogWarning(ex, "[SharedList] Could not remember a password from the list");
        }
    }

    private async Task<int> StartAcquiring(int resourceId, string url, int? collectionId,
        List<string> problems, CancellationToken ct)
    {
        var lead = (await leads.GetByResourceId(resourceId))
            .FirstOrDefault(l => l.Kind == AcquisitionLeadKind.SharedPage && l.Value == url);

        try
        {
            await acquisitions.CreateAsync(resourceId, AcquisitionLeadKind.SharedPage, url,
                lead?.Id == 0 ? null : lead?.Id, collectionId: collectionId, ct: ct);

            return 1;
        }
        catch (InvalidOperationException ex)
        {
            // Already here, or already being got. Both are fine for a list that overlaps what the
            // user has, and neither should stop the rest of the import.
            problems.Add($"{url}: {ex.Message}");

            return 0;
        }
    }
}
