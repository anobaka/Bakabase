namespace Bakabase.Modules.Federation.Peers;

/// <summary>
/// Optional: lets the host fill the trailing capability members of this node's <see cref="NodeInfo"/>
/// (the data sync fields) before <c>/info</c> or a handshake answers. Those members are outside the
/// handshake proof. Resolved optionally; without a contributor they stay null, as on older builds.
/// </summary>
public interface INodeInfoContributor
{
    NodeInfo Contribute(NodeInfo info);
}
