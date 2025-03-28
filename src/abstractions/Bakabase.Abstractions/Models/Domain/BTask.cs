﻿using Bakabase.Abstractions.Components.Tasks;
using Bakabase.Abstractions.Models.Domain.Constants;
using Bootstrap.Components.Tasks;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace Bakabase.Abstractions.Models.Domain;

public record BTask
{
    public string Id { get; }
    private readonly Func<string> _getName;
    public string Name => _getName();
    private readonly Func<string?>? _getDescription;
    public string? Description => _getDescription?.Invoke();
    private readonly Func<string?>? _getMessageOnInterruption;
    public string? MessageOnInterruption => _getMessageOnInterruption?.Invoke();
    public DateTime CreatedAt { get; } = DateTime.Now;
    public HashSet<string>? ConflictKeys { get; }
    public BTaskLevel Level { get; }
    public string? Error { get; set; }
    public TimeSpan? Interval { get; set; }
    public DateTime? EnableAfter { get; set; }
    public BTaskStatus Status { get; set; }
    public DateTime? StartedAt { get; set; }
    public int Percentage { get; set; }
    public string? Message { get; set; }
    public string? Process { get; set; }
    public DateTime? LastFinishedAt { get; set; }
    public bool IsPersistent { get; }
    public TimeSpan? Elapsed { get; set; }

    public BTask(string id,
        Func<string> getName,
        Func<string?>? getDescription = null,
        Func<string?>? getMessageOnInterruption = null,
        HashSet<string>? conflictKeys = null,
        BTaskLevel level = BTaskLevel.Default,
        bool isPersistent = false)
    {
        Id = id;
        _getName = getName;
        _getDescription = getDescription;
        _getMessageOnInterruption = getMessageOnInterruption;

        ConflictKeys = conflictKeys;
        Level = level;
        IsPersistent = isPersistent;
    }
}