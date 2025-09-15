using Bootstrap.Models;
using Microsoft.Extensions.Logging;

namespace Bakabase.Abstractions.Models.View;

public record MediaLibraryTemplateValidationTrace(
    LogLevel Level,
    DateTime Time,
    string Topic,
    List<KeyValue<string, string?>>? Context);