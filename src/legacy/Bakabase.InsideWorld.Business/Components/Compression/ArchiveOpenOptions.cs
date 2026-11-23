using System.IO;
using System;
using System.Globalization;
using Bakabase.Abstractions.Models.Domain.Constants;

namespace Bakabase.InsideWorld.Business.Components.Compression;

/// <summary>A closed set of archive-tool opening modes, never caller-supplied command switches.</summary>
public sealed record ArchiveOpenOptions
{
    private readonly string? _archiveType;

    // Only the exact temporary link supplied by the executor may be used to read a
    // verified single-disk ZIP. Metadata checks still operate on the original regular file.
    private string? StandaloneZipAlias { get; init; }
    private string? StandaloneZipSource { get; init; }
    public static ArchiveOpenOptions ForStandaloneZipAlias(string alias, string source) =>
        new ArchiveOpenOptions("zip") {StandaloneZipAlias = Path.GetFullPath(alias), StandaloneZipSource = Path.GetFullPath(source)};

    internal string InspectionPath(string path)
    {
        if (StandaloneZipAlias == null) return path;
        if (Path.GetFullPath(path) != StandaloneZipAlias || new FileInfo(path).LinkTarget != StandaloneZipSource)
            throw new InvalidDataException("The private ZIP input no longer matches its registered source.");
        Bakabase.InsideWorld.Business.Components.Artifacts.ArtifactManifestFiles.RejectLinks(StandaloneZipSource!);
        return StandaloneZipSource!;
    }

    internal long? ExpectedPrefixOffset { get; }
    internal string? ExpectedPrefixType => ExpectedPrefixOffset.HasValue ? _archiveType : null;
    private ArchiveOpenOptions(string? archiveType) => _archiveType = archiveType;
    private ArchiveOpenOptions(string archiveType, long prefix)
    {
        _archiveType = archiveType;
        ExpectedPrefixOffset = prefix;
    }
    internal static ArchiveOpenOptions VerifiedZipPrefix(long prefix) =>
        prefix > 0 && prefix < BoundedZipStructureProbe.MaximumPrefixBytes
            ? new("zip", prefix) : throw new ArgumentOutOfRangeException(nameof(prefix));
    internal static ArchiveOpenOptions VerifiedRar5Prefix(long prefix) =>
        prefix is > 0 and <= RarVolumeHeader.MaximumJpegPrefixBytes
            ? new("rar5", prefix) : throw new ArgumentOutOfRangeException(nameof(prefix));
    internal static ArchiveOpenOptions VerifiedZipStructure(ZipStructureDescriptor structure) =>
        new ArchiveOpenOptions((string?)null) {ZipStructure = structure};
    internal ZipStructureDescriptor? ZipStructure { get; private init; }
    internal bool UsesZipCompatibilityReader => ZipPasswordCodePage != null || ZipStructure != null;
    internal long? ExpectedZipOffset => _archiveType == "zip" ? ExpectedPrefixOffset : null;
    public static ArchiveOpenOptions BoundedPrefix { get; } = new("*");
    // 7-Zip opens the numbered volumes through its Split handler, then opens the joined 7z payload.
    public static ArchiveOpenOptions VerifiedSevenZipVolumes { get; } = new("7z.split");
    internal bool OpensSevenZipVolumes => _archiveType == "7z.split";

    public ZipFileNameCodePage? ZipFileNameCodePage { get; private init; }
    public ZipPasswordCodePage? ZipPasswordCodePage { get; private init; }
    public ArchiveOpenMode? Mode { get; private init; }
    public string? MemberSelector { get; private init; }

    public static ArchiveOpenOptions Parser(string? memberSelector = null)
    {
        if (memberSelector != null && !ParserArchiveCatalog.SafeMemberName(memberSelector))
            throw new ArgumentException("Choose one exact parser member name.", nameof(memberSelector));
        return new ArchiveOpenOptions((string?)null) {Mode = ArchiveOpenMode.SevenZipParser, MemberSelector = memberSelector};
    }

    /// <summary>Compose a validated ZIP name decoder with an already authorized opening mode.</summary>
    public static ArchiveOpenOptions? WithZipFileNameCodePage(ArchiveOpenOptions? opening,
        ZipFileNameCodePage? codePage)
    {
        if (codePage is null) return opening;
        if (!Enum.IsDefined(codePage.Value)) throw new ArgumentOutOfRangeException(nameof(codePage));
        if (opening?.Mode != null) throw new ArgumentException("Parser mode and ZIP filename decoding belong in separate extraction steps.");
        return (opening ?? new ArchiveOpenOptions((string?)null)) with {ZipFileNameCodePage = codePage};
    }

    public static ArchiveOpenOptions? WithZipPasswordCodePage(ArchiveOpenOptions? opening,
        ZipPasswordCodePage? codePage)
    {
        if (codePage is null) return opening;
        if (!Enum.IsDefined(codePage.Value)) throw new ArgumentOutOfRangeException(nameof(codePage));
        if (opening?.Mode != null) throw new ArgumentException("Parser mode and ZIP password encoding belong in separate extraction steps.");
        return (opening ?? new ArchiveOpenOptions((string?)null)) with {ZipPasswordCodePage = codePage};
    }

    // A known filename suffix still changes 7-Zip's wildcard opening path. Preserve that
    // explicit format with the enlarged limit; do not allow arbitrary command-line types.
    public static ArchiveOpenOptions BoundedPrefixFor(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".zip" => new("zip"), ".rar" => new("rar"), ".7z" => new("7z"), _ => BoundedPrefix
    };

    // Preserve the default embedded-archive behavior (:r), with a finite start-position limit.
    // https://github.com/ip7z/7zip/blob/main/CPP/7zip/UI/Common/OpenArchive.cpp
    // A validated container may have another format appended to it. Open exactly that type,
    // without :r descending into another container. The bound comes from its structure,
    // not caller-supplied switches, a filename suffix or an unrestricted media scan.
    internal string? TypeArgument => Mode == ArchiveOpenMode.SevenZipParser ? "-t#" :
        OpensSevenZipVolumes ? "-t7z.split" : StandaloneZipAlias != null ? "-tzip" : ExpectedPrefixOffset is { } prefix
        ? "-t" + _archiveType + ":s" + (prefix + 1).ToString(CultureInfo.InvariantCulture) + "b"
        : _archiveType == null ? null : $"-t{_archiveType}:s32m:r";
}

public sealed record ArchiveFormatProbeResult(string Extension, ArchiveOpenOptions? OpenOptions = null);
