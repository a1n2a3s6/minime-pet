using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using MiniMe.Engine;
using MiniMe.Models;
using MiniMe.Native;

namespace MiniMe.Views;

public partial class PetWindow : Window
{
    private const double BasePetSize = 128.0;
    private const double WindowHorizontalPadding = 106.0;
    private const double WindowTopSpeechReserve = 236.0;

    private AppSettings _settings;
    private readonly BrainService _brain;
    private readonly ScreenVisionService _vision;
    private readonly PhysicsEngine _physics;
    private readonly ChaosEngine _chaos;
    private readonly Stopwatch _frameClock = new();
    private readonly DispatcherTimer _speechHideTimer;
    private readonly DispatcherTimer _autoWatchTimer;

    private BitmapSource[] _frames = [];
    private SpriteFrame _renderedFrame = (SpriteFrame)(-1);

    private HwndSource? _hwndSource;
    private bool _isBossMode;
    private bool _isPointerDown;
    private bool _isDragging;
    private Point _pointerDownScreenDip;
    private double _lastDragTimeSec;
    private bool _visionRequestInFlight;
    private string? _currentFixText;

    public event Action? OpenWizardRequested;
    public event Action? OpenSettingsRequested;
    public event Action? BossModeToggleRequested;
    public event Action? ExitRequested;

    public PetWindow(AppSettings settings, BrainService brain, ScreenVisionService vision)
    {
        InitializeComponent();

        _settings = settings;
        _brain = brain;
        _vision = vision;
        _physics = new PhysicsEngine();
        _chaos = new ChaosEngine(_physics);

        _physics.OnLandedOnWindow += () =>
        {
            if (!_isBossMode)
            {
                ShowSimpleBubble("😈 ROAST", _brain.GetOfflineQuip("climb", _settings.Language), isHelpful: false, durationSeconds: 4.5);
            }
        };

        _chaos.OnChaosQuipRequested += category =>
        {
            if (!_isBossMode)
            {
                ShowSimpleBubble("😈 CHAOS", _brain.GetOfflineQuip(category, _settings.Language), isHelpful: false, durationSeconds: 4.5);
            }
        };

        _speechHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(6.0) };
        _speechHideTimer.Tick += (_, _) =>
        {
            _speechHideTimer.Stop();
            SpeechBubbleContainer.Visibility = Visibility.Collapsed;
        };

        _autoWatchTimer = new DispatcherTimer();
        _autoWatchTimer.Tick += AutoWatchTimer_Tick;

        ReloadSkin(_settings);
        ApplySettings(_settings);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RegisterGlobalHotkeys();

        _physics.InitializeAtCenter(SystemParameters.WorkArea);
        SyncWindowPosition();

        _frameClock.Restart();
        CompositionTarget.Rendering += OnCompositionRendering;
        SyncAutoWatchTimer();

