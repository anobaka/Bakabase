using Bakabase.Abstractions.Components.TextProcessing;
using Bakabase.Modules.BulkModification.Abstractions.Components;

namespace Bakabase.Modules.BulkModification.Components.Processors.String;

public record BulkModificationStringProcessorOptions : TextProcessingOptions, IBulkModificationProcessorOptions;