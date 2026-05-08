/**
 * Provider-kind specific JSON example snippets, rendered as the initial editor
 * content (with `//` comments) when the user has no ConfigJson yet. The backend
 * invokers strip these comments via JsonCommentHandling.Skip.
 */
export const AigcProviderConfigSamples: Record<number, { header: string; body: string }> = {
  // StableDiffusionWebUI
  1: {
    header:
      `// Stable Diffusion WebUI: defaults are sent in the request body if not\n` +
      `// overridden by the generator's parametersJson. All fields are optional.`,
    body: `{
  "sampler_name": "DPM++ 2M Karras",
  "cfg_scale": 7,
  "steps": 20,
  "width": 512,
  "height": 512
}`,
  },
  // ComfyUI
  2: {
    header:
      `// ComfyUI: provide a default workflow JSON. Tokens "{prompt}",\n` +
      `// "{negativePrompt}" and "{seed}" are substituted before submission.\n` +
      `// You can also override per-generator via parametersJson.workflow.`,
    body: `{
  "defaultWorkflow": {
    "3": {
      "inputs": { "seed": "{seed}", "steps": 20, "cfg": 7, "sampler_name": "euler", "scheduler": "normal", "denoise": 1, "model": ["4", 0], "positive": ["6", 0], "negative": ["7", 0], "latent_image": ["5", 0] },
      "class_type": "KSampler"
    }
  }
}`,
  },
  // OpenAIImage
  3: {
    header:
      `// OpenAI Image: optional defaults. Override per-generator via\n` +
      `// parametersJson (model, size, quality, n).`,
    body: `{
  "defaultModel": "dall-e-3",
  "defaultSize": "1024x1024"
}`,
  },
  // GeminiImage (Nano Banana)
  4: {
    header:
      `// Gemini Image: optional defaults. Override the model per-generator via\n` +
      `// parametersJson.model (e.g. "gemini-2.5-flash-image-preview").`,
    body: `{
  "defaultModel": "gemini-2.5-flash-image-preview"
}`,
  },
  // HttpCustom
  99: {
    header:
      `// Custom HTTP: a generic invoker driven by templates. Tokens supported\n` +
      `// inside any template string: {prompt} {negativePrompt} {apiKey} {seed}\n` +
      `// plus any key from the generator's parametersJson.\n` +
      `// Set asyncMode to "polling" if your provider returns a task id and\n` +
      `// requires a status URL to be polled.`,
    body: `{
  "method": "POST",
  "urlTemplate": "https://api.example.com/v1/generate",
  "headers": {
    "Authorization": "Bearer {apiKey}",
    "Content-Type": "application/json"
  },
  "bodyTemplate": "{ \\"prompt\\": \\"{prompt}\\", \\"seed\\": {seed} }",
  "asyncMode": "none",
  "outputs": [
    {
      "mediaType": "Image",
      "extractFromPath": "$.data[*].b64_json",
      "encoding": "base64",
      "extension": "png"
    }
  ]
}`,
  },
};

export const AigcGeneratorParametersSamples: Record<number, { header: string; body: string }> = {
  1: {
    header: `// Stable Diffusion WebUI parameters`,
    body: `{
  "width": 768,
  "height": 768,
  "steps": 30,
  "cfg_scale": 7.0,
  "sampler_name": "DPM++ 2M Karras",
  "batch_size": 1,
  "n_iter": 1
}`,
  },
  2: {
    header:
      `// ComfyUI: usually leave empty so the provider's defaultWorkflow is used.\n` +
      `// You can paste a per-generator workflow under "workflow".`,
    body: `{
  "seed": 0
}`,
  },
  3: {
    header: `// OpenAI Image parameters`,
    body: `{
  "model": "dall-e-3",
  "size": "1024x1024",
  "quality": "standard",
  "n": 1
}`,
  },
  4: {
    header: `// Gemini Image parameters`,
    body: `{
  "model": "gemini-2.5-flash-image-preview"
}`,
  },
  99: {
    header: `// Custom HTTP: free-form. Anything here is exposed as {token} in templates.`,
    body: `{
  "seed": 12345
}`,
  },
};
