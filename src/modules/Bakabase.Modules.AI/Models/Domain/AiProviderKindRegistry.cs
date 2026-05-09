namespace Bakabase.Modules.AI.Models.Domain;

/// <summary>
/// Single source of truth for per-kind metadata (display name, connection requirements,
/// AIGC media support). Avoids the situation where ILlmProviderFactory and
/// IAigcProviderInvoker both declare the same kind with possibly diverging metadata.
/// </summary>
public static class AiProviderKindRegistry
{
    public static readonly IReadOnlyDictionary<AiProviderKind, AiProviderKindInfo> All =
        new[]
        {
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.OpenAI,
                DisplayName = "OpenAI",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.OpenAI),
                RequiresApiKey = true,
                RequiresEndpoint = false,
                DefaultEndpoint = null,
                SupportedAigcMediaTypes = [AigcMediaType.Image]
            },
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.Claude,
                DisplayName = "Claude",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.Claude),
                RequiresApiKey = true,
                RequiresEndpoint = false,
                DefaultEndpoint = "https://api.anthropic.com/v1/",
            },
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.Ollama,
                DisplayName = "Ollama",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.Ollama),
                RequiresApiKey = false,
                RequiresEndpoint = true,
                DefaultEndpoint = "http://localhost:11434",
            },
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.DashScope,
                DisplayName = "阿里云百炼",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.DashScope),
                RequiresApiKey = true,
                RequiresEndpoint = false,
                DefaultEndpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            },
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.Gemini,
                DisplayName = "Google Gemini",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.Gemini),
                RequiresApiKey = true,
                RequiresEndpoint = false,
                DefaultEndpoint = "https://generativelanguage.googleapis.com/v1beta/openai/",
                SupportedAigcMediaTypes = [AigcMediaType.Image]
            },
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.StableDiffusionWebUI,
                DisplayName = "Stable Diffusion WebUI",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.StableDiffusionWebUI),
                RequiresApiKey = false,
                RequiresEndpoint = true,
                DefaultEndpoint = "http://127.0.0.1:7860",
                SupportedAigcMediaTypes = [AigcMediaType.Image]
            },
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.ComfyUI,
                DisplayName = "ComfyUI",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.ComfyUI),
                RequiresApiKey = false,
                RequiresEndpoint = true,
                DefaultEndpoint = "http://127.0.0.1:8188",
                SupportedAigcMediaTypes = [AigcMediaType.Image, AigcMediaType.Video]
            },
            new AiProviderKindInfo
            {
                Kind = AiProviderKind.HttpCustom,
                DisplayName = "Custom HTTP",
                Capabilities = AiProviderCapabilities.For(AiProviderKind.HttpCustom),
                RequiresApiKey = false,
                RequiresEndpoint = false,
                DefaultEndpoint = null,
                SupportedAigcMediaTypes =
                [
                    AigcMediaType.Image, AigcMediaType.Text,
                    AigcMediaType.Audio, AigcMediaType.Video, AigcMediaType.Other
                ]
            },
        }.ToDictionary(x => x.Kind);
}
