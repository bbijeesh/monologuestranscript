using mystickymonologues.Models;
using Newtonsoft.Json;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;

namespace mystickymonologues.Services;

public class AIService
{
    private readonly SettingsService _settings;
    private static readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

    public AIService(SettingsService settings)
    {
        _settings = settings;
    }

    private string GetApiKey()
    {
        var s = _settings.Settings;
        if (!string.IsNullOrWhiteSpace(s.AIKeyFilePath) && File.Exists(s.AIKeyFilePath))
            return File.ReadAllText(s.AIKeyFilePath).Trim();
        return _settings.GetApiKeyForModel(s.AIModelId);
    }

    public async Task<string> TranscribeAndFixAsync(byte[] audioWavData)
    {
        var provider = _settings.Settings.AIProvider?.ToLower() ?? "openai";
        var modelId = _settings.Settings.AIModelId ?? "whisper-1";
        var apiKey = GetApiKey();

        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException("No API key configured.");

        // Get the full model info including endpoint
        var model = AIModelCatalog.GetModelById(modelId);
        if (model == null)
            throw new InvalidOperationException($"Unknown model: {modelId}");

        return model.Provider switch
        {
            "OpenAI" => await TranscribeOpenAIAsync(audioWavData, apiKey, model),
            "Gemini" => await TranscribeGeminiAsync(audioWavData, apiKey, model),
            _ => throw new InvalidOperationException($"Unknown provider: {model.Provider}")
        };
    }

    private async Task<string> TranscribeOpenAIAsync(byte[] audioData, string apiKey, AIModel model)
    {
        using var content = new MultipartFormDataContent();
        var audioContent = new ByteArrayContent(audioData);
        audioContent.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");

        content.Add(audioContent, "file", "audio.wav");
        content.Add(new StringContent(model.ModelId), "model");
        content.Add(new StringContent("en"), "language");

        // Add a 'prompt' to guide Whisper away from hallucinations
        content.Add(new StringContent("This is a short, literal voice note transcription."), "prompt");

        var request = new HttpRequestMessage(HttpMethod.Post, model.ApiEndpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = content;

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        dynamic? obj = JsonConvert.DeserializeObject(json);
        string raw = obj?.text?.ToString() ?? "";

        // If the output is too short or just punctuation, it's likely noise
        if (string.IsNullOrWhiteSpace(raw) || raw.Length < 2) return "";

        // Always run through the Fixer to filter out 'Quick Brown Fox' etc.
        return await FixTextWithGPTAsync(raw, apiKey);
    }

    
    private async Task<string> FixTextWithGPTAsync(string rawText, string apiKey)
    {
        // Local check: if Whisper returned a known common hallucination, kill it immediately
        string lowerText = rawText.ToLower();
        if (lowerText.Contains("quick brown fox") ||
            lowerText.Contains("thank you for watching") ||
            lowerText.Contains("subtitle by"))
        {
            return "";
        }

        var payload = new
        {
            model = "gpt-4o-mini",
            messages = new[]
            {
            new {
                role = "system",
                content = "You are a transcription validator. " +
                          "Your job is to clean grammar and spelling. " +
                          "CRITICAL: If the input text looks like a hallucination (e.g., 'The quick brown fox', 'Thank you for watching', or random alphabet strings), return an empty string \"\". " +
                          "Return ONLY the corrected text or an empty string. No commentary."
            },
            new { role = "user", content = rawText }
        },
            temperature = 0.0, // Critical for consistent validation
            max_tokens = 500
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        if (!response.IsSuccessStatusCode) return rawText; // Fallback to raw if GPT fails

        var json = await response.Content.ReadAsStringAsync();
        dynamic? obj = JsonConvert.DeserializeObject(json);
        return obj?.choices?[0]?.message?.content?.ToString()?.Trim() ?? "";
    }
    private async Task<string> TranscribeGeminiAsync(byte[] audioData, string apiKey, AIModel model)
    {
        var base64 = Convert.ToBase64String(audioData);
        var url = $"{model.ApiEndpoint}?key={apiKey}";

        var payload = new
        {
            
            system_instruction = new
            {
                parts = new[] { new {
                text = "You are a literal Audio-to-Text Transcriber. " +
                       "CRITICAL: If the audio is silent or contains no speech, return an empty string \"\" and nothing else. " +
                       "Do not output 'The quick brown fox jumps over the lazy dog', do not ask questions, and do not provide help templates. " +
                       "Accuracy is more important than being helpful."
            } }
            },
            contents = new[]
            {
            new
            {
                parts = new object[]
                {
                    new { inline_data = new { mime_type = "audio/wav", data = base64 } }
                    
                }
            }
        },
            generationConfig = new
            {
                temperature = 0.0, // Force absolute determinism
                topP = 0.1,        // Restrict the word choice pool
                maxOutputTokens = 500
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"Gemini API error {(int)response.StatusCode}: {err}");
        }

        var json = await response.Content.ReadAsStringAsync();
        dynamic? obj = JsonConvert.DeserializeObject(json);

        // Safety check for empty candidates
        string? result = obj?.candidates?[0]?.content?.parts?[0]?.text?.ToString();
        return result?.Trim() ?? "";
    }

    /// <summary>
    /// Pings the selected LLM to warm up the connection.
    /// Fire-and-forget method to avoid blocking startup.
    /// </summary>
    public async Task WarmupLLMAsync()
    {
        try
        {
            var provider = _settings.Settings.AIProvider?.ToLower() ?? "openai";
            var modelId = _settings.Settings.AIModelId ?? "whisper-1";
            var apiKey = GetApiKey();

            if (string.IsNullOrWhiteSpace(apiKey))
            {
                System.Diagnostics.Debug.WriteLine("LLM warmup skipped: No API key configured.");
                return;
            }

            var model = AIModelCatalog.GetModelById(modelId);
            if (model == null)
            {
                System.Diagnostics.Debug.WriteLine($"LLM warmup skipped: Unknown model {modelId}");
                return;
            }

            var warmupTask = model.Provider switch
            {
                "OpenAI" => WarmupOpenAIAsync(apiKey, model),
                "Gemini" => WarmupGeminiAsync(apiKey, model),
                _ => Task.CompletedTask
            };
            await warmupTask;

            System.Diagnostics.Debug.WriteLine($"LLM warmup completed for {model.ModelId}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"LLM warmup failed: {ex.Message}");
            // Silently fail - don't block app startup
        }
    }

    private async Task WarmupOpenAIAsync(string apiKey, AIModel model)
    {
        var payload = new
        {
            model = model.ModelId,
            messages = new[]
            {
                new { role = "system", content = "You are a helpful assistant." },
                new { role = "user", content = "Hello" }
            },
            max_tokens = 10,
            temperature = 0.0
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }

    private async Task WarmupGeminiAsync(string apiKey, AIModel model)
    {
        var url = $"{model.ApiEndpoint}?key={apiKey}";

        var payload = new
        {
            contents = new[]
            {
                new
                {
                    parts = new[] { new { text = "Hello" } }
                }
            },
            generationConfig = new
            {
                temperature = 0.0,
                maxOutputTokens = 10
            }
        };

        var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonConvert.SerializeObject(payload), Encoding.UTF8, "application/json");

        var response = await _http.SendAsync(request);
        response.EnsureSuccessStatusCode();
    }
}
