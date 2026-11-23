using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniMe.Engine;
using MiniMe.Models;

namespace MiniMe.Views;

public partial class SetupWizardWindow : Window
{
    private readonly AppSettings _draft;
    private readonly BrainService _brain;
    private readonly DispatcherTimer _poseCycleTimer;

    private int _currentStep = 1;
    private string? _uploadedFilePath;
    private ProcessedSpriteSheet? _processedSheet;
    private BitmapSource[] _previewFrames = [];
    private int _selectedPoseIndex;
    private bool _manualPoseSelected;

    public event Action<AppSettings>? WizardCompleted;

    public SetupWizardWindow(AppSettings currentSettings, BrainService brain)
    {
        InitializeComponent();

        _draft = currentSettings.Clone();
        _brain = brain;

        LoadDraftIntoControls();
        BuildPoseButtons();
        UpdatePromptForSelectedStyle();
        LoadInitialPreviewFrames();

        _poseCycleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(750) };
        _poseCycleTimer.Tick += (_, _) =>
        {
            if (!_manualPoseSelected && _previewFrames.Length == SpriteProcessor.FrameCount)
            {
                _selectedPoseIndex = (_selectedPoseIndex + 1) % SpriteProcessor.FrameCount;
                DisplayPose(_selectedPoseIndex);
            }
        };
        _poseCycleTimer.Start();

        GoToStep(1);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _poseCycleTimer.Stop();
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void CloseChromeBtn_Click(object sender, RoutedEventArgs e) => Close();

    private void LoadDraftIntoControls()
    {
        PetNameTextBox.Text = _draft.PetName;
        LangArabicRadio.IsChecked = _draft.Language == QuipLanguage.EgyptianArabic;
        LangEnglishRadio.IsChecked = _draft.Language == QuipLanguage.English;

        PersonaTrollRadio.IsChecked = _draft.Personality == PetPersonality.EgyptianTroll;
        PersonaCopilotRadio.IsChecked = _draft.Personality == PetPersonality.SmartCopilot;
        PersonaGremlinRadio.IsChecked = _draft.Personality == PetPersonality.FullGremlin;

        ToleranceSlider.Value = _draft.ChromaTolerance;
        ToleranceValueText.Text = _draft.ChromaTolerance.ToString("0.00");
        SpillSlider.Value = _draft.SpillSuppression;
        SpillValueText.Text = _draft.SpillSuppression.ToString("0.00");

        AiGeminiRadio.IsChecked = _draft.AiProvider is AiProvider.Gemini or AiProvider.None;
        AiGroqRadio.IsChecked = _draft.AiProvider == AiProvider.Groq;
        AiOfflineRadio.IsChecked = _draft.AiProvider == AiProvider.None && string.IsNullOrWhiteSpace(_draft.AiApiKey);

        WizApiKeyBox.Password = _draft.AiApiKey;
        WizModelTextBox.Text = string.IsNullOrWhiteSpace(_draft.AiModel) ? "gemini-2.5-flash" : _draft.AiModel;
        WizTypoToggle.IsChecked = _draft.EnableTypoCatcher;
        WizBugToggle.IsChecked = _draft.EnableCodeBugSpotter;
        WizReplyToggle.IsChecked = _draft.EnableReplyPredictor;

        if (!_draft.EnableAutoWatch)
        {
            WatchOffRadio.IsChecked = true;
        }
        else if (_draft.AutoWatchIntervalSeconds == 10)
        {
            Watch10Radio.IsChecked = true;
        }
        else if (_draft.AutoWatchIntervalSeconds == 45)
        {
            Watch45Radio.IsChecked = true;
        }
        else
        {
            Watch20Radio.IsChecked = true;
        }

        ChaosChillRadio.IsChecked = _draft.ChaosLevel == ChaosIntensity.Chill;
        ChaosMischievousRadio.IsChecked = _draft.ChaosLevel == ChaosIntensity.Mischievous;
        ChaosGremlinRadio.IsChecked = _draft.ChaosLevel == ChaosIntensity.FullGremlin;

        WizMouseToggle.IsChecked = _draft.EnableMouseThief;
        WizShoveToggle.IsChecked = _draft.EnableWindowShoveShake;
        WizResizeToggle.IsChecked = _draft.EnableWindowResize;
        WizClimbToggle.IsChecked = _draft.EnableWindowClimber;
        WizMinimizeToggle.IsChecked = _draft.EnableSneakyMinimizer;
        WizUnmaxToggle.IsChecked = _draft.EnableUnmaximizePrank;
    }

