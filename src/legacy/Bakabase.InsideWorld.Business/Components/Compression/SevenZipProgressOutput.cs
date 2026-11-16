using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CliWrap;

namespace Bakabase.InsideWorld.Business.Components.Compression;

// 7-Zip redraws its progress with backspaces on Unix and carriage returns on Windows.
// A line-oriented pipe may receive nothing until extraction exits. Keep diagnostics as
// records, but emit the leading progress field without waiting for the next newline.
internal sealed class SevenZipProgressOutput(Action<string> output)
{
    private readonly StringBuilder record = new();
    private bool reported;

    internal static PipeTarget CreatePipe(Action<string> output) => PipeTarget.Create(async (stream, ct) =>
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, false, 4096, leaveOpen: true);
        var decoder = new SevenZipProgressOutput(output);
        var buffer = new char[4096];
        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), ct).ConfigureAwait(false);
            if (read == 0) break;
            decoder.Append(buffer.AsSpan(0, read), ct);
        }
        ct.ThrowIfCancellationRequested();
        decoder.Complete();
    });

    internal void Append(ReadOnlySpan<char> text, CancellationToken ct = default)
    {
        foreach (var ch in text)
        {
            ct.ThrowIfCancellationRequested();
            if (ch is '\b' or '\r' or '\n') { Complete(); continue; }
            record.Append(ch);
            // The percent must end the leading field, not occur inside a filename.
            if (!reported && ch == ' ' && record.Length == 5 &&
                TryPercentage(record.ToString(), out var percentage))
            {
                output(percentage.ToString(CultureInfo.InvariantCulture) + "%");
                reported = true;
            }
        }
    }

    internal void Complete()
    {
        if (!reported && record.Length > 0)
        {
            var text = record.ToString();
            if (TryPercentage(text, out var percentage)) output(percentage.ToString(CultureInfo.InvariantCulture) + "%");
            else if (!string.IsNullOrWhiteSpace(text)) output(text);
        }
        record.Clear();
        reported = false;
    }

    // Consumers only accept normalized percentage records. Diagnostic paths such as
    // "ERROR: 70%.zip" or "Extracting 70% complete.zip" can never become progress.
    internal static bool TryPercentage(string record, out int percentage)
    {
        var text = record.AsSpan().Trim(' ');
        percentage = 0;
        return text.Length is >= 2 and <= 4 && text[^1] == '%' &&
               int.TryParse(text[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out percentage) &&
               percentage is >= 0 and <= 100;
    }
}
