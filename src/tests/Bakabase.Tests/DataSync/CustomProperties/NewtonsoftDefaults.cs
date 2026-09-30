using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;

namespace Bakabase.Tests.DataSync.CustomProperties;

/// <summary>
/// Custom property options go through <see cref="JsonConvert"/>, whose global <see cref="JsonConvert.DefaultSettings"/>
/// the app replaces in <c>AppService</c>'s static constructor (camelCase, nulls ignored). Whether a test process has
/// run that constructor depends on which tests ran before, so these tests choose the settings themselves: the
/// library's defaults and the app's. The constructor is never run here: it creates the real data directory.
/// </summary>
internal static class NewtonsoftDefaults
{
    /// <summary>A copy of the settings <c>AppService</c> installs; keep it in step with that constructor.</summary>
    public static JsonSerializerSettings App() => new()
    {
        ContractResolver = new DefaultContractResolver
        {
            NamingStrategy = new CamelCaseNamingStrategy { ProcessDictionaryKeys = false },
        },
        DateFormatString = "yyyy-MM-dd HH:mm:ss.fff",
        ReferenceLoopHandling = ReferenceLoopHandling.Ignore,
        NullValueHandling = NullValueHandling.Ignore,
    };

    /// <summary>Runs <paramref name="body"/> under the library's defaults, then under the app's; restores what was set.</summary>
    public static void UnderEach(Action body)
    {
        var saved = JsonConvert.DefaultSettings;
        try
        {
            foreach (var settings in new Func<JsonSerializerSettings>?[] { null, App })
            {
                JsonConvert.DefaultSettings = settings;
                body();
            }
        }
        finally
        {
            JsonConvert.DefaultSettings = saved;
        }
    }

    /// <summary>Installs the app's settings until the returned scope is disposed.</summary>
    public static IDisposable UseApp()
    {
        var saved = JsonConvert.DefaultSettings;
        JsonConvert.DefaultSettings = App;
        return new Restore(saved);
    }

    private sealed class Restore(Func<JsonSerializerSettings>? saved) : IDisposable
    {
        public void Dispose() => JsonConvert.DefaultSettings = saved;
    }
}
