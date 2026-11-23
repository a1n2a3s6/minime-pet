using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using MiniMe.Models;

namespace MiniMe.Engine;

public sealed class VisionResponse
{
    [JsonPropertyName("mood")]
    public string Mood { get; set; } = "helpful";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "suggestion";

    [JsonPropertyName("message")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("fix_or_suggestion")]
    public string? FixOrSuggestion { get; set; }
}

public sealed record ConnectionTestResult(bool Success, long LatencyMs, string StatusMessage);

public sealed class BrainService
{
    private static readonly HttpClient SharedHttpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    private static readonly JsonSerializerOptions JsonCaseInsensitive = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static readonly string[] CodingKeywords =
    [
        "visual studio", "vs code", "code", "pycharm", "rider", "intellij", "webstorm",
        "terminal", "powershell", "cmd.exe", "wsl", "cursor", "neovim", "github"
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
            var parsed = JsonSerializer.Deserialize<Dictionary<string, QuipBucket>>(json, JsonCaseInsensitive);
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
            // Keep safe built-in fallback
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
                : "Hey, don't forget to hydrate!";
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

    private static string CleanApiKey(string rawKey)
    {
        return rawKey.Trim().Trim('"', '\'').Replace("\r", string.Empty).Replace("\n", string.Empty);
    }

    /// <summary>
    /// Live connection & latency test used in Setup Wizard Step 3 and Settings.
    /// Surfaces exact API error messages if the key or model is rejected.
    /// </summary>
    public async Task<ConnectionTestResult> TestConnectionAsync(AppSettings settings, CancellationToken ct = default)
    {
        string cleanKey = CleanApiKey(settings.AiApiKey);
        if (settings.AiProvider == AiProvider.None || string.IsNullOrWhiteSpace(cleanKey))
        {
            return new ConnectionTestResult(false, 0, "Enter a Gemini or Groq API key first.");
        }

        var sw = Stopwatch.StartNew();
        try
        {
            (string? Text, string? Error) result = settings.AiProvider switch
            {
                AiProvider.Gemini => await CallGeminiMultimodalAsync(
                    cleanKey,
                    settings.AiModel.Trim(),
                    "Reply with only the word OK.",
                    "ping",
                    jpegBytes: null,
                    expectJson: false,
                    ct),
                AiProvider.Groq => await CallGroqMultimodalAsync(
                    cleanKey,
                    settings.AiModel.Trim(),
                    "Reply with only the word OK.",
                    "ping",
                    jpegBytes: null,
                    ct),
                _ => (null, "Select Gemini or Groq first.")
            };
            sw.Stop();

            if (!string.IsNullOrWhiteSpace(result.Text))
            {
                return new ConnectionTestResult(true, sw.ElapsedMilliseconds, $"✅ Ready in {sw.ElapsedMilliseconds}ms!");
            }

            string errMsg = !string.IsNullOrWhiteSpace(result.Error)
                ? result.Error
                : "API returned an empty response.";
            return new ConnectionTestResult(false, sw.ElapsedMilliseconds, $"❌ {errMsg}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new ConnectionTestResult(false, sw.ElapsedMilliseconds, $"❌ Connection failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Ultra-fast multimodal screen analysis returning structured JSON (typo fix, bug spot, prediction, or roast).
    /// </summary>
    public async Task<VisionResponse> AnalyzeScreenAsync(
        ScreenSnapshot? snapshot,
        string? userQuestion,
        AppSettings settings,
        bool isAutoWatch,
        CancellationToken ct = default)
    {
        string cleanKey = CleanApiKey(settings.AiApiKey);
        if (settings.AiProvider == AiProvider.None || string.IsNullOrWhiteSpace(cleanKey) || snapshot is null)
        {
            string cat = DetectWindowCategory(snapshot?.ActiveWindowTitle ?? string.Empty);
            string offlineMsg = !string.IsNullOrWhiteSpace(userQuestion)
                ? (settings.Language == QuipLanguage.EgyptianArabic
                    ? "ضيف مفتاح Gemini 2.5 Flash أو Groq في الإعدادات عشان أشوف الشاشة وأرد عليك!"
                    : "Add a Gemini 2.5 Flash or Groq API key in Settings to enable live Screen Vision!")
                : GetOfflineQuip(cat, settings.Language);

            return new VisionResponse
            {
                Mood = "troll",
                Category = "roast",
                Message = offlineMsg
            };
        }

        string systemPrompt = BuildVisionSystemPrompt(settings, isAutoWatch);
        string userPrompt = BuildVisionUserPrompt(snapshot, userQuestion, isAutoWatch);

        try
        {
            (string? Text, string? Error) result = settings.AiProvider switch
            {
                AiProvider.Gemini => await CallGeminiMultimodalAsync(
                    cleanKey,
                    settings.AiModel.Trim(),
                    systemPrompt,
                    userPrompt,
                    snapshot.JpegBytes,
                    expectJson: true,
                    ct),
                AiProvider.Groq => await CallGroqMultimodalAsync(
                    cleanKey,
                    settings.AiModel.Trim(),
                    systemPrompt,
                    userPrompt,
                    snapshot.JpegBytes,
                    ct),
                _ => (null, null)
            };

            VisionResponse? parsed = ParseStructuredResponse(result.Text);
            if (parsed is not null && !string.IsNullOrWhiteSpace(parsed.Message))
            {
                return parsed;
            }
        }
        catch
        {
            // Fall back cleanly to offline quip
        }

        string fallbackCat = DetectWindowCategory(snapshot.ActiveWindowTitle);
        return new VisionResponse
        {
            Mood = "troll",
            Category = "roast",
            Message = GetOfflineQuip(fallbackCat, settings.Language)
        };
    }

    private static string BuildVisionSystemPrompt(AppSettings settings, bool isAutoWatch)
    {
        string lang = settings.Language == QuipLanguage.EgyptianArabic
            ? "Egyptian Arabic dialect (عامية مصرية ذكية ولطيفة)"
            : "crisp, witty English";

        string persona = settings.Personality switch
        {
            PetPersonality.SmartCopilot => "smart, sharp, concise AI copilot who still has warm charm",
            PetPersonality.FullGremlin => "chaotic, hilarious gremlin desktop buddy who roasts hard yet still drops accurate fixes",
            _ => "witty Egyptian troll desktop buddy who catches typos/bugs fast and teases playfully"
        };

        var abilities = new List<string>();
        if (settings.EnableTypoCatcher)
        {
            abilities.Add("spot spelling/grammar typos in Arabic or English text and put the exact corrected phrase in fix_or_suggestion");
        }
        if (settings.EnableCodeBugSpotter)
        {
            abilities.Add("spot code errors, red squiggles, or terminal tracebacks and give a 1-line diagnosis + exact fix in fix_or_suggestion");
        }
        if (settings.EnableReplyPredictor)
        {
            abilities.Add("if an open chat/email/editor is visible, predict a smart next sentence or reply in fix_or_suggestion");
        }

        string abilityText = abilities.Count > 0
            ? string.Join("; ", abilities)
            : "give a witty context roast about what is on screen";

        string autoWatchRule = isAutoWatch
            ? "If nothing needs fixing or suggesting and the screen is plain idle work, set message to empty string \"\"."
            : "Always provide a crisp, high-value response.";

        return
            $"You are {settings.PetName}, a tiny {persona} living on the user's Windows screen. " +
            $"Respond ONLY in valid raw JSON matching this exact schema: " +
            "{\"mood\":\"troll|helpful|alert\",\"category\":\"typo|bug|suggestion|prediction|roast\",\"message\":\"short 1-2 sentence insight in " + lang + "\",\"fix_or_suggestion\":\"exact corrected word/phrase, code fix, or predicted next reply (or null)\"}. " +
            $"Priorities: {abilityText}. If the user is slacking on YouTube/social media with no text/code to fix, roast them playfully. {autoWatchRule}";
    }

    private static string BuildVisionUserPrompt(ScreenSnapshot snapshot, string? userQuestion, bool isAutoWatch)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Active Window Title: \"{snapshot.ActiveWindowTitle}\"");
        if (!string.IsNullOrWhiteSpace(snapshot.FocusedTextSnippet))
        {
            sb.AppendLine($"Locally Focused Text Input Snippet: \"{snapshot.FocusedTextSnippet}\"");
        }

        if (!string.IsNullOrWhiteSpace(userQuestion))
        {
            sb.AppendLine($"User directly asked: \"{userQuestion}\"");
        }
        else if (isAutoWatch)
        {
            sb.AppendLine("Auto-Watch check: inspect the screenshot and only interrupt if you spot a typo, error, helpful reply prediction, or funny slacking moment.");
        }
        else
        {
            sb.AppendLine("Instant Scan: inspect the screenshot right now and give your best typo fix, bug spot, reply prediction, or witty roast.");
        }

        return sb.ToString();
    }

    private static async Task<(string? Text, string? Error)> CallGeminiMultimodalAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        byte[]? jpegBytes,
        bool expectJson,
        CancellationToken ct)
    {
        string effectiveModel = string.IsNullOrWhiteSpace(model) ? "gemini-2.5-flash" : model;
        // Strip optional "models/" prefix if the user pasted it
        if (effectiveModel.StartsWith("models/", StringComparison.OrdinalIgnoreCase))
        {
            effectiveModel = effectiveModel["models/".Length..];
        }

        string url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(effectiveModel)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        var userParts = new List<object> { new { text = userPrompt } };
        if (jpegBytes is not null && jpegBytes.Length > 0)
        {
            userParts.Add(new
            {
                inlineData = new
                {
                    mimeType = "image/jpeg",
                    data = Convert.ToBase64String(jpegBytes)
                }
            });
        }

        // Disable internal thinking budget on Gemini 2.5+ so maxOutputTokens isn't consumed by hidden thoughts
        var genConfig = new Dictionary<string, object>
        {
            ["temperature"] = 0.7,
            ["maxOutputTokens"] = expectJson ? 320 : 64
        };
        if (expectJson)
        {
            genConfig["responseMimeType"] = "application/json";
        }
        if (effectiveModel.Contains("2.5", StringComparison.OrdinalIgnoreCase))
        {
            genConfig["thinkingConfig"] = new { thinkingBudget = 0 };
        }

        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = userParts
                }
            },
            generationConfig = genConfig
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return (null, ExtractApiErrorMessage((int)response.StatusCode, body));
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) &&
            candidates.GetArrayLength() > 0 &&
            candidates[0].TryGetProperty("content", out JsonElement content) &&
            content.TryGetProperty("parts", out JsonElement parts) &&
            parts.GetArrayLength() > 0)
        {
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement textElem))
                {
                    string? text = textElem.GetString();
                    if (!string.IsNullOrWhiteSpace(text))
                    {
                        return (text, null);
                    }
                }
            }
        }

        return (null, "Gemini returned no text parts (try gemini-2.5-flash or gemini-2.0-flash).");
    }

    private static async Task<(string? Text, string? Error)> CallGroqMultimodalAsync(
        string apiKey,
        string model,
        string systemPrompt,
        string userPrompt,
        byte[]? jpegBytes,
        CancellationToken ct)
    {
        string effectiveModel = string.IsNullOrWhiteSpace(model)
            ? "meta-llama/llama-4-scout-17b-16e-instruct"
            : model;
        const string url = "https://api.groq.com/openai/v1/chat/completions";

        object userContent;
        if (jpegBytes is not null && jpegBytes.Length > 0)
        {
            string dataUri = $"data:image/jpeg;base64,{Convert.ToBase64String(jpegBytes)}";
            userContent = new object[]
            {
                new { type = "text", text = userPrompt },
                new { type = "image_url", image_url = new { url = dataUri } }
            };
        }
        else
        {
            userContent = userPrompt;
        }

        // Omit strict response_format=json_object because Groq vision endpoints can reject it on multimodal messages
        var payload = new
        {
            model = effectiveModel,
            temperature = 0.7,
            max_tokens = 256,
            messages = new object[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userContent }
            }
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        using HttpResponseMessage response = await SharedHttpClient.SendAsync(request, ct);
        string body = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            return (null, ExtractApiErrorMessage((int)response.StatusCode, body));
        }

        using JsonDocument doc = JsonDocument.Parse(body);
        if (doc.RootElement.TryGetProperty("choices", out JsonElement choices) &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("message", out JsonElement message) &&
            message.TryGetProperty("content", out JsonElement contentElem))
        {
            return (contentElem.GetString(), null);
        }

        return (null, "Groq returned an empty choices array.");
    }

    private static string ExtractApiErrorMessage(int statusCode, string body)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out JsonElement err))
            {
                if (err.ValueKind == JsonValueKind.Object &&
                    err.TryGetProperty("message", out JsonElement msgElem))
                {
                    string? msg = msgElem.GetString();
                    if (!string.IsNullOrWhiteSpace(msg))
                    {
                        return $"HTTP {statusCode}: {msg}";
                    }
                }
                else if (err.ValueKind == JsonValueKind.String)
                {
                    return $"HTTP {statusCode}: {err.GetString()}";
                }
            }
        }
        catch
        {
            // Ignore JSON parse error on non-JSON error pages
        }

        string snippet = body.Length > 140 ? body[..140] : body;
        return $"HTTP {statusCode}: {snippet}";
    }

    private static VisionResponse? ParseStructuredResponse(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        string cleaned = raw.Trim();
        int firstBrace = cleaned.IndexOf('{');
        int lastBrace = cleaned.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            cleaned = cleaned[firstBrace..(lastBrace + 1)];
        }

        try
        {
            return JsonSerializer.Deserialize<VisionResponse>(cleaned, JsonCaseInsensitive);
        }
        catch
        {
            return new VisionResponse
            {
                Mood = "helpful",
                Category = "suggestion",
                Message = raw.Trim()
            };
        }
    }
}
