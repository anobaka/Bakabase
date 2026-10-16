using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.Abstractions.Models.Domain;

/// <summary>An already-normalized posting; RangeValue keeps its original CLR comparison type.</summary>
public readonly record struct ResourceSearchIndexEntry(
    PropertyPool Pool, int PropertyId, string ValueKey, IComparable? RangeValue = null);
