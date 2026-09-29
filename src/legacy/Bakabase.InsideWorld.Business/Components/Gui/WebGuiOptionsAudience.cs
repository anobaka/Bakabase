using System;
using System.Linq;
using System.Threading.Tasks;
using Bakabase.Infrastructures.Components.Configurations.App;
using Bakabase.InsideWorld.Business.Components.Configurations;
using Bakabase.InsideWorld.Business.Components.Configurations.Models.Domain;
using Bakabase.InsideWorld.Models.Configs;
using Humanizer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;

namespace Bakabase.InsideWorld.Business.Components.Gui
{
    /// <summary>
    /// Which options a UI hub connection is sent, and the sending itself — at connect
    /// (<see cref="SendAllAsync"/>) and whenever an options object changes
    /// (<see cref="PublishAsync"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The options hold what this install keeps for itself: third-party cookies and API keys,
    /// proxy passwords, this machine's paths. The window on this machine and a device paired
    /// with it are sent every one of them, as always, and so is a browser of an Unrestricted
    /// server, which belongs to the operator. A browser on another device that has not paired —
    /// which an Enabled server lets browse the library — is sent only what browsing reads
    /// (<see cref="ForBrowsing"/>).
    /// </para>
    /// <para>
    /// Who is who is the remote-access gate's decision, taken in the Service when the connection
    /// opens and handed over through <see cref="AdmitAsync"/>; this project knows nothing about
    /// remote access. A connection nobody admitted is sent what a browsing one is, and nothing
    /// of a broadcast.
    /// </para>
    /// </remarks>
    public static class WebGuiOptionsAudience
    {
        /// <summary>Connections sent every options object.</summary>
        public const string AllOptionsGroup = "options:all";

        /// <summary>Connections sent only what browsing reads.</summary>
        public const string BrowsingOptionsGroup = "options:browsing";

        private static readonly object ReadsAllOptionsKey = new();

        /// <summary>
        /// Records what <paramref name="connection"/> may be sent, for the data it asks for
        /// and for every later change.
        /// </summary>
        public static Task AdmitAsync(HubCallerContext connection, IGroupManager groups, bool readsAllOptions)
        {
            connection.Items[ReadsAllOptionsKey] = readsAllOptions;
            return groups.AddToGroupAsync(connection.ConnectionId,
                readsAllOptions ? AllOptionsGroup : BrowsingOptionsGroup, connection.ConnectionAborted);
        }

        public static bool ReadsAllOptions(HubCallerContext connection) =>
            connection.Items.TryGetValue(ReadsAllOptionsKey, out var value) && value is true;

        /// <summary>
        /// What a browsing connection is sent of <paramref name="options"/>, or null for nothing.
        /// </summary>
        /// <remarks>
        /// Listed rather than filtered, so an options type added later reaches a browsing
        /// connection only once someone has decided it should. What is listed is what the
        /// pages such a browser can open read: the language and theme the app starts in, how
        /// the library is laid out, and the searches it offers — none of it a secret.
        /// </remarks>
        public static object? ForBrowsing(object options) =>
            options switch
            {
                AppOptions app => new {app.Language, app.UiTheme, app.EnableAnonymousDataTracking},
                UIOptions or UIStyleOptions or ResourceOptions => options,
                _ => null
            };

        /// <summary>
        /// Every options object <paramref name="connection"/> may read, to it alone.
        /// </summary>
        public static async Task SendAllAsync(BakabaseOptionsManagerPool optionsManagerPool,
            HubCallerContext connection, IWebGuiClient caller)
        {
            var readsAll = ReadsAllOptions(connection);

            foreach (var (optionsType, optionsManagerObj) in optionsManagerPool.AllOptionsManagers)
            {
                var genericType = typeof(IOptions<>).MakeGenericType(optionsType);
                var valueGetter = genericType.GetProperties()
                    .FirstOrDefault(a => a.Name == nameof(IOptions<AppOptions>.Value));
                var options = valueGetter!.GetMethod!.Invoke(optionsManagerObj, null)!;

                var sent = readsAll ? options : ForBrowsing(options);
                if (sent != null)
                {
                    await caller.OptionsChanged(NameOf(optionsType), sent);
                }
            }
        }

        /// <summary>
        /// A changed options object, to every connection by what it may read.
        /// </summary>
        public static Task PublishAsync(IHubClients<IWebGuiClient> clients, Type optionsType, object options)
        {
            var name = NameOf(optionsType);
            var all = clients.Group(AllOptionsGroup).OptionsChanged(name, options);

            return ForBrowsing(options) is { } browsing
                ? Task.WhenAll(all, clients.Group(BrowsingOptionsGroup).OptionsChanged(name, browsing))
                : all;
        }

        private static string NameOf(Type optionsType) => optionsType.Name.Camelize();
    }
}
