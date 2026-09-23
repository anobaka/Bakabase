using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Bakabase.Abstractions.Components.Configuration;
using Bakabase.Abstractions.Exceptions;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Infrastructures.Components.App.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions;
using Bakabase.InsideWorld.Business.Components.Dependency.Abstractions.Models.Constants;
using Bakabase.InsideWorld.Business.Components.Dependency.Discovery;
using Bakabase.InsideWorld.Business.Components.Dependency.Exceptions;
using Bakabase.InsideWorld.Models.Constants;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bakabase.InsideWorld.Business.Components.Dependency
{
    public abstract class DependentComponentService : IDependentComponentService
    {
        private readonly IServiceProvider _globalServiceProvider;
        private readonly SemaphoreSlim _operationLock = new(1, 1);
        private int _completedInstallVersion;
        public abstract string Id { get; }

        public string DisplayName =>
            _globalServiceProvider.GetRequiredService<IDependencyLocalizer>()
                .Dependency_Component_Name(KeyInLocalizer) ?? GetType().Name;

        public string? Description => _globalServiceProvider.GetRequiredService<IDependencyLocalizer>()
            .Dependency_Component_Description(KeyInLocalizer);

        protected abstract string KeyInLocalizer { get; }
        public string DefaultLocation { get; }
        public abstract bool IsRequired { get; }
        protected ILogger Logger;
        protected string DirectoryName { get; }
        protected string TempDirectory { get; }

        protected DependentComponentService(ILoggerFactory loggerFactory, AppService appService,
            string directoryName, IServiceProvider globalServiceProvider)
            : this(loggerFactory, appService.ComponentsPath, directoryName, globalServiceProvider)
        {
        }

        protected DependentComponentService(ILoggerFactory loggerFactory, string componentsPath,
            string directoryName, IServiceProvider globalServiceProvider)
        {
            _globalServiceProvider = globalServiceProvider;
            Logger = loggerFactory.CreateLogger(GetType());

            DirectoryName = directoryName;
            DefaultLocation = Path.Combine(componentsPath, DirectoryName);
            TempDirectory = Path.Combine(DefaultLocation, InternalOptions.TempDirectoryName);
        }

        protected string GetExecutableWithValidation(string name) => Status == DependentComponentStatus.Installed
            ? Path.Combine(Context.Location!, name)
            : throw CreateNotReadyException();

        /// <summary>
        /// The component is not usable yet — either still downloading or never installed. Both are
        /// things the user resolves from the system settings page, so this is a
        /// <see cref="DependencyNotInstalledException"/> (an <see cref="IUserActionableException"/>)
        /// rather than a bare <see cref="Exception"/>: a bare one reads as a crash and ends up in
        /// the error dashboard.
        /// </summary>
        private DependencyNotInstalledException CreateNotReadyException()
        {
            var localizer = _globalServiceProvider.GetRequiredService<IDependencyLocalizer>();
            var message = Status == DependentComponentStatus.Installing
                ? localizer.Dependency_Installing_Message(DisplayName)
                : localizer.Dependency_NotInstalled_Message(DisplayName);
            return new DependencyNotInstalledException(KeyInLocalizer, DisplayName, message);
        }

        /// <summary>
        /// Override this property to declare dependencies that must be installed before this component.
        /// The dependencies will be automatically installed during the installation process.
        /// </summary>
        protected virtual IEnumerable<Type> Dependencies => Enumerable.Empty<Type>();

        protected abstract Task InstallCore(CancellationToken ct);

        /// <summary>
        /// How long an optional caller trusts a "not found" before probing again. Discovery spawns a
        /// process, and callers such as cover discovery ask once per resource.
        /// </summary>
        protected virtual TimeSpan MissingRediscoveryInterval => TimeSpan.FromSeconds(10);

        /// <summary><see cref="Environment.TickCount64"/> of the last optional miss, or -1.</summary>
        private long _missingDiscoveredAt = -1;

        public virtual async Task EnsureReadyAsync(CancellationToken ct)
        {
            if (Status == DependentComponentStatus.Installed)
            {
                return;
            }

            // Optional features skip the tool while it installs or was just found missing, as the
            // status check they replaced did, instead of waiting out a download or probing again.
            var missingAt = Volatile.Read(ref _missingDiscoveredAt);
            if (!IsRequired && (Status == DependentComponentStatus.Installing || missingAt >= 0 &&
                    Environment.TickCount64 - missingAt < MissingRediscoveryInterval.TotalMilliseconds))
            {
                throw CreateNotReadyException();
            }

            await _operationLock.WaitAsync(ct);
            try
            {
                if (Status == DependentComponentStatus.Installed)
                {
                    return;
                }

                ThrowIfUnsupported();
                await DiscoverCore(ct);
                if (Status != DependentComponentStatus.Installed && !IsRequired)
                {
                    Volatile.Write(ref _missingDiscoveredAt, Environment.TickCount64);
                }
                if (Status != DependentComponentStatus.Installed && IsRequired)
                {
                    await InstallWhileLocked(ct);
                }

                if (Status != DependentComponentStatus.Installed)
                {
                    throw CreateNotReadyException();
                }
            }
            finally
            {
                _operationLock.Release();
            }
        }

        public virtual async Task Install(CancellationToken ct)
        {
            // Overlapping requests wait for the successful install. A later explicit request
            // still checks for updates, including when the component is already installed.
            var observedVersion = Volatile.Read(ref _completedInstallVersion);
            await _operationLock.WaitAsync(ct);
            try
            {
                if (observedVersion != _completedInstallVersion && Status == DependentComponentStatus.Installed)
                {
                    return;
                }

                ThrowIfUnsupported();
                await DiscoverCore(ct);
                await InstallWhileLocked(ct);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private void ThrowIfUnsupported()
        {
            if (!IsAvailableOnCurrentPlatform)
            {
                throw new PlatformNotSupportedException($"{DisplayName} is not available on this platform.");
            }
        }

        private async Task InstallWhileLocked(CancellationToken ct)
        {
            Status = DependentComponentStatus.Installing;
            try
            {
                await UpdateContext(d =>
                {
                    d.Error = null;
                    d.InstallationProgress = 0;
                });

                foreach (var dependencyType in Dependencies)
                {
                    if (_globalServiceProvider.GetRequiredService(dependencyType) is not IDependentComponentService dependency)
                    {
                        throw new InvalidOperationException($"Dependency type {dependencyType.Name} is not a valid IDependentComponentService");
                    }

                    // Installation authorizes its prerequisites too, but reuse an existing
                    // system installation before downloading another copy.
                    await dependency.Discover(ct);
                    if (dependency.Status != DependentComponentStatus.Installed)
                    {
                        await dependency.Install(ct);
                    }
                }

                await InstallCore(ct);
                await DiscoverCore(ct);
                if (Status != DependentComponentStatus.Installed)
                {
                    throw CreateNotReadyException();
                }

                await UpdateContext(d => { d.InstallationProgress = 100; });
                Interlocked.Increment(ref _completedInstallVersion);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                await RestoreAfterFailedInstall(null);
                throw;
            }
            catch (Exception e)
            {
                await RestoreAfterFailedInstall(e.Message);
                var message = $"An error occurred during installing {DisplayName}: {e.Message}";
                if (IsNetworkException(e))
                {
                    Logger.LogWarning(e, message);
                }
                else
                {
                    Logger.LogError(e, message);
                }

                throw;
            }
        }

        /// <summary>
        /// A failed or cancelled update must not hide a copy that still works, so the status
        /// comes from discovering what is on disk now; the error stays visible either way.
        /// </summary>
        private async Task RestoreAfterFailedInstall(string? error)
        {
            try
            {
                await DiscoverCore(CancellationToken.None);
            }
            catch (Exception e)
            {
                Logger.LogWarning(e, $"Failed to rediscover {DisplayName} after an unsuccessful install: {e.Message}");
                Status = DependentComponentStatus.NotInstalled;
            }

            await UpdateContext(d => d.Error = error);
        }

        private static bool IsNetworkException(Exception? e)
        {
            while (e != null)
            {
                if (e is HttpRequestException or SocketException or IOException or TaskCanceledException)
                {
                    return true;
                }

                e = e.InnerException;
            }

            return false;
        }

        /// <summary>
        /// <inheritdoc cref="IDependentComponentService.Discover"/>
        /// </summary>
        /// <param name="ct"></param>
        /// <returns></returns>
        public virtual async Task Discover(CancellationToken ct)
        {
            await _operationLock.WaitAsync(ct);
            try
            {
                Volatile.Write(ref _missingDiscoveredAt, -1);
                await DiscoverCore(ct);
            }
            finally
            {
                _operationLock.Release();
            }
        }

        private async Task DiscoverCore(CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            var r = IsAvailableOnCurrentPlatform ? await Discoverer.Discover(DefaultLocation, ct) : null;
            ct.ThrowIfCancellationRequested();
            Status = string.IsNullOrEmpty(r?.Version)
                ? DependentComponentStatus.NotInstalled
                : DependentComponentStatus.Installed;
            await UpdateContext(c =>
            {
                c.Location = r?.Location;
                c.Version = r?.Version;
                if (Status == DependentComponentStatus.Installed)
                {
                    c.Error = null;
                }
            });
        }

        protected abstract IDiscoverer Discoverer { get; }

        public virtual bool IsAvailableOnCurrentPlatform => true;

        public DependentComponentStatus Status { get; protected set; } = DependentComponentStatus.NotInstalled;

        private DependentComponentVersion? _latestVersion;

        public async Task<DependentComponentVersion> GetLatestVersion(bool fromCache, CancellationToken ct)
        {
            if (!fromCache || _latestVersion == null)
            {
                try
                {
                    _latestVersion = await GetLatestVersion(ct);
                }
                catch (Exception e)
                {
                    Logger.LogError(e,
                        $"An error occurred during getting latest version of dependent component {this.DisplayName}");
                    _latestVersion = DependentComponentVersion.Unknown;
                }
            }

            if (_latestVersion.CanUpdate)
            {
                if (Context.Version == _latestVersion.Version)
                {
                    _latestVersion.CanUpdate = false;
                }
            }

            return _latestVersion;
        }

        public abstract Task<DependentComponentVersion> GetLatestVersion(CancellationToken ct);


        protected async Task UpdateContext(Action<DependentComponentContext> update)
        {
            update(Context);
            await TriggerOnStateChange();
        }

        public virtual DependentComponentContext Context { get; } = new();

        public event Func<DependentComponentContext, Task>? OnStateChange;

        protected virtual async Task TriggerOnStateChange()
        {
            if (OnStateChange != null)
            {
                await OnStateChange(Context);
            }
        }
    }
}
