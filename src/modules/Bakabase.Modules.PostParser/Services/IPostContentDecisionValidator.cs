using Bakabase.Modules.PostParser.Models.Domain;

namespace Bakabase.Modules.PostParser.Services;

/// <summary>Optional source-specific evidence constraints. A passing check alone never classifies a block.</summary>
public interface IPostContentDecisionValidator
{
    string Source { get; }
    bool CanIgnore(PostContentLock block, PostContentBlockDecision decision);
}
