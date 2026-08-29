using System;
using System.Collections.Generic;
using Bakabase.Modules.RemoteAccess.Abstractions.Components;
using AppContext = Bakabase.Infrastructures.Components.App.AppContext;

namespace Bakabase.Service.Components.RemoteAccess
{
    /// <summary>
    /// Reads the bound addresses off the host's own <see cref="AppContext"/>, which
    /// is populated once Kestrel reports what it actually listens on.
    /// </summary>
    public class AppContextListeningAddressProvider(AppContext appContext) : IListeningAddressProvider
    {
        public IReadOnlyList<string> GetListeningAddresses() =>
            appContext.ListeningAddresses ?? Array.Empty<string>();
    }
}
