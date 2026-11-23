using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using MiniMe.Engine;
using MiniMe.Models;

namespace MiniMe.Views;

public partial class SetupWindow : Window
{
    private readonly DispatcherTimer _previewTimer;
    private string? _uploadedFilePath;
    private ProcessedSpriteSheet? _processedSheet;
    private BitmapSource[] _previewFrames = [];
    private int _previewIndex;

    public event Action<string?, double>? SkinActivated;

    public SetupWindow(AppSettings currentSettings)
    {
        InitializeComponent();

        ToleranceSlider.Value = Math.Clamp(currentSettings.ChromaTolerance, 0.10, 0.60);
        ToleranceValueText.Text = ToleranceSlider.Value.ToString("0.00");

        UpdatePromptForSelectedStyle();

        // Load current skin or built-in procedural fallback into live preview immediately
        if (!string.IsNullOrWhiteSpace(currentSettings.ActiveSkinPath) && File.Exists(currentSettings.ActiveSkinPath))
        {
            _previewFrames = SpriteProcessor.LoadSavedCleanedSkin(currentSettings.ActiveSkinPath);
            SelectedFileText.Text = $"Active skin: {Path.GetFileName(currentSettings.ActiveSkinPath)}";
        }
        else
        {
            _previewFrames = SpriteProcessor.GenerateProceduralFallbackFrames(144);
        }

        RenderSlicedGrid(_previewFrames);

        _previewTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(550) };
        _previewTimer.Tick += (_, _) => AdvanceAnimatedPreview();
        _previewTimer.Start();
        AdvanceAnimatedPreview();
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _previewTimer.Stop();
    }

    private void StyleRadio_Checked(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
        {
            return;
        }
        UpdatePromptForSelectedStyle();
    }

    private void UpdatePromptForSelectedStyle()
    {
        string styleDescriptor = "adorable Chibi Anime style, big expressive eyes, crisp clean ink outlines, vibrant cel-shaded colors";
        if (StylePixelRadio?.IsChecked == true)
        {
            styleDescriptor = "crisp 32-bit retro Pixel Art style, clean pixel edges, zero anti-aliasing blur, charming indie game sprite";
        }
        else if (StyleCartoonRadio?.IsChecked == true)
        {
            styleDescriptor = "expressive 2D modern Cartoon style, bold clean outlines, lively facial expressions, Saturday-morning animation look";
        }

        PromptTextBox.Text =
            $"Using the attached reference photo of the person, create a clean desktop pet sprite sheet in {styleDescriptor}, keeping their exact hairstyle, outfit, and likeness. " +
            "Arrange the character in an EXACT evenly-spaced 4 columns × 2 rows grid (8 distinct full-body poses, centered in each cell, never touching cell borders, no text, no labels, no grid lines): " +
            "Row 1: (1) Idle standing friendly smile, (2) Walking pose left foot forward, (3) Walking pose right foot forward, (4) Mischievous trolling laugh grin. " +
            "Row 2: (5) Hit and dizzy stunned face, (6) Being lifted/dragged by the collar with dangling legs, (7) Sitting peacefully with legs dangling forward, (8) Sleeping curled up with closed eyes. " +
            "CRITICAL BACKGROUND RULE: Place ALL 8 sprites on a 100% solid, flat, uniform pure chroma-key green background (#00FF00) with zero shadows, zero gradients, and zero green clothing on the character.";
    }

    private void CopyPromptBtn_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Clipboard.SetText(PromptTextBox.Text);
            CopyStatusText.Text = "Copied to clipboard ✓";
        }
        catch
        {
            CopyStatusText.Text = "Select & press Ctrl+C to copy";
        }
    }

    private void BrowseImageBtn_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select 4×2 Green-Screen Sprite Sheet",
            Filter = "Image Files (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg|All Files (*.*)|*.*"
        };

        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        _uploadedFilePath = dialog.FileName;
        SelectedFileText.Text = Path.GetFileName(_uploadedFilePath);

        string suggestedName = Path.GetFileNameWithoutExtension(_uploadedFilePath);
        if (!string.IsNullOrWhiteSpace(suggestedName))
        {
            SkinNameTextBox.Text = suggestedName;
        }

        ReprocessUploadedSprite();
    }

    private void ToleranceSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ToleranceValueText is not null)
        {
            ToleranceValueText.Text = e.NewValue.ToString("0.00");
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
            _processedSheet = SpriteProcessor.LoadAndProcessSpriteSheet(_uploadedFilePath, ToleranceSlider.Value);
            _previewFrames = _processedSheet.Frames;
            RenderSlicedGrid(_previewFrames);
            SaveActivateBtn.IsEnabled = true;
            FooterStatusText.Text = $"Chroma-keyed cleanly (tolerance {ToleranceSlider.Value:0.00}). Ready to save & activate!";
        }
        catch (Exception ex)
        {
            SaveActivateBtn.IsEnabled = false;
            FooterStatusText.Text = $"Could not load image: {ex.Message}";
        }
    }

    private void RenderSlicedGrid(BitmapSource[] frames)
    {
        SlicedFramesGrid.Children.Clear();
        Brush checker = (Brush)FindResource("CheckerboardBrush");
        Brush borderBrush = (Brush)FindResource("BorderSubtle");
        Brush textSecondary = (Brush)FindResource("TextSecondary");

        for (int i = 0; i < Math.Min(frames.Length, SpriteProcessor.FrameCount); i++)
        {
            var cell = new Border
            {
                Background = checker,
                BorderBrush = borderBrush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(7),
                Margin = new Thickness(4),
                Padding = new Thickness(4)
            };

            var stack = new Grid();
            stack.RowDefinitions.Add(new RowDefinition { Height = new GridLength(74) });
            stack.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var img = new Image
            {
                Source = frames[i],
                Stretch = Stretch.Uniform
            };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            Grid.SetRow(img, 0);

            var label = new TextBlock
            {
                Text = SpriteProcessor.FrameLabels[i],
                FontSize = 10.5,
                Foreground = textSecondary,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            };
            Grid.SetRow(label, 1);

            stack.Children.Add(img);
            stack.Children.Add(label);
            cell.Child = stack;
            SlicedFramesGrid.Children.Add(cell);
        }
    }

    private void AdvanceAnimatedPreview()
    {
        if (_previewFrames.Length == 0)
        {
            return;
        }

        _previewIndex %= _previewFrames.Length;
        AnimatedPreviewImage.Source = _previewFrames[_previewIndex];
        AnimatedPoseLabel.Text = SpriteProcessor.FrameLabels[_previewIndex];
        _previewIndex++;
    }

    private void SaveActivateBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_processedSheet is null)
        {
            return;
        }

        SettingsStore.EnsureDirectories();
        string rawName = SkinNameTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(rawName))
        {
            rawName = $"skin_{DateTime.Now:yyyyMMdd_HHmmss}";
        }

        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            rawName = rawName.Replace(invalid, '_');
        }

        string destinationPath = Path.Combine(SettingsStore.SkinsDirectory, $"{rawName}.png");
        SpriteProcessor.SaveCleanedSpriteSheet(_processedSheet.CleanedSheet, destinationPath);

        SkinActivated?.Invoke(destinationPath, ToleranceSlider.Value);
        FooterStatusText.Text = $"Saved & activated \"{rawName}.png\"!";
    }

    private void ResetDefaultBtn_Click(object sender, RoutedEventArgs e)
    {
        _uploadedFilePath = null;
        _processedSheet = null;
        _previewFrames = SpriteProcessor.GenerateProceduralFallbackFrames(144);
        RenderSlicedGrid(_previewFrames);
        SaveActivateBtn.IsEnabled = false;
        SelectedFileText.Text = "Active skin: Built-In Procedural Chibi";
        SkinActivated?.Invoke(null, ToleranceSlider.Value);
        FooterStatusText.Text = "Switched back to the built-in procedural Chibi skin.";
    }

    private void CloseBtn_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
