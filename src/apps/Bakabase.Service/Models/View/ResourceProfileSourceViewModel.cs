namespace Bakabase.Service.Models.View;

public sealed record ResourceProfileSourceInfo(string DeviceId, string Name, string LibraryEpoch);

public sealed record ResourceProfileSourceViewModel(string DeviceId, string Name, string? LibraryEpoch,
    bool Available, string Kind);

/// <summary>Search previews carry library data only, never the source's effective player or other configuration.</summary>
public sealed record ResourceProfilePreviewResource(int Id, string? Path, string? DisplayName);

/// <summary>The page needs the opening behavior, not this computer's executable paths or arguments.</summary>
public sealed record ResourceProfileOpeningViewModel(string? OpeningMode, int[] ProfileIds);

public sealed record ResourceProfileOpeningResourceViewModel(int ResourceId, string? OpeningMode, int[] ProfileIds);
