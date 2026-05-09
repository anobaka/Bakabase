namespace Bakabase.Modules.AI.Models.Domain;

[Flags]
public enum AiProviderCapability
{
    None = 0,
    Llm = 1 << 0,
    Aigc = 1 << 1,
}

public static class AiProviderCapabilities
{
    public static AiProviderCapability For(AiProviderKind kind) => kind switch
    {
        AiProviderKind.OpenAI => AiProviderCapability.Llm | AiProviderCapability.Aigc,
        AiProviderKind.Claude => AiProviderCapability.Llm,
        AiProviderKind.Ollama => AiProviderCapability.Llm,
        AiProviderKind.DashScope => AiProviderCapability.Llm,
        AiProviderKind.Gemini => AiProviderCapability.Llm | AiProviderCapability.Aigc,
        AiProviderKind.StableDiffusionWebUI => AiProviderCapability.Aigc,
        AiProviderKind.ComfyUI => AiProviderCapability.Aigc,
        AiProviderKind.HttpCustom => AiProviderCapability.Aigc,
        _ => AiProviderCapability.None,
    };

    public static bool SupportsLlm(AiProviderKind kind) => For(kind).HasFlag(AiProviderCapability.Llm);
    public static bool SupportsAigc(AiProviderKind kind) => For(kind).HasFlag(AiProviderCapability.Aigc);
}
