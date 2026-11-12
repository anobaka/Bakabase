using System;
using System.Collections.Generic;
using System.IO;

namespace Bakabase.Abstractions.Components.FileSystem;

/// <summary>A local destination failure, separate from a remote account's storage quota.</summary>
public sealed class LocalStorageException : IOException
{
    public string Code { get; }

    public LocalStorageException(string code, Exception? inner = null) : base(code, inner)
    {
        if (code is not ("LOCAL_STORAGE_FULL" or "LOCAL_STORAGE_WRITE_FAILED" or
            "LOCAL_STORAGE_ACCESS_DENIED" or "LOCAL_STORAGE_UNAVAILABLE" or "LOCAL_STORAGE_RESERVE_LOW"))
            throw new ArgumentOutOfRangeException(nameof(code));
        Code = code;
    }
}

public static class LocalStorageFailures
{
    /// <summary>Reads only the application's bare start-rejection code or a typed exception
    /// declaration in a persisted .NET exception chain, never a code mentioned in arbitrary text.</summary>
    public static string? PersistedCode(string? message)
    {
        static bool IsKnown(string code) => code is "LOCAL_STORAGE_FULL" or "LOCAL_STORAGE_WRITE_FAILED" or
            "LOCAL_STORAGE_ACCESS_DENIED" or "LOCAL_STORAGE_UNAVAILABLE" or "LOCAL_STORAGE_RESERVE_LOW";
        if (string.IsNullOrWhiteSpace(message)) return null;
        var exact = message.Trim();
        if (IsKnown(exact)) return exact;
        var declaration = typeof(LocalStorageException).FullName + ": ";
        using var lines = new StringReader(message);
        while (lines.ReadLine() is { } raw)
        {
            var line = raw.TrimStart();
            if (line.StartsWith("---> ", StringComparison.Ordinal))
            {
                line = line[5..];
                if (line.StartsWith("(Inner Exception #", StringComparison.Ordinal))
                {
                    var end = line.IndexOf(") ", StringComparison.Ordinal);
                    if (end < 0 || !int.TryParse(line.AsSpan(18, end - 18), out _)) continue;
                    line = line[(end + 2)..];
                }
            }
            if (!line.StartsWith(declaration, StringComparison.Ordinal)) continue;
            var code = line[declaration.Length..].TrimEnd();
            if (code.EndsWith("<---", StringComparison.Ordinal)) code = code[..^4].TrimEnd();
            if (IsKnown(code)) return code;
        }
        return null;
    }

    /// <summary>Only typed failures and explicit OS error numbers establish a storage cause.
    /// Provider messages and network IOExceptions must not be guessed to mean a full disk.</summary>
    public static string? Code(Exception error)
    {
        var pending = new Stack<Exception>();
        var seen = new HashSet<Exception>();
        pending.Push(error);
        string? fallback = null;
        while (pending.Count > 0 && seen.Count < 64)
        {
            var current = pending.Pop();
            if (!seen.Add(current)) continue;
            if (current is LocalStorageException local)
            {
                if (local.Code == "LOCAL_STORAGE_FULL") return local.Code;
                fallback ??= local.Code;
            }
            if (current is IOException && IsCapacityError(current.HResult)) return "LOCAL_STORAGE_FULL";
            if (current is UnauthorizedAccessException) fallback ??= "LOCAL_STORAGE_ACCESS_DENIED";
            if (current is AggregateException aggregate)
                foreach (var child in aggregate.InnerExceptions) pending.Push(child);
            else if (current.InnerException is { } inner) pending.Push(inner);
        }
        return fallback;
    }

    internal static bool IsCapacityError(int error) =>
        // .NET also uses these HRESULTs on Unix for translated IO failures.
        error is unchecked((int)0x80070070) or unchecked((int)0x80070027) ||
        (!OperatingSystem.IsWindows() && (error == 28 ||
            error == (OperatingSystem.IsMacOS() ? 69 : 122)));
}
