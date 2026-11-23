using System.Windows;
using MiniMe.Models;

namespace MiniMe.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _workingCopy;

    public event Action<AppSettings>? SettingsSaved;
    public event Action? BossModeToggleRequested;

    public SettingsWindow(AppSettings currentSettings, bool isBossMode)
    {
        InitializeComponent();

        _workingCopy = currentSettings.Clone();

        WindowClimberToggle.IsChecked = _workingCopy.EnableWindowClimber;
        WindowPusherToggle.IsChecked = _workingCopy.EnableWindowPusher;
        MouseThiefToggle.IsChecked = _workingCopy.EnableMouseThief;
        SneakyMinimizerToggle.IsChecked = _workingCopy.EnableSneakyMinimizer;

        PetScaleSlider.Value = _workingCopy.PetScale;
        ScaleValueText.Text = $"{_workingCopy.PetScale:0.0}×";

        LangArabicRadio.IsChecked = _workingCopy.Language == QuipLanguage.EgyptianArabic;
        LangEnglishRadio.IsChecked = _workingCopy.Language == QuipLanguage.English;

        FrequencySlider.Value = _workingCopy.QuipFrequencySeconds;
        FreqValueText.Text = $"{_workingCopy.QuipFrequencySeconds}s";

        AiNoneRadio.IsChecked = _workingCopy.AiProvider == AiProvider.None;
        AiGeminiRadio.IsChecked = _workingCopy.AiProvider == AiProvider.Gemini;
        AiGroqRadio.IsChecked = _workingCopy.AiProvider == AiProvider.Groq;

        ApiKeyPasswordBox.Password = _workingCopy.AiApiKey;
        PopulateModelSuggestions(_workingCopy.AiProvider, _workingCopy.AiModel);

        SyncBossModeState(isBossMode);
    }

    public void SyncBossModeState(bool isBossMode)
    {
        BossModeBtn.Content = isBossMode ? "Exit Boss Mode" : "Enable Boss Mode";
    }

    private void PetScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleValueText is not null)
        {
            ScaleValueText.Text = $"{e.NewValue:0.0}×";
        }
    }

    private void FrequencySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FreqValueText is not null)
        {
            FreqValueText.Text = $"{(int)Math.Round(e.NewValue)}s";
        }
    }

    private void AiProviderRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        AiProvider selected = GetSelectedProvider();
        PopulateModelSuggestions(selected, null);
    }

    private AiProvider GetSelectedProvider()
    {
        if (AiGeminiRadio.IsChecked == true)
        {
            return AiProvider.Gemini;
        }
        if (AiGroqRadio.IsChecked == true)
        {
            return AiProvider.Groq;
        }
        return AiProvider.None;
    }

    private void PopulateModelSuggestions(AiProvider provider, string? existingModel)
    {
        ModelComboBox.Items.Clear();

        string[] suggestions = provider switch
        {
            AiProvider.Gemini => ["gemini-2.0-flash", "gemini-2.5-flash", "gemini-1.5-flash"],
            AiProvider.Groq => ["llama-3.3-70b-versatile", "llama-3.1-8b-instant", "gemma2-9b-it"],
            _ => ["gemini-2.0-flash", "llama-3.3-70b-versatile"]
        };

        foreach (string item in suggestions)
        {
            ModelComboBox.Items.Add(item);
        }

        ModelComboBox.Text = !string.IsNullOrWhiteSpace(existingModel)
            ? existingModel
            : suggestions[0];

        bool aiEnabled = provider != AiProvider.None;
        ApiKeyPasswordBox.IsEnabled = aiEnabled;
        ModelComboBox.IsEnabled = aiEnabled;
    }

    private void BossModeBtn_Click(object sender, RoutedEventArgs e)
    {
        BossModeToggleRequested?.Invoke();
    }

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _workingCopy.EnableWindowClimber = WindowClimberToggle.IsChecked == true;
        _workingCopy.EnableWindowPusher = WindowPusherToggle.IsChecked == true;
        _workingCopy.EnableMouseThief = MouseThiefToggle.IsChecked == true;
        _workingCopy.EnableSneakyMinimizer = SneakyMinimizerToggle.IsChecked == true;
        _workingCopy.PetScale = Math.Round(PetScaleSlider.Value, 2);

        _workingCopy.Language = LangEnglishRadio.IsChecked == true
            ? QuipLanguage.English
            : QuipLanguage.EgyptianArabic;
        _workingCopy.QuipFrequencySeconds = (int)Math.Round(FrequencySlider.Value);

        _workingCopy.AiProvider = GetSelectedProvider();
        _workingCopy.AiApiKey = ApiKeyPasswordBox.Password.Trim();
        _workingCopy.AiModel = ModelComboBox.Text.Trim();

        SettingsSaved?.Invoke(_workingCopy);
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
