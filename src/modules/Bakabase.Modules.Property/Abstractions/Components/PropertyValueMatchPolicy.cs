namespace Bakabase.Modules.Property.Abstractions.Components;

/// <summary>
/// How PrepareDbValue treats biz values that don't match an existing option of a
/// reference-type property (Choice/Tags/Multilevel). Non-reference types have no
/// options and ignore the policy.
/// </summary>
public enum PropertyValueMatchPolicy
{
    /// <summary>
    /// Match against existing options only; unmatched entries are dropped and
    /// property.Options is never modified.
    /// </summary>
    MatchOnly = 1,

    /// <summary>
    /// Create missing options on the property (mutates property.Options and reports
    /// PropertyChanged = true). This is the write-path default: enhancers and user
    /// edits are expected to introduce new options.
    /// </summary>
    AutoCreateOptions = 2,
}
