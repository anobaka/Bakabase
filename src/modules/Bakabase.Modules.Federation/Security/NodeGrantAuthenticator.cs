namespace Bakabase.Modules.Federation.Security;

public sealed class NodeGrantAuthenticator(NodeGrantService grants, NodeNonceCache nonces, TimeProvider timeProvider)
{
    public async Task<NodePrincipal> AuthenticateAsync(string authorization, string method, string path,
        string rawQuery, string bodyDigest, CancellationToken ct = default)
    {
        var header = NodeRequestSignature.Parse(authorization) ?? throw Denied();
        DateTimeOffset timestamp;
        try { timestamp = DateTimeOffset.FromUnixTimeSeconds(header.Timestamp); }
        catch (ArgumentOutOfRangeException) { throw Denied(); }
        if ((timeProvider.GetUtcNow() - timestamp).Duration() > NodeRequestSignature.MaxClockSkew)
            throw new FederationAccessException("SignatureExpired", 401, "The node signature timestamp is outside the accepted window.");
        // A grant of either scope authenticates; which routes it reaches is the gate's decision, by its scope.
        var (credentials, scope) = await grants.GetCredentialsAsync(header.GrantId, ct);
        if (credentials.SubjectNodeId != header.SubjectNodeId || credentials.AudienceNodeId != header.AudienceNodeId)
            throw Denied();
        string expected;
        try
        {
            expected = NodeRequestSignature.Mac(credentials.Key,
                NodeRequestSignature.Canonical(header, method, path, rawQuery, bodyDigest));
        }
        catch (FormatException) { throw Denied(); }
        if (!NodeRequestSignature.FixedEquals(expected, header.Signature)) throw Denied();
        if (!nonces.TryConsume(header.GrantId, header.Nonce))
            throw new FederationAccessException("SignatureReplayed", 401, "This signature was already used or its replay budget is exhausted.");
        return new NodePrincipal(credentials.GrantId, credentials.SubjectNodeId, credentials.AudienceNodeId,
            credentials.LibraryEpoch, credentials.Revision, scope);
    }

    private static FederationAccessException Denied() =>
        new("InvalidNodeSignature", 401, "The node request could not be authenticated.");
}
