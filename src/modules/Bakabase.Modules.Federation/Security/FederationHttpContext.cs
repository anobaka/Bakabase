using Microsoft.AspNetCore.Http;

namespace Bakabase.Modules.Federation.Security;

public enum FederationEndpointKind { Local, Public, Export }

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class FederationEndpointAttribute(FederationEndpointKind kind) : Attribute
{
    public FederationEndpointKind Kind { get; } = kind;
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
