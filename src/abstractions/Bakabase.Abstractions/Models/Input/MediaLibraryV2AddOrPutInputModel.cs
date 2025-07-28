using Bakabase.Abstractions.Models.Domain;

namespace Bakabase.Abstractions.Models.Input;

public record MediaLibraryV2AddOrPutInputModel(string Name, List<string> Paths, string? Color = null, List<MediaLibraryPlayer>? Players = null);