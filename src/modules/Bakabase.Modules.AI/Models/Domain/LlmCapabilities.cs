namespace Bakabase.Modules.AI.Models.Domain;

[Flags]
public enum LlmCapabilities
{
    None = 0,
    Chat = 1 << 0,
    ToolCalling = 1 << 1,
    Vision = 1 << 2,
    Streaming = 1 << 3,
    Embedding = 1 << 4,
    JsonMode = 1 << 5
}
