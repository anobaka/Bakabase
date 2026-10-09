namespace Bakabase.Service.Components.ServerData;

public sealed record AppDataImportProgressUpdate(
    string Phase,
    long CompletedBytes = 0,
    long TotalBytes = 0,
    int CompletedFiles = 0,
    int TotalFiles = 0,
    string? CurrentFile = null,
    int CompletedEntries = 0,
    int TotalEntries = 0);
