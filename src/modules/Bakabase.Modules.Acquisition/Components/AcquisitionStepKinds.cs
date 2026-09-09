namespace Bakabase.Modules.Acquisition.Components;

/// <summary>
/// The kinds of the built-in steps. Named here rather than as string literals in recipes so a
/// rename is a compile error instead of a recipe that silently no longer resolves.
/// <para>
/// Grammar follows workflow activity kinds: lowerCamelCase segments, <c>acquisition.{name}</c>.
/// </para>
/// </summary>
public static class AcquisitionStepKinds
{
    private const string Module = "acquisition";

    /// <summary>Reads a shared page, document or pasted text into links, codes and a title.</summary>
    public const string ResolveSharedContent = $"{Module}.resolveSharedContent";

    /// <summary>Picks which of the parsed links to use, asking when it cannot tell.</summary>
    public const string SelectLink = $"{Module}.selectLink";

    /// <summary>Downloads an http(s) link straight into the working directory.</summary>
    public const string FetchHttp = $"{Module}.fetchHttp";

    /// <summary>
    /// Opens the link for the user and waits for the file to appear in the inbox directory. The one
    /// step that deliberately leaves work to a person, because cloud drives behind logins and
    /// captchas cannot be automated for long.
    /// </summary>
    public const string WaitForInbox = $"{Module}.waitForInbox";

    /// <summary>Asks the platform that holds the resource to hand it over.</summary>
    public const string FetchFromPlatform = $"{Module}.fetchFromPlatform";

    /// <summary>Extracts archives, trying the passwords it knows about.</summary>
    public const string Unpack = $"{Module}.unpack";

    /// <summary>Lets the user point at a directory they already have.</summary>
    public const string PickLocalDirectory = $"{Module}.pickLocalDirectory";

    /// <summary>Moves the files to where the library keeps them.</summary>
    public const string Place = $"{Module}.place";

    /// <summary>Points the resource at the placed files.</summary>
    public const string Materialize = $"{Module}.materialize";
}