    private void LoadInitialPreviewFrames()
    {
        if (!string.IsNullOrWhiteSpace(_draft.ActiveSkinPath) && File.Exists(_draft.ActiveSkinPath))
        {
            _previewFrames = SpriteProcessor.LoadSavedCleanedSkin(_draft.ActiveSkinPath);
            UploadStatusText.Text = $"Loaded skin: {Path.GetFileName(_draft.ActiveSkinPath)}";
        }
        else
        {
            _previewFrames = SpriteProcessor.GenerateProceduralFallbackFrames(144);
        }

        if (_previewFrames.Length > 0)
        {
            BeforeSheetImage.Source = _previewFrames[0];
            AfterSheetImage.Source = _previewFrames[0];
            DisplayPose(0);
        }
    }

    private void BuildPoseButtons()
    {
        PoseButtonsGrid.Children.Clear();
        var btnStyle = (Style)FindResource("GhostButton");

        for (int i = 0; i < SpriteProcessor.FrameCount; i++)
        {
            int poseIdx = i;
            var btn = new Button
            {
                Content = SpriteProcessor.FrameLabels[i],
                Style = btnStyle,
                FontSize = 11.5,
                Padding = new Thickness(6, 5, 6, 5),
                Margin = new Thickness(3)
            };
            btn.Click += (_, _) =>
            {
                _manualPoseSelected = true;
                _selectedPoseIndex = poseIdx;
                DisplayPose(poseIdx);
            };
            PoseButtonsGrid.Children.Add(btn);
        }
    }

    private void DisplayPose(int index)
    {
        if (_previewFrames.Length == 0)
        {
            return;
        }

        int safeIdx = Math.Clamp(index, 0, _previewFrames.Length - 1);
        ActivePoseImage.Source = _previewFrames[safeIdx];
        ActivePoseText.Text = SpriteProcessor.FrameLabels[safeIdx];
    }

    private void GoToStep(int step)
    {
        _currentStep = Math.Clamp(step, 1, 4);

        Step1Panel.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        Step2Panel.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        Step3Panel.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        Step4Panel.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        BackBtn.Visibility = _currentStep > 1 ? Visibility.Visible : Visibility.Collapsed;
        NextBtn.Visibility = _currentStep < 4 ? Visibility.Visible : Visibility.Collapsed;
        LaunchBtn.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;

        UpdateStepBadge(StepBadge1, StepLabel1, 1);
        UpdateStepBadge(StepBadge2, StepLabel2, 2);
        UpdateStepBadge(StepBadge3, StepLabel3, 3);
        UpdateStepBadge(StepBadge4, StepLabel4, 4);
    }

