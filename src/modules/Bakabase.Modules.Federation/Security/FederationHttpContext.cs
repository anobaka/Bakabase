using Bakabase.Modules.Federation.Peers;
using Microsoft.AspNetCore.Http;

namespace Bakabase.Modules.Federation.Security;

public enum FederationEndpointKind { Local, Public, Export }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class FederationEndpointAttribute(FederationEndpointKind kind) : Attribute
{
    public FederationEndpointKind Kind { get; } = kind;

    /// <summary>
    /// Export endpoints only: the grant scope that may reach the action — <see cref="FederationScopes.LibraryRead"/>,
    /// <see cref="FederationScopes.DataSyncRead"/>, or <see cref="FederationScopes.Any"/> for the handshake.
    /// Every Export action declares one; Local and Public endpoints declare none.
    /// </summary>
    public string? Scope { get; set; }
}

public static class FederationHttpContext
{
    private static readonly object KindKey = new();
    private static readonly object PrincipalKey = new();

    public static bool IsHandled(HttpContext context) => context.Items.ContainsKey(KindKey);
    public static FederationEndpointKind? GetKind(HttpContext context) =>
        context.Items.TryGetValue(KindKey, out var value) ? value as FederationEndpointKind? : null;
    public static NodePrincipal? GetNodePrincipal(HttpContext context) =>
        context.Items.TryGetValue(PrincipalKey, out var value) ? value as NodePrincipal : null;

    public static void MarkHandled(HttpContext context, FederationEndpointKind kind, NodePrincipal? principal = null)
    {
        context.Items[KindKey] = kind;
        if (principal != null) context.Items[PrincipalKey] = principal;
    }
}
