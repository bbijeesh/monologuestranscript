namespace mystickymonologues.Models;

/// <summary>
/// Represents an AI model with its identifier and API endpoint
/// </summary>
public class AIModel
{
    public string DisplayName { get; set; } = "";
    public string ModelId { get; set; } = "";
    public string ApiEndpoint { get; set; } = "";
    public string Provider { get; set; } = ""; // "OpenAI" or "Gemini"
}

/// <summary>
/// Catalog of available AI models
/// </summary>
public static class AIModelCatalog
{
    public static readonly List<AIModel> OpenAIModels = new()
    {
        new AIModel
        {
            DisplayName = "Whisper v3 (Current)",
            ModelId = "whisper-1",
            ApiEndpoint = "https://api.openai.com/v1/audio/transcriptions",
            Provider = "OpenAI"
        },
        new AIModel
        {
            DisplayName = "GPT-4o Transcribe",
            ModelId = "gpt-4o-transcribe",
            ApiEndpoint = "https://api.openai.com/v1/audio/transcriptions",
            Provider = "OpenAI"
        },
        new AIModel
        {
            DisplayName = "GPT-4o mini Transcribe",
            ModelId = "gpt-4o-mini-transcribe",
            ApiEndpoint = "https://api.openai.com/v1/audio/transcriptions",
            Provider = "OpenAI"
        },
        new AIModel
        {
            DisplayName = "Whisper v2 (Legacy)",
            ModelId = "whisper-large-v2",
            ApiEndpoint = "https://api.openai.com/v1/audio/transcriptions",
            Provider = "OpenAI"
        }
    };

    public static readonly List<AIModel> GeminiModels = new()
    {
        new AIModel
        {
            DisplayName = "Gemini 3.1 Pro",
            ModelId = "gemini-3.1-pro-preview",
            ApiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3.1-pro-preview:generateContent",
            Provider = "Gemini"
        },
        new AIModel
        {
            DisplayName = "Gemini 3 Flash",
            ModelId = "gemini-3-flash-preview",
            ApiEndpoint = "https://generativelanguage.googleapis.com/v1beta/models/gemini-3-flash-preview:generateContent",
            Provider = "Gemini"
        },
        new AIModel
        {
            DisplayName = "Gemini 2.5 Pro",
            ModelId = "gemini-2.5-pro",
            ApiEndpoint = "https://generativelanguage.googleapis.com/v1/models/gemini-2.5-pro:generateContent",
            Provider = "Gemini"
        },
        new AIModel
        {
            DisplayName = "Gemini 2.5 Flash",
            ModelId = "gemini-2.5-flash",
            ApiEndpoint = "https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash:generateContent",
            Provider = "Gemini"
        },
        new AIModel
        {
            DisplayName = "Gemini 2.5 Lite",
            ModelId = "gemini-2.5-flash-lite",
            ApiEndpoint = "https://generativelanguage.googleapis.com/v1/models/gemini-2.5-flash-lite:generateContent",
            Provider = "Gemini"
        }
    };

    /// <summary>
    /// Get all models for a specific provider
    /// </summary>
    public static List<AIModel> GetModelsByProvider(string provider)
    {
        return provider.ToLower().Contains("gemini") ? GeminiModels : OpenAIModels;
    }

    /// <summary>
    /// Get a specific model by its identifier
    /// </summary>
    public static AIModel? GetModelById(string modelId)
    {
        var model = OpenAIModels.FirstOrDefault(m => m.ModelId == modelId);
        return model ?? GeminiModels.FirstOrDefault(m => m.ModelId == modelId);
    }
}