        string greeting = _settings.Language == QuipLanguage.EgyptianArabic
            ? $"أهلاً! أنا {_settings.PetName} — دوس Ctrl+Shift+S أفحصلك الشاشة أو دبل كليك نتكلم!"
            : $"Hey! I'm {_settings.PetName} — press Ctrl+Shift+S to scan your screen or double-click to ask!";
        ShowSimpleBubble("💡 READY", greeting, isHelpful: true, durationSeconds: 6.5);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionRendering;
        _autoWatchTimer.Stop();
        _speechHideTimer.Stop();
        UnregisterGlobalHotkeys();
    }

    public void ReloadSkin(AppSettings settings)
    {
        _settings = settings;
        try
        {
            if (!string.IsNullOrWhiteSpace(settings.ActiveSkinPath) && File.Exists(settings.ActiveSkinPath))
            {
                _frames = SpriteProcessor.LoadSavedCleanedSkin(settings.ActiveSkinPath);
            }
            else
            {
                _frames = SpriteProcessor.GenerateProceduralFallbackFrames(144);
            }
        }
        catch
        {
            _frames = SpriteProcessor.GenerateProceduralFallbackFrames(144);
        }

        _renderedFrame = (SpriteFrame)(-1);
        UpdateSpriteFrame(SpriteFrame.Idle);
    }

    public void ApplySettings(AppSettings updated)
    {
        _settings = updated;
        ApplyScale(_settings.PetScale);
        _chaos.ResetCooldown(_settings);
        SyncAutoWatchTimer();
    }

    private void SyncAutoWatchTimer()
    {
        _autoWatchTimer.Stop();
        int intervalSec = _settings.AutoWatchIntervalSeconds is 10 or 20 or 45
            ? _settings.AutoWatchIntervalSeconds
            : 20;
        _autoWatchTimer.Interval = TimeSpan.FromSeconds(intervalSec);

        if (!_isBossMode)
        {
            _autoWatchTimer.Start();
        }
    }

    public void SetBossMode(bool enabled)
    {
        _isBossMode = enabled;
        if (enabled)
        {
            _chaos.AbortAll();
            _autoWatchTimer.Stop();
            MiniChatPanel.Visibility = Visibility.Collapsed;
            SpeechBubbleContainer.Visibility = Visibility.Collapsed;
            Hide();
        }
        else
        {
            Show();
            Topmost = true;
            SyncAutoWatchTimer();
            string backMsg = _settings.Language == QuipLanguage.EgyptianArabic
                ? "رجعنا تاني! المدير مشي؟ 👀"
                : "We're back! Coast is clear? 👀";
            ShowSimpleBubble("😈 BACK", backMsg, isHelpful: false, durationSeconds: 4.0);
        }
    }

    public void SummonToCenter()
    {
        _chaos.AbortAll();
        _physics.SummonToCenter(SystemParameters.WorkArea);
        SyncWindowPosition();
    }

    public void TriggerChaosPrank(ChaosPrankType prank)
    {
        if (_isBossMode)
        {
            return;
        }

        (double dpiX, double dpiY) = GetDpiScale();
        bool started = _chaos.TriggerPrankNow(prank, _settings, dpiX, dpiY);
        if (!started)
        {
            string msg = _settings.Language == QuipLanguage.EgyptianArabic
                ? "افتح نافذة عادية (مش Fullscreen) عشان أعمل المقلب ده!"
                : "Open a regular non-maximized window so I can pull off that prank!";
            ShowSimpleBubble("😈 CHAOS", msg, isHelpful: false, durationSeconds: 4.0);
        }
    }

    public void TriggerInstantScreenScan()
    {
        if (_isBossMode)
        {
            return;
        }
        _ = RunScreenVisionAsync(userQuestion: null, isAutoWatch: false);
    }

    private void ApplyScale(double scale)
    {
        double clamped = Math.Clamp(scale, 0.60, 1.60);
        double size = BasePetSize * clamped;
        _physics.PetWidth = size;
        _physics.PetHeight = size;
        PetHost.Width = size;
        PetHost.Height = size;
        Width = Math.Max(340.0, size + (WindowHorizontalPadding * 2.0));
        Height = size + WindowTopSpeechReserve;
    }

    private void OnCompositionRendering(object? sender, EventArgs e)
    {
        if (_isBossMode)
        {
            return;
        }

        double dt = _frameClock.Elapsed.TotalSeconds;
        _frameClock.Restart();
        if (dt <= 0.0 || dt > 0.25)
        {
            dt = 1.0 / 60.0;
        }

        (double dpiX, double dpiY) = GetDpiScale();
        Rect workArea = SystemParameters.WorkArea;

        _chaos.Update(dt, workArea, dpiX, dpiY, _settings, _isBossMode);
        _physics.Update(dt, workArea, dpiX, dpiY);

        UpdateSpriteFrame(_physics.CurrentFrame);
        PetScaleTransform.ScaleX = _physics.FacingRight ? 1.0 : -1.0;
        PetScaleTransform.ScaleY = _physics.BreathScaleY;
        PetRotateTransform.Angle = _physics.TiltAngle;

        UpdateDizzyStars();
        SyncWindowPosition();
    }

    private void UpdateSpriteFrame(SpriteFrame frame)
    {
        if (_frames.Length != SpriteProcessor.FrameCount || frame == _renderedFrame)
        {
            return;
        }

        _renderedFrame = frame;
        PetImage.Source = _frames[(int)frame];
    }

    private void UpdateDizzyStars()
    {
        if (!_physics.IsDizzy)
        {
            if (DizzyStarsCanvas.Visibility != Visibility.Collapsed)
            {
                DizzyStarsCanvas.Visibility = Visibility.Collapsed;
            }
            return;
        }

        if (DizzyStarsCanvas.Visibility != Visibility.Visible)
        {
            DizzyStarsCanvas.Visibility = Visibility.Visible;
        }

        double centerX = (PetHost.Width * 0.5) - 7.0;
        double centerY = 14.0;
        double rx = PetHost.Width * 0.30;
        double ry = 9.0;

        PositionStar(DizzyStar1, _physics.DizzyOrbitAngle, centerX, centerY, rx, ry);
        PositionStar(DizzyStar2, _physics.DizzyOrbitAngle + 120.0, centerX, centerY, rx, ry);
        PositionStar(DizzyStar3, _physics.DizzyOrbitAngle + 240.0, centerX, centerY, rx, ry);
    }

    private static void PositionStar(UIElement star, double angleDeg, double cx, double cy, double rx, double ry)
    {
        double rad = angleDeg * Math.PI / 180.0;
        Canvas.SetLeft(star, cx + Math.Cos(rad) * rx);
        Canvas.SetTop(star, cy + Math.Sin(rad) * ry);
    }

    private void SyncWindowPosition()
    {
        double petOffsetX = (Width - _physics.PetWidth) * 0.5;
        double petOffsetY = Height - _physics.PetHeight;

        Left = _physics.X - petOffsetX;
        Top = _physics.Y - petOffsetY;
    }

    private (double DpiX, double DpiY) GetDpiScale()
    {
        PresentationSource? source = PresentationSource.FromVisual(this);
        if (source?.CompositionTarget is not null)
        {
            return (source.CompositionTarget.TransformToDevice.M11, source.CompositionTarget.TransformToDevice.M22);
        }
        return (1.0, 1.0);
    }

    private Point GetCursorDipPosition()
    {
        if (Win32Interop.GetCursorPos(out Win32Interop.POINT pt))
        {
            (double dpiX, double dpiY) = GetDpiScale();
            return new Point(pt.X / dpiX, pt.Y / dpiY);
        }
        return new Point(_physics.X, _physics.Y);
    }

    private void PetHost_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _chaos.AbortAll();

        if (e.ClickCount >= 2)
        {
            _isPointerDown = false;
            _isDragging = false;
            ToggleMiniChat(true);
            e.Handled = true;
            return;
        }

        _isPointerDown = true;
        _isDragging = false;
        _pointerDownScreenDip = GetCursorDipPosition();
        _lastDragTimeSec = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        PetHost.CaptureMouse();
    }

    private void PetHost_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isPointerDown)
        {
            return;
        }

        Point cursorDip = GetCursorDipPosition();
        double nowSec = Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;
        double dt = Math.Max(0.004, nowSec - _lastDragTimeSec);
        _lastDragTimeSec = nowSec;

        if (!_isDragging)
        {
            double dist = (cursorDip - _pointerDownScreenDip).Length;
            if (dist >= 6.0)
            {
                _isDragging = true;
                MiniChatPanel.Visibility = Visibility.Collapsed;
                _physics.BeginDrag(cursorDip);
                ShowSimpleBubble("😈 DRAGGED", _brain.GetOfflineQuip("drag", _settings.Language), isHelpful: false, durationSeconds: 3.0);
            }
        }

        if (_isDragging)
        {
            _physics.UpdateDrag(cursorDip, dt);
        }
    }

    private void PetHost_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isPointerDown)
        {
            return;
        }

        _isPointerDown = false;
        PetHost.ReleaseMouseCapture();

        if (_isDragging)
        {
            _isDragging = false;
            _physics.EndDrag();
        }
        else
        {
            Point localPos = e.GetPosition(PetHost);
            _physics.TriggerSlap(localPos.X);
            ShowSimpleBubble("💫 OUCH!", _brain.GetOfflineQuip("slap", _settings.Language), isHelpful: false, durationSeconds: 4.5);
        }
    }

    private async void AutoWatchTimer_Tick(object? sender, EventArgs e)
    {
        if (_isBossMode || _visionRequestInFlight || MiniChatPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        if (_settings.EnableAutoWatch && _settings.AiProvider != AiProvider.None && !string.IsNullOrWhiteSpace(_settings.AiApiKey))
        {
            await RunScreenVisionAsync(userQuestion: null, isAutoWatch: true);
        }
        else
        {
            // Light offline context quip when Auto-Watch AI is off
            string title = Win32Interop.GetActiveWindowTitle();
            string cat = _brain.DetectWindowCategory(title);
            ShowSimpleBubble("😈 ROAST", _brain.GetOfflineQuip(cat, _settings.Language), isHelpful: false, durationSeconds: 5.0);
        }
    }

    private async Task RunScreenVisionAsync(string? userQuestion, bool isAutoWatch)
    {
        if (_visionRequestInFlight || _isBossMode)
        {
            return;
        }

        _visionRequestInFlight = true;
        try
        {
            ScreenSnapshot? snapshot = await _vision.CaptureAsync(includeFocusedText: true);
            if (snapshot is null)
            {
                return;
            }

            // Skip API call during Auto-Watch if screen pixels haven't changed!
            if (isAutoWatch && !_vision.HasScreenChangedSinceLastAutoWatch(snapshot.PerceptualHash))
            {
                return;
            }

            if (!isAutoWatch)
            {
                string scanning = _settings.Language == QuipLanguage.EgyptianArabic
                    ? "ببص على الشاشة..."
                    : "Scanning your screen...";
                ShowSimpleBubble("⚡ SCANNING", scanning, isHelpful: true, durationSeconds: 10.0);
            }

            VisionResponse result = await _brain.AnalyzeScreenAsync(snapshot, userQuestion, _settings, isAutoWatch);
            if (!_isBossMode && !string.IsNullOrWhiteSpace(result.Message))
            {
                ShowVisionBubble(result);
            }
        }
        finally
        {
            _visionRequestInFlight = false;
        }
    }

    public void ShowVisionBubble(VisionResponse response)
    {
        string catLower = (response.Category ?? "suggestion").Trim().ToLowerInvariant();
        (string badgeText, Color accent, Color badgeBg) = catLower switch
        {
            "typo" => ("✍️ TYPO FIX", Color.FromRgb(16, 185, 129), Color.FromRgb(16, 42, 34)),
            "bug" => ("🐛 BUG SPOT", Color.FromRgb(239, 68, 68), Color.FromRgb(48, 20, 24)),
            "prediction" => ("🔮 NEXT LINE", Color.FromRgb(16, 185, 129), Color.FromRgb(16, 42, 34)),
            "roast" => ("😈 ROAST", Color.FromRgb(245, 158, 11), Color.FromRgb(42, 31, 14)),
            _ => ("💡 SUGGESTION", Color.FromRgb(16, 185, 129), Color.FromRgb(16, 42, 34))
        };

        ApplyBubbleStyle(badgeText, accent, badgeBg, response.Message, response.FixOrSuggestion, durationSeconds: 9.0);
    }

    public void ShowSimpleBubble(string badgeText, string message, bool isHelpful, double durationSeconds = 5.5)
    {
        Color accent = isHelpful ? Color.FromRgb(16, 185, 129) : Color.FromRgb(245, 158, 11);
        Color badgeBg = isHelpful ? Color.FromRgb(16, 42, 34) : Color.FromRgb(42, 31, 14);
        ApplyBubbleStyle(badgeText, accent, badgeBg, message, null, durationSeconds);
    }

    private void ApplyBubbleStyle(
        string badgeText,
        Color accentColor,
        Color badgeBgColor,
        string message,
        string? fixOrSuggestion,
        double durationSeconds)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        var accentBrush = new SolidColorBrush(accentColor);
        SpeechBubbleBorder.BorderBrush = accentBrush;
        SpeechTail.Fill = accentBrush;
        CategoryBadge.BorderBrush = accentBrush;
        CategoryBadge.Background = new SolidColorBrush(badgeBgColor);
        CategoryBadgeText.Foreground = accentBrush;
        CategoryBadgeText.Text = badgeText;

        SpeechText.FlowDirection = ContainsArabic(message) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
        SpeechText.Text = message;

        if (!string.IsNullOrWhiteSpace(fixOrSuggestion))
        {
            _currentFixText = fixOrSuggestion.Trim();
            FixSuggestionText.FlowDirection = ContainsArabic(_currentFixText) ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;
            FixSuggestionText.Text = _currentFixText;
            CopyFixBtn.Content = "📋 Copy";
            FixHighlightBox.Visibility = Visibility.Visible;
        }
        else
        {
            _currentFixText = null;
            FixHighlightBox.Visibility = Visibility.Collapsed;
        }

        SpeechBubbleContainer.Visibility = Visibility.Visible;
        _speechHideTimer.Stop();
        _speechHideTimer.Interval = TimeSpan.FromSeconds(durationSeconds);
        _speechHideTimer.Start();
    }

    private static bool ContainsArabic(string text)
    {
        foreach (char c in text)
        {
            if (c is >= '\u0600' and <= '\u06FF' or >= '\u0750' and <= '\u077F')
            {
                return true;
            }
        }
        return false;
    }

    private void CopyFixBtn_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(_currentFixText))
        {
            return;
        }

        try
        {
            Clipboard.SetText(_currentFixText);
            CopyFixBtn.Content = "✓ Copied";
        }
        catch
        {
            CopyFixBtn.Content = "Retry";
        }
    }

    private void DismissBubbleBtn_Click(object sender, RoutedEventArgs e)
    {
        _speechHideTimer.Stop();
        SpeechBubbleContainer.Visibility = Visibility.Collapsed;
    }

    private void ToggleMiniChat(bool open)
    {
        MiniChatPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
        if (open)
        {
            Activate();
            MiniChatInput.Focus();
        }
    }

    private async void MiniChatSendBtn_Click(object sender, RoutedEventArgs e)
    {
        await SubmitAskAboutScreenAsync();
    }

    private async void MiniChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SubmitAskAboutScreenAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ToggleMiniChat(false);
        }
    }

    private void MiniChatCloseBtn_Click(object sender, RoutedEventArgs e) => ToggleMiniChat(false);

    private async Task SubmitAskAboutScreenAsync()
    {
        string question = MiniChatInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(question))
        {
            return;
        }

        MiniChatInput.Text = string.Empty;
        MiniChatSendBtn.IsEnabled = false;
        try
        {
            await RunScreenVisionAsync(userQuestion: question, isAutoWatch: false);
        }
        finally
        {
            MiniChatSendBtn.IsEnabled = true;
        }
    }

    private void RegisterGlobalHotkeys()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);

        uint mods = Win32Interop.MOD_CONTROL | Win32Interop.MOD_SHIFT | Win32Interop.MOD_NOREPEAT;
        _ = Win32Interop.RegisterHotKey(handle, Win32Interop.BOSS_MODE_HOTKEY_ID, mods, Win32Interop.VK_H);
        _ = Win32Interop.RegisterHotKey(handle, Win32Interop.SCREEN_SCAN_HOTKEY_ID, mods, Win32Interop.VK_S);
    }

    private void UnregisterGlobalHotkeys()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = Win32Interop.UnregisterHotKey(handle, Win32Interop.BOSS_MODE_HOTKEY_ID);
            _ = Win32Interop.UnregisterHotKey(handle, Win32Interop.SCREEN_SCAN_HOTKEY_ID);
        }
        _hwndSource?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Interop.WM_HOTKEY)
        {
            int id = wParam.ToInt32();
            if (id == Win32Interop.BOSS_MODE_HOTKEY_ID)
            {
                BossModeToggleRequested?.Invoke();
                handled = true;
            }
            else if (id == Win32Interop.SCREEN_SCAN_HOTKEY_ID)
            {
                TriggerInstantScreenScan();
                handled = true;
            }
        }
        return IntPtr.Zero;
    }

    private void MenuScanNow_Click(object sender, RoutedEventArgs e) => TriggerInstantScreenScan();
    private void MenuChaosShove_Click(object sender, RoutedEventArgs e) => TriggerChaosPrank(ChaosPrankType.ShoveWindow);
    private void MenuChaosStealMouse_Click(object sender, RoutedEventArgs e) => TriggerChaosPrank(ChaosPrankType.StealMouse);
    private void MenuChaosShake_Click(object sender, RoutedEventArgs e) => TriggerChaosPrank(ChaosPrankType.ShakeWindow);
    private void MenuChaosResize_Click(object sender, RoutedEventArgs e) => TriggerChaosPrank(ChaosPrankType.SquishWindow);
    private void MenuChaosClimb_Click(object sender, RoutedEventArgs e) => TriggerChaosPrank(ChaosPrankType.ClimbWindow);
    private void MenuChaosMinimize_Click(object sender, RoutedEventArgs e) => TriggerChaosPrank(ChaosPrankType.MinimizeWindow);
    private void MenuBossMode_Click(object sender, RoutedEventArgs e) => BossModeToggleRequested?.Invoke();
    private void MenuWizard_Click(object sender, RoutedEventArgs e) => OpenWizardRequested?.Invoke();
    private void MenuSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsRequested?.Invoke();
    private void MenuSummon_Click(object sender, RoutedEventArgs e) => SummonToCenter();
    private void MenuExit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
