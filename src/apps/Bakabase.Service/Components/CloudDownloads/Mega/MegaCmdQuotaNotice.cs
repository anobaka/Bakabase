using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Bakabase.Modules.Downloader.Cloud;

namespace Bakabase.Service.Components.CloudDownloads.Mega;

/// <summary>The exclusively owned server can report a quota wait without exiting get or
/// changing its transfer state. Observe only new official notices during this get.</summary>
internal sealed class MegaCmdQuotaNotice : IDisposable
{
    private static readonly Regex QuotaLine = new(
        @"(?:\A|\n)\d{4}-\d{2}-\d{2}_\d{2}-\d{2}-\d{2}\.\d+ cmd WARN (?:Reached bandwidth quota\. Your download could not proceed because it would take you over the current free transfer allowance for your IP address\.|Transfer quota exceeded\. You can't continue downloading as you've used all of the transfer quota available to you\.)",
        RegexOptions.CultureInvariant | RegexOptions.NonBacktracking);
    private readonly FileStream _log;
    private readonly byte[] _buffer = new byte[16 * 1024];
    private string _suffix;
    private bool _waiting;
    private double? _percentageAtNotice;

    private MegaCmdQuotaNotice(FileStream log)
    {
        _log = log;
        // A line already in progress when get starts cannot be attributed to this transfer.
        _suffix = log.Length == 0 || ReadLastByte(log) == '\n' ? "" : "\0";
    }

    private static int ReadLastByte(FileStream log)
    {
        log.Seek(-1, SeekOrigin.End);
        return log.ReadByte();
    }

    internal static MegaCmdQuotaNotice? Observe(string path)
    {
        FileStream? log = null;
        try
        {
            log = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return new(log);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            log?.Dispose();
            return null;
        }
    }

    internal CloudDownloadProgress Apply(CloudDownloadProgress snapshot)
    {
        var notice = ReadNotice();
        if (snapshot.Stage is "TransferFinishing" or "TransferCancelled" or "TransferFailed")
            _waiting = false;
        else if (notice)
        {
            _waiting = true;
            _percentageAtNotice = snapshot.Percentage;
        }
        else if (_waiting && snapshot.Percentage is { } current)
        {
            _percentageAtNotice ??= current;
            if (current > _percentageAtNotice) _waiting = false;
        }
        return _waiting ? snapshot with { Stage = "WaitingForTransferQuota" } : snapshot;
    }

    private bool ReadNotice()
    {
        try
        {
            var end = _log.Length;
            if (_log.Position > end || end - _log.Position > _buffer.Length)
            {
                _log.Position = Math.Max(0, end - _buffer.Length);
                _suffix = _log.Position == 0 ? "" : "\0";
            }
            var count = _log.Read(_buffer, 0, (int)(end - _log.Position));
            if (count == 0) return false;
            // All markers are ASCII. Keep only enough of the last line to match a split notice.
            var sample = _suffix + Encoding.ASCII.GetString(_buffer, 0, count);
            var matched = QuotaLine.IsMatch(sample);
            var lastLine = sample.AsSpan(sample.LastIndexOf('\n') + 1);
            _suffix = lastLine.Length > 300 || QuotaLine.IsMatch(lastLine) ? "\0" : lastLine.ToString();
            return matched;
        }
        catch (IOException) { return false; }
    }

    public void Dispose() => _log.Dispose();
}
