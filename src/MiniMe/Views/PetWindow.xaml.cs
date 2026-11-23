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
    private const double WindowHorizontalPadding = 77.0;
    private const double WindowTopSpeechReserve = 154.0;

    private AppSettings _settings;
    private readonly BrainService _brain;
    private readonly PhysicsEngine _physics;
    private readonly ChaosEngine _chaos;
    private readonly Stopwatch _frameClock = new();
    private readonly DispatcherTimer _speechHideTimer;
    private readonly DispatcherTimer _contextQuipTimer;

    private BitmapSource[] _frames = [];
    private SpriteFrame _renderedFrame = (SpriteFrame)(-1);

    private HwndSource? _hwndSource;
    private bool _isBossMode;
    private bool _isPointerDown;
    private bool _isDragging;
    private Point _pointerDownScreenDip;
    private double _lastDragTimeSec;
    private bool _chatRequestInFlight;

    public event Action? OpenSetupRequested;
    public event Action? OpenSettingsRequested;
    public event Action? BossModeToggleRequested;
    public event Action? ExitRequested;

    public PetWindow(AppSettings settings, BrainService brain)
    {
        InitializeComponent();

        _settings = settings;
        _brain = brain;
        _physics = new PhysicsEngine();
        _chaos = new ChaosEngine(_physics);

        _physics.OnLandedOnWindow += () =>
        {
            if (!_isBossMode)
            {
                ShowSpeechBubble(_brain.GetOfflineQuip("climb", _settings.Language), 4.5);
            }
        };

        _chaos.OnChaosQuipRequested += category =>
        {
            if (!_isBossMode)
            {
                ShowSpeechBubble(_brain.GetOfflineQuip(category, _settings.Language), 4.5);
            }
        };

        _speechHideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5.0) };
        _speechHideTimer.Tick += (_, _) =>
        {
            _speechHideTimer.Stop();
            SpeechBubbleContainer.Visibility = Visibility.Collapsed;
        };

        _contextQuipTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.QuipFrequencySeconds, 15, 90))
        };
        _contextQuipTimer.Tick += ContextQuipTimer_Tick;

        ReloadSkin(_settings);
        ApplyScale(_settings.PetScale);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        RegisterGlobalBossHotkey();

        _physics.InitializeAtCenter(SystemParameters.WorkArea);
        SyncWindowPosition();

        _frameClock.Restart();
        CompositionTarget.Rendering += OnCompositionRendering;
        _contextQuipTimer.Start();

        string greeting = _settings.Language == QuipLanguage.EgyptianArabic
            ? "أهلاً بيك! أنا MiniMe، اضربني كليك أو دبل كليك نتكلم!"
            : "Hey! I'm MiniMe — click to slap, drag to fling, or double-click to chat!";
        ShowSpeechBubble(greeting, 6.0);
    }

    private void Window_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        CompositionTarget.Rendering -= OnCompositionRendering;
        _contextQuipTimer.Stop();
        _speechHideTimer.Stop();
        UnregisterGlobalBossHotkey();
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
        _contextQuipTimer.Interval = TimeSpan.FromSeconds(Math.Clamp(_settings.QuipFrequencySeconds, 15, 90));
    }

    public void SetBossMode(bool enabled)
    {
        _isBossMode = enabled;
        if (enabled)
        {
            _chaos.AbortAll();
            MiniChatPanel.Visibility = Visibility.Collapsed;
            SpeechBubbleContainer.Visibility = Visibility.Collapsed;
            Hide();
        }
        else
        {
            Show();
            Topmost = true;
            string backMsg = _settings.Language == QuipLanguage.EgyptianArabic
                ? "رجعنا تاني! المدير مشي؟ 👀"
                : "We're back! Coast is clear? 👀";
            ShowSpeechBubble(backMsg, 4.0);
        }
    }

    public void SummonToCenter()
    {
        _chaos.AbortAll();
        _physics.SummonToCenter(SystemParameters.WorkArea);
        SyncWindowPosition();
    }

    private void ApplyScale(double scale)
    {
        double clamped = Math.Clamp(scale, 0.60, 1.60);
        double size = BasePetSize * clamped;
        _physics.PetWidth = size;
        _physics.PetHeight = size;
        PetHost.Width = size;
        PetHost.Height = size;
        Width = Math.Max(290.0, size + (WindowHorizontalPadding * 2.0));
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
                ShowSpeechBubble(_brain.GetOfflineQuip("drag", _settings.Language), 3.0);
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
            // Quick single-click = Slap!
            Point localPos = e.GetPosition(PetHost);
            _physics.TriggerSlap(localPos.X);
            ShowSpeechBubble(_brain.GetOfflineQuip("slap", _settings.Language), 4.5);
        }
    }

    private async void ContextQuipTimer_Tick(object? sender, EventArgs e)
    {
        if (_isBossMode || MiniChatPanel.Visibility == Visibility.Visible)
        {
            return;
        }

        string activeTitle = Win32Interop.GetActiveWindowTitle();
        string quip = await _brain.GenerateContextQuipAsync(activeTitle, _settings);
        if (!_isBossMode)
        {
            ShowSpeechBubble(quip, 6.0);
        }
    }

    public void ShowSpeechBubble(string message, double durationSeconds = 5.5)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return;
        }

        SpeechText.FlowDirection = ContainsArabic(message)
            ? FlowDirection.RightToLeft
            : FlowDirection.LeftToRight;
        SpeechText.Text = message;
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
        await SubmitMiniChatAsync();
    }

    private async void MiniChatInput_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await SubmitMiniChatAsync();
        }
        else if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ToggleMiniChat(false);
        }
    }

    private void MiniChatCloseBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleMiniChat(false);
    }

    private async Task SubmitMiniChatAsync()
    {
        if (_chatRequestInFlight)
        {
            return;
        }

        string text = MiniChatInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        _chatRequestInFlight = true;
        MiniChatInput.Text = string.Empty;
        MiniChatSendBtn.IsEnabled = false;
        ShowSpeechBubble("...", 15.0);

        try
        {
            string activeTitle = Win32Interop.GetActiveWindowTitle();
            string reply = await _brain.ChatWithPetAsync(text, activeTitle, _settings);
            ShowSpeechBubble(reply, 7.5);
        }
        finally
        {
            _chatRequestInFlight = false;
            MiniChatSendBtn.IsEnabled = true;
        }
    }

    private void RegisterGlobalBossHotkey()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        _hwndSource = HwndSource.FromHwnd(handle);
        _hwndSource?.AddHook(WndProc);
        _ = Win32Interop.RegisterHotKey(
            handle,
            Win32Interop.BOSS_MODE_HOTKEY_ID,
            Win32Interop.MOD_CONTROL | Win32Interop.MOD_SHIFT | Win32Interop.MOD_NOREPEAT,
            Win32Interop.VK_H);
    }

    private void UnregisterGlobalBossHotkey()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero)
        {
            _ = Win32Interop.UnregisterHotKey(handle, Win32Interop.BOSS_MODE_HOTKEY_ID);
        }
        _hwndSource?.RemoveHook(WndProc);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == Win32Interop.WM_HOTKEY && wParam.ToInt32() == Win32Interop.BOSS_MODE_HOTKEY_ID)
        {
            BossModeToggleRequested?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    private void MenuSkinSetup_Click(object sender, RoutedEventArgs e) => OpenSetupRequested?.Invoke();
    private void MenuSettings_Click(object sender, RoutedEventArgs e) => OpenSettingsRequested?.Invoke();
    private void MenuBossMode_Click(object sender, RoutedEventArgs e) => BossModeToggleRequested?.Invoke();
    private void MenuSummon_Click(object sender, RoutedEventArgs e) => SummonToCenter();
    private void MenuExit_Click(object sender, RoutedEventArgs e) => ExitRequested?.Invoke();
}
