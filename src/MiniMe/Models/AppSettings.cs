using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MiniMe.Models;

public enum QuipLanguage
{
    EgyptianArabic,
    English
}

public enum AiProvider
{
    None,
    Gemini,
    Groq
}

public sealed class AppSettings
{
    public string? ActiveSkinPath { get; set; }
    public double ChromaTolerance { get; set; } = 0.30;
    public double PetScale { get; set; } = 1.0;

    public QuipLanguage Language { get; set; } = QuipLanguage.EgyptianArabic;
    public int QuipFrequencySeconds { get; set; } = 30;

    public bool EnableWindowClimber { get; set; } = true;
    public bool EnableWindowPusher { get; set; } = true;
    public bool EnableMouseThief { get; set; } = true;
    public bool EnableSneakyMinimizer { get; set; } = false;

    public AiProvider AiProvider { get; set; } = AiProvider.None;
    public string AiApiKey { get; set; } = string.Empty;
    public string AiModel { get; set; } = "gemini-2.0-flash";

    public AppSettings Clone() => new()
    {
        ActiveSkinPath = ActiveSkinPath,
        ChromaTolerance = ChromaTolerance,
        PetScale = PetScale,
        Language = Language,
        QuipFrequencySeconds = QuipFrequencySeconds,
        EnableWindowClimber = EnableWindowClimber,
        EnableWindowPusher = EnableWindowPusher,
        EnableMouseThief = EnableMouseThief,
        EnableSneakyMinimizer = EnableSneakyMinimizer,
        AiProvider = AiProvider,
        AiApiKey = AiApiKey,
        AiModel = AiModel
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
            loaded.PetScale = Math.Clamp(loaded.PetScale, 0.60, 1.60);
            loaded.QuipFrequencySeconds = Math.Clamp(loaded.QuipFrequencySeconds, 15, 90);
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
