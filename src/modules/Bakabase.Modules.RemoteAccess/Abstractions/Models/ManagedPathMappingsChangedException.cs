namespace Bakabase.Modules.RemoteAccess.Abstractions.Models;

/// <summary>A recovered or concurrent editor must reload before replacing another editor's mappings.</summary>
public sealed class ManagedPathMappingsChangedException() : InvalidOperationException(
    "The saved path mappings changed. Reload them before saving this edit.");
