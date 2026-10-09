using System.Globalization;
using Bakabase.Abstractions.Components.Localization;

namespace Bakabase.Abstractions.Components.Tasks;

/// <summary>A task message keeps its template and value arguments until a UI snapshot is made.</summary>
public sealed class BTaskText
{
    private readonly Func<string> _render;
    private BTaskText(Func<string> render) => _render = render;

    public static BTaskText Deferred(Func<string> render) => new(render);

    public static BTaskText Localize(IBakabaseLocalizer localizer, string key, params object?[] arguments)
    {
        // Numeric/string arguments are snapshots, not closures over a changing loop counter.
        var captured = arguments.ToArray();
        return new BTaskText(() => localizer[key, captured].Value);
    }

    public static implicit operator BTaskText?(string? literal) => literal == null ? null : new(() => literal);
    public static implicit operator string?(BTaskText? text) => text?.ToString();
    public override string ToString() => _render();

    public Dictionary<string, string?> Translations() => BTaskTextCultures.Project(() => (string?)ToString());
}

public static class BTaskTextCultures
{
    /// <summary>
    /// A hub broadcasts one snapshot to devices using different languages. Render both
    /// supported languages without changing the process default or the caller's culture.
    /// The scope is synchronous: no async operation may escape it.
    /// </summary>
    public static Dictionary<string, T> Project<T>(Func<T> render)
    {
        var culture = CultureInfo.CurrentCulture;
        var uiCulture = CultureInfo.CurrentUICulture;
        try
        {
            var result = new Dictionary<string, T>();
            foreach (var (key, name) in new[] { ("en", "en-US"), ("cn", "zh-Hans") })
            {
                CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(name);
                result[key] = render();
            }
            return result;
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = uiCulture;
        }
    }
}
