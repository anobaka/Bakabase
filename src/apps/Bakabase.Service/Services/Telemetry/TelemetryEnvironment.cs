using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Bakabase.Infrastructures.Components.App;
using Bakabase.Modules.RemoteAccess.Abstractions.Models;
using Bakabase.Service.Components;
using Bootstrap.Models.Constants;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;

namespace Bakabase.Service.Services.Telemetry;

public sealed class TelemetryEnvironment(IDeviceIdService deviceIds, IWebHostEnvironment environment,
    IServerSelfDescription selfDescription)
{
    public string RuntimeId { get; } = Guid.NewGuid().ToString("D");
    public string DeploymentId => deviceIds.GetOrCreate();
    public string DeploymentType => DetectDeployment(AppService.RuntimeMode, selfDescription.Kind,
        System.Environment.GetEnvironmentVariable("DOTNET_RUNNING_IN_CONTAINER") == "true");
    public string ServerOs => OperatingSystem.IsWindows() ? "windows" : OperatingSystem.IsMacOS() ? "macos" :
        OperatingSystem.IsLinux() ? "linux" : "other";
    public string ServerArch => RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
    public string Environment => NormalizeEnvironment(environment.EnvironmentName);
    public static string NormalizeEnvironment(string? name) => name?.ToLowerInvariant() switch
    {
        "production" => "production", "staging" => "staging", "development" => "development",
        "test" or "testing" => "test", _ => "unknown"
    };
    public static bool CanSend(string? name) => NormalizeEnvironment(name) is "production" or "staging";
    public string ReleaseChannel => ReleaseChannelDetector.Detect(environment);
    public string ServerVersion => AppService.CoreVersion.ToString();

    public static string DetectDeployment(RuntimeMode mode, ServerKind? kind, bool inContainer) =>
        mode == RuntimeMode.Docker || inContainer ? "docker_server" :
        kind == ServerKind.Desktop ? "desktop_all_in_one" : "standalone_server";

    public Dictionary<string, object?> Properties() => new()
    {
        ["schema_version"] = 2, ["identity_version"] = 2,
        ["deployment_id"] = DeploymentId, ["deployment_type"] = DeploymentType, ["runtime_id"] = RuntimeId,
        ["server_version"] = ServerVersion, ["release_channel"] = ReleaseChannel,
        ["server_os"] = ServerOs, ["server_arch"] = ServerArch, ["environment"] = Environment
    };
}
