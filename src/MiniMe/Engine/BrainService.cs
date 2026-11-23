using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using MiniMe.Models;

namespace MiniMe.Engine;

public sealed class BrainService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(15)
    };

    private static readonly string[] CodingKeywords =
    [
        "visual studio", "vs code", "code", "pycharm", "rider", "intellij", "webstorm",
        "terminal", "powershell", "cmd.exe", "command prompt", "wsl", "cursor", "neovim", "github"
    ];

    private static readonly string[] VideoSocialKeywords =
    [
        "youtube", "netflix", "twitch", "instagram", "tiktok", "discord",
        "twitter", "reddit", "facebook", "whatsapp", "shahid", "spotify"
    ];

    private static readonly string[] BrowserKeywords =
    [
        "google chrome", "microsoft edge", "mozilla firefox", "brave", "opera", "vivaldi", "arc"
    ];

    private readonly Dictionary<string, QuipBucket> _quips = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastQuipPerCategory = new(StringComparer.OrdinalIgnoreCase);
    private readonly Random _rng = new();

    private sealed class QuipBucket
    {
        public List<string> Ar { get; set; } = [];
        public List<string> En { get; set; } = [];
    }

    public BrainService()
    {
        LoadQuips();
    }

    private void LoadQuips()
    {
        string? json = null;

        // 1. Prefer copied local Assets/quips.json if present (allows easy user customization)
        string diskPath = Path.Combine(AppContext.BaseDirectory, "Assets", "quips.json");
        if (File.Exists(diskPath))
        {
            try
            {
                json = File.ReadAllText(diskPath, Encoding.UTF8);
            }
            catch
            {
                json = null;
            }
        }

        // 2. Fallback to embedded resource so single-file .exe works standalone anywhere
        if (string.IsNullOrWhiteSpace(json))
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using Stream? stream = asm.GetManifestResourceStream("MiniMe.Assets.quips.json");
            if (stream is not null)
            {
                using var reader = new StreamReader(stream, Encoding.UTF8);
                json = reader.ReadToEnd();
            }
        }

        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        try
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var parsed = JsonSerializer.Deserialize<Dictionary<string, QuipBucket>>(json, options);
            if (parsed is not null)
            {
                foreach ((string key, QuipBucket bucket) in parsed)
                {
                    _quips[key] = bucket;
                }
            }
        }
        catch
        {
            // Keep safe fallback if user edited JSON with a syntax error
        }
    }

    public string DetectWindowCategory(string windowTitle)
    {
        if (string.IsNullOrWhiteSpace(windowTitle))
        {
            return "idle";
        }

        string lower = windowTitle.ToLowerInvariant();
        if (VideoSocialKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
        {
            return "video_social";
        }

        if (CodingKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
        {
            return "coding";
        }

        if (BrowserKeywords.Any(k => lower.Contains(k, StringComparison.Ordinal)))
        {
            return "browser";
        }

        return "idle";
    }

    public string GetOfflineQuip(string category, QuipLanguage language)
    {
        if (!_quips.TryGetValue(category, out QuipBucket? bucket))
        {
            _quips.TryGetValue("idle", out bucket);
        }

        List<string>? list = language == QuipLanguage.EgyptianArabic ? bucket?.Ar : bucket?.En;
        if (list is null || list.Count == 0)
        {
            return language == QuipLanguage.EgyptianArabic
                ? "يا عم قوم اشرب مية!"
                : "Hey, don't forget to drink water!";
        }

        if (list.Count == 1)
        {
            return list[0];
        }

        _lastQuipPerCategory.TryGetValue(category, out string? previous);
        string selected = list[_rng.Next(list.Count)];
        for (int attempt = 0; attempt < 4 && selected == previous; attempt++)
        {
            selected = list[_rng.Next(list.Count)];
        }

        _lastQuipPerCategory[category] = selected;
        return selected;
    }

    public async Task<string> GenerateContextQuipAsync(
        string activeWindowTitle,
        AppSettings settings,
        CancellationToken ct = default)
    {
        string category = DetectWindowCategory(activeWindowTitle);

        if (settings.AiProvider != AiProvider.None &&
            !string.IsNullOrWhiteSpace(settings.AiApiKey) &&
            !string.IsNullOrWhiteSpace(activeWindowTitle))
        {
            string langInstruction = settings.Language == QuipLanguage.EgyptianArabic
                ? "Respond in witty, playful Egyptian Arabic dialect (عامية مصرية) in 1 short sentence (max 14 words). No quotes, no hashtags."
                : "Respond in witty, playful English in 1 short sentence (max 15 words). No quotes, no hashtags.";

            string systemPrompt =
                $"You are MiniMe, a tiny cheeky desktop pet sitting on the user's Windows screen. {langInstruction}";
            string userPrompt =
                $"The user's currently active window title is: \"{activeWindowTitle}\". Give a funny, lighthearted reaction or roast.";

            string? aiReply = await CallAiAsync(settings, systemPrompt, userPrompt, ct);
            if (!string.IsNullOrWhiteSpace(aiReply))
            {
                return aiReply.Trim();
            }
        }

        return GetOfflineQuip(category, settings.Language);
    }

    public async Task<string> ChatWithPetAsync(
        string userMessage,
        string activeWindowTitle,
        AppSettings settings,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userMessage))
        {
            return GetOfflineQuip("idle", settings.Language);
        }

        if (settings.AiProvider != AiProvider.None && !string.IsNullOrWhiteSpace(settings.AiApiKey))
        {
            string langInstruction = settings.Language == QuipLanguage.EgyptianArabic
                ? "Reply in friendly, witty Egyptian Arabic (عامية مصرية) in 1-2 short sentences."
                : "Reply in friendly, witty English in 1-2 short sentences.";

            string systemPrompt =
                $"You are MiniMe, a lively tiny desktop pet living on the user's Windows desktop. Current active window: \"{activeWindowTitle}\". {langInstruction}";

            string? aiReply = await CallAiAsync(settings, systemPrompt, userMessage, ct);
            if (!string.IsNullOrWhiteSpace(aiReply))
            {
                return aiReply.Trim();
            }
        }

        // Playful offline fallback when no API key is configured
        return settings.Language == QuipLanguage.EgyptianArabic
            ? "سمعتك! لو عايزني أرغي معاك بذكاء اصطناعي، ضيف مفتاح Gemini أو Groq المجاني في Settings 😉"
            : "Heard you! Add a free Gemini Flash or Groq API key in Settings to unlock full AI chat 😉";
    }

    private static async Task<string?> CallAiAsync(
        AppSettings settings,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        try
        {
            return settings.AiProvider switch
            {
                AiProvider.Gemini => await CallGeminiAsync(settings.AiApiKey.Trim(), settings.AiModel.Trim(), systemPrompt, userPrompt, ct),
                AiProvider.Groq => await CallGroqAsync(settings.AiApiKey.Trim(), settings.AiModel.Trim(), systemPrompt, userPrompt, ct),
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    private static async Task<string?> CallGeminiAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        string effectiveModel = string.IsNullOrWhiteSpace(model) ? "gemini-2.0-flash" : model;
        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(effectiveModel)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var payload = new
        {
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                temperature = 0.9,
                maxOutputTokens = 80
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string body = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out JsonElement content) &&
            content.TryGetProperty("parts", out JsonElement parts) &&
            parts.GetArrayLength() > 0 &&
            parts[0].TryGetProperty("text", out JsonElement textElem))
        {
            return textElem.GetString();
        }

        return null;
    }

    private static async Task<string?> CallGroqAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        CancellationToken ct)
    {
        string effectiveModel = string.IsNullOrWhiteSpace(model) ? "llama-3.3-70b-versatile" : model;
        const string url = "https://api.groq.com/openai/v1/chat/completions";

        var payload = new
        {
            model = effectiveModel,
            temperature = 0.9,
            max_tokens = 80,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        string body = await response.Content.ReadAsStringAsync(ct);
        using JsonDocument doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out JsonElement message) &&
            message.TryGetProperty("content", out JsonElement contentElem))
        {
            return contentElem.GetString();
        }

        return null;
    }
}
