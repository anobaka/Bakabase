using System;
using System.Collections.Generic;
using System.Linq;
using Bakabase.Modules.Acquisition.Models.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Bakabase.Service.Components.Acquisition.Planning;

/// <summary>ConfigurationBinder appends initialized lists; an explicitly saved priority replaces the defaults.</summary>
public sealed class AcquisitionOptionsBinding(IConfiguration configuration) : IPostConfigureOptions<AcquisitionOptions>
{
    public void PostConfigure(string? name, AcquisitionOptions options)
    {
        var section = configuration.GetSection(AcquisitionOptions.ConfigurationKey);
        var priority = section.GetChildren().FirstOrDefault(child =>
            child.Key.Equals(nameof(AcquisitionOptions.SourceProviderPriority), StringComparison.OrdinalIgnoreCase));
        // An empty JSON array has a key but no children. Exists() cannot distinguish it from a missing field.
        if (priority != null) options.SourceProviderPriority = priority.Get<List<string>>() ?? [];
    }
}