    private void UpdateStepBadge(Border badge, TextBlock label, int stepNum)
    {
        if (stepNum == _currentStep)
        {
            badge.Background = new SolidColorBrush(Color.FromRgb(16, 36, 30));
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(16, 185, 129));
            label.Foreground = new SolidColorBrush(Color.FromRgb(52, 211, 153));
            label.FontWeight = FontWeights.Bold;
        }
        else if (stepNum < _currentStep)
        {
            badge.Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(245, 158, 11));
            label.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
            label.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            badge.Background = new SolidColorBrush(Color.FromRgb(22, 27, 34));
            badge.BorderBrush = new SolidColorBrush(Color.FromRgb(38, 45, 58));
            label.Foreground = new SolidColorBrush(Color.FromRgb(139, 148, 158));
            label.FontWeight = FontWeights.Medium;
        }
    }

    private void BackBtn_Click(object sender, RoutedEventArgs e) => GoToStep(_currentStep - 1);
    private void NextBtn_Click(object sender, RoutedEventArgs e) => GoToStep(_currentStep + 1);

    private void StyleRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (IsLoaded)
        {
            UpdatePromptForSelectedStyle();
        }
    }

    private void UpdatePromptForSelectedStyle()
    {
        string style = "adorable Chibi Anime style, expressive eyes, crisp ink outlines, vibrant cel-shaded colors";
        if (StylePixelRadio?.IsChecked == true)
        {
            style = "crisp 32-bit retro Pixel Art style, sharp pixels, zero blur, charming indie game sprite";
        }
        else if (StyleCartoonRadio?.IsChecked == true)
        {
            style = "expressive 2D modern Cartoon style, bold clean outlines, lively expressions";
        }

        PromptTextBox.Text =
            $"Using my uploaded photo, create a desktop pet sprite sheet in {style}, keeping my exact hair and likeness. " +
            "EXACT evenly-spaced 4 columns × 2 rows grid (8 full-body poses, centered in each cell, no text, no grid lines): " +
            "Row 1: (1) Idle smile, (2) Walk left foot, (3) Walk right foot, (4) Mischievous Troll laugh. " +
            "Row 2: (5) Hit/Dizzy stunned, (6) Dragged by collar dangling legs, (7) Sitting legs dangling, (8) Sleeping curled up. " +
            "CRITICAL: 100% solid flat pure chroma-key green background (#00FF00) with zero shadows and zero green clothes.";
    }

    private void CopyPromptBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PromptTextBox.Text);
            CopyPromptBtn.Content = "✓ Copied!";
        }
        catch
        {
            CopyPromptBtn.Content = "Ctrl+C";
        }
    }

    private void BrowseSpriteBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select 4×2 Green-Screen Sprite Sheet",
            Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) == true)
        {
            LoadUploadedSprite(dialog.FileName);
        }
    }

    private void UploadDropZone_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void UploadDropZone_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] files &&
            files.Length > 0)
        {
            LoadUploadedSprite(files[0]);
        }
    }

    private void LoadUploadedSprite(string filePath)
    {
        _uploadedFilePath = filePath;
        UploadStatusText.Text = $"Loaded: {Path.GetFileName(filePath)}";
        ReprocessUploadedSprite();
    }

    private void UseBuiltInSkinBtn_Click(object sender, RoutedEventArgs e)
    {
        _uploadedFilePath = null;
        _processedSheet = null;
        _draft.ActiveSkinPath = null;
        _previewFrames = SpriteProcessor.GenerateProceduralFallbackFrames(144);
        BeforeSheetImage.Source = _previewFrames[0];
        AfterSheetImage.Source = _previewFrames[0];
        DisplayPose(0);
        UploadStatusText.Text = "Using Built-In Animated Chibi Skin";
    }

    private void ChromaSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToleranceValueText is not null)
        {
            ToleranceValueText.Text = ToleranceSlider.Value.ToString("0.00");
        }
        if (SpillValueText is not null)
        {
            SpillValueText.Text = SpillSlider.Value.ToString("0.00");
        }

        if (IsLoaded && !string.IsNullOrWhiteSpace(_uploadedFilePath))
        {
            ReprocessUploadedSprite();
        }
    }

    private void ReprocessUploadedSprite()
    {
        if (string.IsNullOrWhiteSpace(_uploadedFilePath) || !File.Exists(_uploadedFilePath))
        {
            return;
        }

        try
        {
            _processedSheet = SpriteProcessor.LoadAndProcessSpriteSheet(
                _uploadedFilePath,
                ToleranceSlider.Value,
                SpillSlider.Value);
            _previewFrames = _processedSheet.Frames;
            BeforeSheetImage.Source = _processedSheet.OriginalSheet;
            AfterSheetImage.Source = _processedSheet.CleanedSheet;
            DisplayPose(_selectedPoseIndex);
        }
        catch (Exception ex)
        {
            UploadStatusText.Text = $"Error: {ex.Message}";
        }
    }

    private void WizAiProvider_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }

        if (AiGeminiRadio.IsChecked == true)
        {
            WizModelTextBox.Text = "gemini-2.5-flash";
            WizApiKeyBox.IsEnabled = true;
            WizModelTextBox.IsEnabled = true;
            TestLatencyBtn.IsEnabled = true;
        }
        else if (AiGroqRadio.IsChecked == true)
        {
            WizModelTextBox.Text = "meta-llama/llama-4-scout-17b-16e-instruct";
            WizApiKeyBox.IsEnabled = true;
            WizModelTextBox.IsEnabled = true;
            TestLatencyBtn.IsEnabled = true;
        }
        else
        {
            WizApiKeyBox.IsEnabled = false;
            WizModelTextBox.IsEnabled = false;
            TestLatencyBtn.IsEnabled = false;
        }
    }

    private async void TestLatencyBtn_Click(object sender, RoutedEventArgs e)
    {
        var probeSettings = new AppSettings
        {
            AiProvider = AiGeminiRadio.IsChecked == true ? AiProvider.Gemini : AiGroqRadio.IsChecked == true ? AiProvider.Groq : AiProvider.None,
            AiApiKey = WizApiKeyBox.Password.Trim(),
            AiModel = WizModelTextBox.Text.Trim()
        };

        TestLatencyBtn.IsEnabled = false;
        LatencyResultText.Foreground = new SolidColorBrush(Color.FromRgb(251, 191, 36));
        LatencyResultText.Text = "Testing live vision endpoint...";

        try
        {
            ConnectionTestResult res = await _brain.TestConnectionAsync(probeSettings);
            LatencyResultText.Foreground = res.Success
                ? new SolidColorBrush(Color.FromRgb(52, 211, 153))
                : new SolidColorBrush(Color.FromRgb(248, 113, 113));
            LatencyResultText.Text = res.StatusMessage;
        }
        finally
        {
            TestLatencyBtn.IsEnabled = true;
        }
    }

    private void LaunchBtn_Click(object sender, RoutedEventArgs e)
    {
        _draft.PetName = string.IsNullOrWhiteSpace(PetNameTextBox.Text) ? "MiniMe" : PetNameTextBox.Text.Trim();
        _draft.Language = LangEnglishRadio.IsChecked == true ? QuipLanguage.English : QuipLanguage.EgyptianArabic;
        _draft.Personality = PersonaCopilotRadio.IsChecked == true
            ? PetPersonality.SmartCopilot
            : PersonaGremlinRadio.IsChecked == true
                ? PetPersonality.FullGremlin
                : PetPersonality.EgyptianTroll;

        _draft.ChromaTolerance = Math.Round(ToleranceSlider.Value, 2);
        _draft.SpillSuppression = Math.Round(SpillSlider.Value, 2);

        if (_processedSheet is not null)
        {
            SettingsStore.EnsureDirectories();
            string safeName = $"custom_{DateTime.Now:yyyyMMdd_HHmmss}.png";
            string destPath = Path.Combine(SettingsStore.SkinsDirectory, safeName);
            SpriteProcessor.SaveCleanedSpriteSheet(_processedSheet.CleanedSheet, destPath);
            _draft.ActiveSkinPath = destPath;
        }

        _draft.AiProvider = AiOfflineRadio.IsChecked == true
            ? AiProvider.None
            : AiGroqRadio.IsChecked == true
                ? AiProvider.Groq
                : AiProvider.Gemini;
        _draft.AiApiKey = WizApiKeyBox.Password.Trim();
        _draft.AiModel = WizModelTextBox.Text.Trim();
        _draft.EnableTypoCatcher = WizTypoToggle.IsChecked == true;
        _draft.EnableCodeBugSpotter = WizBugToggle.IsChecked == true;
        _draft.EnableReplyPredictor = WizReplyToggle.IsChecked == true;

        if (WatchOffRadio.IsChecked == true)
        {
            _draft.EnableAutoWatch = false;
        }
        else
        {
            _draft.EnableAutoWatch = true;
            _draft.AutoWatchIntervalSeconds = Watch10Radio.IsChecked == true ? 10 : Watch45Radio.IsChecked == true ? 45 : 20;
        }

        _draft.ChaosLevel = ChaosChillRadio.IsChecked == true
            ? ChaosIntensity.Chill
            : ChaosGremlinRadio.IsChecked == true
                ? ChaosIntensity.FullGremlin
                : ChaosIntensity.Mischievous;

        _draft.EnableMouseThief = WizMouseToggle.IsChecked == true;
        _draft.EnableWindowShoveShake = WizShoveToggle.IsChecked == true;
        _draft.EnableWindowResize = WizResizeToggle.IsChecked == true;
        _draft.EnableWindowClimber = WizClimbToggle.IsChecked == true;
        _draft.EnableSneakyMinimizer = WizMinimizeToggle.IsChecked == true;
        _draft.EnableUnmaximizePrank = WizUnmaxToggle.IsChecked == true;

        WizardCompleted?.Invoke(_draft);
        Close();
    }
}
