using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniMe.Models;

public enum QuipLanguage
{
    EgyptianArabic,
    English
}

public enum PetPersonality
{
    EgyptianTroll,
    SmartCopilot,
    FullGremlin
}

public enum ChaosIntensity
{
    Chill,
    Mischievous,
    FullGremlin
}

public enum AiProvider
{
    None,
    Gemini,
    Groq
}

public sealed class AppSettings
{
    public bool HasCompletedOnboarding { get; set; } = false;
    public string PetName { get; set; } = "MiniMe";
    public PetPersonality Personality { get; set; } = PetPersonality.EgyptianTroll;
    public QuipLanguage Language { get; set; } = QuipLanguage.EgyptianArabic;

    // Skin
    public string? ActiveSkinPath { get; set; }
    public double ChromaTolerance { get; set; } = 0.30;
    public double SpillSuppression { get; set; } = 0.75;
    public double PetScale { get; set; } = 1.0;

    // Chaos Engine
    public ChaosIntensity ChaosLevel { get; set; } = ChaosIntensity.Mischievous;
    public bool EnableMouseThief { get; set; } = true;
    public bool EnableWindowShoveShake { get; set; } = true;
    public bool EnableWindowResize { get; set; } = true;
    public bool EnableSneakyMinimizer { get; set; } = false;
    public bool EnableWindowClimber { get; set; } = true;
    public bool EnableUnmaximizePrank { get; set; } = false;

    // Screen Vision & AI Copilot
    public AiProvider AiProvider { get; set; } = AiProvider.None;
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = "gemini-2.5-flash";
    public bool EnableTypoCatcher { get; set; } = true;
    public bool EnableCodeBugSpotter { get; set; } = true;
    public bool EnableReplyPredictor { get; set; } = true;
    public bool EnableAutoWatch { get; set; } = false;
    public int AutoWatchIntervalSeconds { get; set; } = 20;

    public int GetChaosIntervalSeconds() => ChaosLevel switch
    {
        ChaosIntensity.Chill => 45,
        ChaosIntensity.FullGremlin => 7,
        _ => 18
    };

    public AppSettings Clone() => new()
    {
        HasCompletedOnboarding = HasCompletedOnboarding,
        PetName = PetName,
        Personality = Personality,
        Language = Language,
        ActiveSkinPath = ActiveSkinPath,
        ChromaTolerance = ChromaTolerance,
        SpillSuppression = SpillSuppression,
        PetScale = PetScale,
        ChaosLevel = ChaosLevel,
        EnableMouseThief = EnableMouseThief,
        EnableWindowShoveShake = EnableWindowShoveShake,
        EnableWindowResize = EnableWindowResize,
        EnableSneakyMinimizer = EnableSneakyMinimizer,
        EnableWindowClimber = EnableWindowClimber,
        EnableUnmaximizePrank = EnableUnmaximizePrank,
        AiProvider = AiProvider,
        AiApiKey = AiApiKey,
        AiModel = AiModel,
        EnableTypoCatcher = EnableTypoCatcher,
        EnableCodeBugSpotter = EnableCodeBugSpotter,
        EnableReplyPredictor = EnableReplyPredictor,
        EnableAutoWatch = EnableAutoWatch,
        AutoWatchIntervalSeconds = AutoWatchIntervalSeconds
    };
}

public static class SettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public static string AppDataDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MiniMe");

    public static string SkinsDirectory => Path.Combine(AppDataDirectory, "skins");

    public static string SettingsFilePath => Path.Combine(AppDataDirectory, "settings.json");

    public static void EnsureDirectories()
    {
        Directory.CreateDirectory(AppDataDirectory);
        Directory.CreateDirectory(SkinsDirectory);
    }

    public static AppSettings Load()
    {
        try
        {
            EnsureDirectories();
            if (!File.Exists(SettingsFilePath))
            {
                return new AppSettings();
            }

            string json = File.ReadAllText(SettingsFilePath);
            AppSettings? loaded = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
            if (loaded is null)
            {
                return new AppSettings();
            }

            loaded.ChromaTolerance = Math.Clamp(loaded.ChromaTolerance, 0.10, 0.60);
            loaded.SpillSuppression = Math.Clamp(loaded.SpillSuppression, 0.0, 1.0);
            loaded.PetScale = Math.Clamp(loaded.PetScale, 0.60, 1.60);
            loaded.AutoWatchIntervalSeconds = loaded.AutoWatchIntervalSeconds is 10 or 20 or 45
                ? loaded.AutoWatchIntervalSeconds
                : 20;
            if (string.IsNullOrWhiteSpace(loaded.PetName))
            {
                loaded.PetName = "MiniMe";
            }

            return loaded;
        }
        catch
        {
            return new AppSettings();
        }
    }

    public static void Save(AppSettings settings)
    {
        EnsureDirectories();
        string json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(SettingsFilePath, json);
    }
}
