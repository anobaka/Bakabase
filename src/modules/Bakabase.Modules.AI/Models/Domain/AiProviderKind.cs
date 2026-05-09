namespace Bakabase.Modules.AI.Models.Domain;

/// <summary>
/// Real-world AI service providers. A single provider entity may expose one or more
/// <see cref="AiProviderCapability"/> flags. Values 1-5 match the legacy LlmProviderType
/// values one-to-one to keep existing rows compatible.
/// </summary>
public enum AiProviderKind
{
    OpenAI = 1,
    Claude = 2,
    Ollama = 3,
    DashScope = 4,
    Gemini = 5,

    StableDiffusionWebUI = 100,
    ComfyUI = 101,
    HttpCustom = 199,
}
