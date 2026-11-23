using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using MiniMe.Engine;
using MiniMe.Models;

namespace MiniMe.Views;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _workingCopy;
    private readonly BrainService _brain;

    public event Action<AppSettings>? SettingsSaved;
    public event Action? BossModeToggleRequested;

    public SettingsWindow(AppSettings currentSettings, BrainService brain, bool isBossMode)
    {
        InitializeComponent();

        _workingCopy = currentSettings.Clone();
        _brain = brain;

        ChillRadio.IsChecked = _workingCopy.ChaosLevel == ChaosIntensity.Chill;
        MischievousRadio.IsChecked = _workingCopy.ChaosLevel == ChaosIntensity.Mischievous;
        GremlinRadio.IsChecked = _workingCopy.ChaosLevel == ChaosIntensity.FullGremlin;

        MouseThiefToggle.IsChecked = _workingCopy.EnableMouseThief;
        WindowShoveToggle.IsChecked = _workingCopy.EnableWindowShoveShake;
        WindowResizeToggle.IsChecked = _workingCopy.EnableWindowResize;
        WindowClimberToggle.IsChecked = _workingCopy.EnableWindowClimber;
        SneakyMinimizerToggle.IsChecked = _workingCopy.EnableSneakyMinimizer;
        UnmaximizeToggle.IsChecked = _workingCopy.EnableUnmaximizePrank;

        PetScaleSlider.Value = _workingCopy.PetScale;
        ScaleValueText.Text = $"{_workingCopy.PetScale:0.0}×";

        AiNoneRadio.IsChecked = _workingCopy.AiProvider == AiProvider.None;
        AiGeminiRadio.IsChecked = _workingCopy.AiProvider == AiProvider.Gemini;
        AiGroqRadio.IsChecked = _workingCopy.AiProvider == AiProvider.Groq;
        ApiKeyPasswordBox.Password = _workingCopy.AiApiKey;
        ModelTextBox.Text = _workingCopy.AiModel;

        TypoToggle.IsChecked = _workingCopy.EnableTypoCatcher;
        BugToggle.IsChecked = _workingCopy.EnableCodeBugSpotter;
        ReplyToggle.IsChecked = _workingCopy.EnableReplyPredictor;

        if (!_workingCopy.EnableAutoWatch)
        {
            WatchOffRadio.IsChecked = true;
        }
        else if (_workingCopy.AutoWatchIntervalSeconds == 10)
        {
            Watch10Radio.IsChecked = true;
        }
        else if (_workingCopy.AutoWatchIntervalSeconds == 45)
        {
            Watch45Radio.IsChecked = true;
        }
        else
        {
            Watch20Radio.IsChecked = true;
        }

        LangArabicRadio.IsChecked = _workingCopy.Language == QuipLanguage.EgyptianArabic;
        LangEnglishRadio.IsChecked = _workingCopy.Language == QuipLanguage.English;

        SyncBossModeState(isBossMode);
    }

    public void SyncBossModeState(bool isBossMode)
    {
        BossModeBtn.Content = isBossMode ? "Exit Boss Mode" : "Enable Boss Mode";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void PetScaleSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ScaleValueText is not null)
        {
            ScaleValueText.Text = $"{e.NewValue:0.0}×";
        }
    }

    private void AiProvider_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (AiGeminiRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(ModelTextBox.Text) || ModelTextBox.Text.Contains("llama", StringComparison.OrdinalIgnoreCase))
            {
                ModelTextBox.Text = "gemini-2.5-flash";
            }
            ApiKeyPasswordBox.IsEnabled = true;
            ModelTextBox.IsEnabled = true;
            TestBtn.IsEnabled = true;
        }
        else if (AiGroqRadio.IsChecked == true)
        {
            if (string.IsNullOrWhiteSpace(ModelTextBox.Text) || ModelTextBox.Text.Contains("gemini", StringComparison.OrdinalIgnoreCase))
            {
                ModelTextBox.Text = "meta-llama/llama-4-scout-17b-16e-instruct";
            }
            ApiKeyPasswordBox.IsEnabled = true;
            ModelTextBox.IsEnabled = true;
            TestBtn.IsEnabled = true;
        }
        else
        {
            ApiKeyPasswordBox.IsEnabled = false;
            ModelTextBox.IsEnabled = false;
            TestBtn.IsEnabled = false;
        }
    }

    private async void TestBtn_Click(object sender, RoutedEventArgs e)
    {
        var probe = new AppSettings
        {
            AiProvider = AiGeminiRadio.IsChecked == true ? AiProvider.Gemini : AiGroqRadio.IsChecked == true ? AiProvider.Groq : AiProvider.None,
            AiApiKey = ApiKeyPasswordBox.Password.Trim(),
            AiModel = ModelTextBox.Text.Trim()
        };

        TestBtn.IsEnabled = false;
        TestStatusText.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        TestStatusText.Text = "Pinging API...";

        try
        {
            ConnectionTestResult res = await _brain.TestConnectionAsync(probe);
            TestStatusText.Foreground = res.Success
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
            TestStatusText.Text = res.StatusMessage;
        }
        finally
        {
            TestBtn.IsEnabled = true;
        }
    }

    private void BossModeBtn_Click(object sender, RoutedEventArgs e) => BossModeToggleRequested?.Invoke();

    private void SaveBtn_Click(object sender, RoutedEventArgs e)
    {
        _workingCopy.ChaosLevel = ChillRadio.IsChecked == true
            ? ChaosIntensity.Chill
            : GremlinRadio.IsChecked == true
                ? ChaosIntensity.FullGremlin
                : ChaosIntensity.Mischievous;

        _workingCopy.EnableMouseThief = MouseThiefToggle.IsChecked == true;
        _workingCopy.EnableWindowShoveShake = WindowShoveToggle.IsChecked == true;
        _workingCopy.EnableWindowResize = WindowResizeToggle.IsChecked == true;
        _workingCopy.EnableWindowClimber = WindowClimberToggle.IsChecked == true;
        _workingCopy.EnableSneakyMinimizer = SneakyMinimizerToggle.IsChecked == true;
        _workingCopy.EnableUnmaximizePrank = UnmaximizeToggle.IsChecked == true;
        _workingCopy.PetScale = Math.Round(PetScaleSlider.Value, 2);

        _workingCopy.AiProvider = AiGeminiRadio.IsChecked == true
            ? AiProvider.Gemini
            : AiGroqRadio.IsChecked == true
                ? AiProvider.Groq
                : AiProvider.None;
        _workingCopy.AiApiKey = ApiKeyPasswordBox.Password.Trim();
        _workingCopy.AiModel = ModelTextBox.Text.Trim();
        _workingCopy.EnableTypoCatcher = TypoToggle.IsChecked == true;
        _workingCopy.EnableCodeBugSpotter = BugToggle.IsChecked == true;
        _workingCopy.EnableReplyPredictor = ReplyToggle.IsChecked == true;

        if (WatchOffRadio.IsChecked == true)
        {
            _workingCopy.EnableAutoWatch = false;
        }
        else
        {
            _workingCopy.EnableAutoWatch = true;
            _workingCopy.AutoWatchIntervalSeconds = Watch10Radio.IsChecked == true ? 10 : Watch45Radio.IsChecked == true ? 45 : 20;
        }

        _workingCopy.Language = LangEnglishRadio.IsChecked == true
            ? QuipLanguage.English
            : QuipLanguage.EgyptianArabic;

        SettingsSaved?.Invoke(_workingCopy);
        Close();
    }

    private void CancelBtn_Click(object sender, RoutedEventArgs e) => Close();
}
