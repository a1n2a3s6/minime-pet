using System.Windows;
using MiniMe.Models;
using MiniMe.Native;

namespace MiniMe.Engine;

public enum ChaosPrankType
{
    StealMouse,
    ShoveWindow,
    ShakeWindow,
    SquishWindow,
    MinimizeWindow,
    ClimbWindow
}

public sealed class ChaosEngine
{
    private enum ChaosPhase
    {
        None,
        MouseHijackSprint,
        MouseHijackDrag,
        MouseHijackEscape,
        WindowShoveApproach,
        WindowShovePush,
        WindowShakeApproach,
        WindowShakeVibrate,
        WindowResizeApproach,
        WindowResizeSquish,
        WindowResizeRestore,
        MinimizeApproach,
        MinimizeTrigger,
        MinimizeLaughHide
    }

    private readonly PhysicsEngine _physics;
    private readonly Random _rng = new();

    private ChaosPhase _phase = ChaosPhase.None;
    private double _cooldownSeconds = 12.0;
    private double _phaseElapsed;
    private double _walkAnimTimer;
    private bool _walkToggle;

    // Target window state
    private IntPtr _targetWindow = IntPtr.Zero;
    private int _origLeft;
    private int _origTop;
    private int _origWidth;
    private int _origHeight;
    private int _pushDirection = 1;
    private int _resizeDeltaW;
    private int _resizeDeltaH;
    private double _approachX;
    private double _approachY;

    // Mouse hijack curved Bezier drag state
    private double _mouseP0X;
    private double _mouseP0Y;
    private double _mouseP1X;
    private double _mouseP1Y;
    private double _mouseP2X;
    private double _mouseP2Y;
    private int _expectedCursorX;
    private int _expectedCursorY;
    private int _escapeDir = 1;

    public bool IsActive => _phase != ChaosPhase.None;

    public event Action<string>? OnChaosQuipRequested;

    public ChaosEngine(PhysicsEngine physics)
    {
        _physics = physics;
    }

    public void ResetCooldown(AppSettings settings)
    {
        _cooldownSeconds = settings.GetChaosIntervalSeconds();
    }

    public void AbortAll()
    {
        if (_phase != ChaosPhase.None)
        {
            _phase = ChaosPhase.None;
            _targetWindow = IntPtr.Zero;
            _physics.SetExternalControl(false);
        }
    }

    /// <summary>
    /// Immediately triggers a specific physical Win32 prank (used by Tray / Right-Click "Trigger Chaos Now" menu).
    /// </summary>
    public bool TriggerPrankNow(
        ChaosPrankType prank,
        AppSettings settings,
        double dpiScaleX,
        double dpiScaleY)
    {
        AbortAll();

        int screenPxW = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpiScaleX);
        int screenPxH = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpiScaleY);

        if (prank == ChaosPrankType.StealMouse)
        {
            StartMouseHijack();
            return true;
        }

        bool allowUnmax = settings.ChaosLevel == ChaosIntensity.FullGremlin && settings.EnableUnmaximizePrank;
        Win32Interop.WindowInfo? target = PickTargetWindow(screenPxW, screenPxH, allowMaximized: prank is ChaosPrankType.MinimizeWindow or ChaosPrankType.ClimbWindow || allowUnmax);
        if (target is null)
        {
            return false;
        }

        // If FullGremlin + Unmaximize Prank is enabled on a maximized normal app, restore it safely before moving/resizing
        if (target.IsMaximized && prank is ChaosPrankType.ShoveWindow or ChaosPrankType.ShakeWindow or ChaosPrankType.SquishWindow)
        {
            if (!allowUnmax)
            {
                return false;
            }

            if (Win32Interop.SafeRestoreWindow(target.Handle) &&
                Win32Interop.GetWindowRect(target.Handle, out Win32Interop.RECT restoredRect))
            {
                target = target with { Rect = restoredRect, IsMaximized = false };
            }
            else
            {
                return false;
            }
        }

        switch (prank)
        {
            case ChaosPrankType.ClimbWindow:
                return _physics.TryStartWindowClimb(target, dpiScaleX, dpiScaleY, forceOverride: true);
            case ChaosPrankType.ShoveWindow:
                StartWindowShove(target, dpiScaleX, dpiScaleY);
                return true;
            case ChaosPrankType.ShakeWindow:
                StartWindowShake(target, dpiScaleX, dpiScaleY);
                return true;
            case ChaosPrankType.SquishWindow:
                StartWindowResize(target, dpiScaleX, dpiScaleY);
                return true;
            case ChaosPrankType.MinimizeWindow:
                StartSneakyMinimize(target, dpiScaleX, dpiScaleY);
                return true;
            default:
                return false;
        }
    }

    public void Update(
        double dt,
        Rect workArea,
        double dpiScaleX,
        double dpiScaleY,
        AppSettings settings,
        bool isBossMode)
    {
        if (isBossMode)
        {
            AbortAll();
            return;
        }

        double clampedDt = Math.Clamp(dt, 0.001, 0.05);
        int screenPxW = (int)Math.Round(SystemParameters.PrimaryScreenWidth * dpiScaleX);
        int screenPxH = (int)Math.Round(SystemParameters.PrimaryScreenHeight * dpiScaleY);

        if (_phase != ChaosPhase.None)
        {
            StepActivePrank(clampedDt, workArea, dpiScaleX, dpiScaleY, screenPxW, screenPxH);
            return;
        }

        if (_physics.State is not (PetBehaviorState.Walking or PetBehaviorState.Idle or PetBehaviorState.Sitting))
        {
            return;
        }

        _cooldownSeconds -= clampedDt;
        if (_cooldownSeconds > 0)
        {
            return;
        }

        int baseInterval = settings.GetChaosIntervalSeconds();
        _cooldownSeconds = baseInterval * (0.85 + (_rng.NextDouble() * 0.35));

        var pool = new List<ChaosPrankType>();
        if (settings.EnableMouseThief)
        {
            pool.Add(ChaosPrankType.StealMouse);
        }
        if (settings.EnableWindowShoveShake)
        {
            pool.Add(ChaosPrankType.ShoveWindow);
            pool.Add(ChaosPrankType.ShakeWindow);
        }
        if (settings.EnableWindowResize)
        {
            pool.Add(ChaosPrankType.SquishWindow);
        }
        if (settings.EnableWindowClimber)
        {
            pool.Add(ChaosPrankType.ClimbWindow);
        }
        if (settings.EnableSneakyMinimizer && _rng.NextDouble() < 0.45)
        {
            pool.Add(ChaosPrankType.MinimizeWindow);
        }

        if (pool.Count == 0)
        {
            return;
        }

        ChaosPrankType chosen = pool[_rng.Next(pool.Count)];
        _ = TriggerPrankNow(chosen, settings, dpiScaleX, dpiScaleY);
    }

    private Win32Interop.WindowInfo? PickTargetWindow(int screenPxW, int screenPxH, bool allowMaximized)
    {
        // Prefer the active foreground window if it is safe to interact with
        IntPtr fg = Win32Interop.GetForegroundWindow();
        if (Win32Interop.IsWindowSafeToInteract(fg, screenPxW, screenPxH, allowMaximized, out Win32Interop.WindowInfo? fgInfo) &&
            fgInfo is not null)
        {
            return fgInfo;
        }

        List<Win32Interop.WindowInfo> candidates = Win32Interop.GetTargetableWindows(screenPxW, screenPxH, allowMaximized);
        return candidates.Count > 0 ? candidates[_rng.Next(candidates.Count)] : null;
    }

    private void StartMouseHijack()
    {
        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _phase = ChaosPhase.MouseHijackSprint;
    }

    private void StartWindowShove(Win32Interop.WindowInfo target, double dpiScaleX, double dpiScaleY)
    {
        _targetWindow = target.Handle;
        _origLeft = target.Rect.Left;
        _origTop = target.Rect.Top;

        double winLeftDip = target.Rect.Left / dpiScaleX;
        double winRightDip = target.Rect.Right / dpiScaleX;
        double winMidYDip = ((target.Rect.Top + target.Rect.Bottom) * 0.5) / dpiScaleY;

        bool pushRight = winLeftDip > _physics.PetWidth + 20;
        _pushDirection = pushRight ? 1 : -1;
        _approachX = pushRight ? (winLeftDip - _physics.PetWidth + 14) : (winRightDip - 14);
        _approachY = Math.Max(0, winMidYDip - (_physics.PetHeight * 0.5));

        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _phase = ChaosPhase.WindowShoveApproach;
    }

    private void StartWindowShake(Win32Interop.WindowInfo target, double dpiScaleX, double dpiScaleY)
    {
        _targetWindow = target.Handle;
        _origLeft = target.Rect.Left;
        _origTop = target.Rect.Top;

        double winLeftDip = target.Rect.Left / dpiScaleX;
        double winRightDip = target.Rect.Right / dpiScaleX;
        double winMidYDip = ((target.Rect.Top + target.Rect.Bottom) * 0.5) / dpiScaleY;

        bool fromLeft = winLeftDip > _physics.PetWidth + 20;
        _pushDirection = fromLeft ? 1 : -1;
        _approachX = fromLeft ? (winLeftDip - _physics.PetWidth + 14) : (winRightDip - 14);
        _approachY = Math.Max(0, winMidYDip - (_physics.PetHeight * 0.5));

        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _phase = ChaosPhase.WindowShakeApproach;
    }

    private void StartWindowResize(Win32Interop.WindowInfo target, double dpiScaleX, double dpiScaleY)
    {
        _targetWindow = target.Handle;
        _origLeft = target.Rect.Left;
        _origTop = target.Rect.Top;
        _origWidth = target.Rect.Width;
        _origHeight = target.Rect.Height;

        bool shrink = _origWidth > 540 && _rng.NextDouble() < 0.65;
        int amountW = _rng.Next(105, 150);
        int amountH = _rng.Next(80, 125);
        _resizeDeltaW = shrink ? -amountW : amountW;
        _resizeDeltaH = shrink ? -amountH : amountH;

        double winRightDip = target.Rect.Right / dpiScaleX;
        double winBottomDip = target.Rect.Bottom / dpiScaleY;
        _approachX = Math.Max(0, winRightDip - (_physics.PetWidth * 0.45));
        _approachY = Math.Max(0, winBottomDip - (_physics.PetHeight * 0.75));

        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _phase = ChaosPhase.WindowResizeApproach;
    }

    private void StartSneakyMinimize(Win32Interop.WindowInfo target, double dpiScaleX, double dpiScaleY)
    {
        _targetWindow = target.Handle;
        double winRightDip = target.Rect.Right / dpiScaleX;
        double winTopDip = target.Rect.Top / dpiScaleY;

        _approachX = Math.Max(0, winRightDip - 135 - (_physics.PetWidth * 0.5));
        _approachY = Math.Max(0, winTopDip - _physics.PetHeight + 26);

        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _phase = ChaosPhase.MinimizeApproach;
    }

    private void StepActivePrank(
        double dt,
        Rect workArea,
        double dpiScaleX,
        double dpiScaleY,
        int screenPxW,
        int screenPxH)
    {
        _phaseElapsed += dt;
        _walkAnimTimer += dt;
        if (_walkAnimTimer >= 0.11)
        {
            _walkAnimTimer = 0;
            _walkToggle = !_walkToggle;
        }

        switch (_phase)
        {
            // 1. TRUE MOUSE HIJACK (260px curved path over 1.2s, Esc or >120px shake abort)
            case ChaosPhase.MouseHijackSprint:
            {
                if (Win32Interop.IsEscapePressed() || !Win32Interop.GetCursorPos(out Win32Interop.POINT cursorPx))
                {
                    AbortAll();
                    return;
                }

                double targetX = (cursorPx.X / dpiScaleX) - (_physics.PetWidth * 0.5);
                double targetY = (cursorPx.Y / dpiScaleY) - (_physics.PetHeight * 0.45);

                if (MoveTowards(targetX, targetY, 640.0, dt))
                {
                    _mouseP0X = cursorPx.X;
                    _mouseP0Y = cursorPx.Y;

                    // Pick a screen corner direction ~260px away
                    double cornerX = cursorPx.X > (screenPxW / 2) ? 60.0 : (screenPxW - 60.0);
                    double cornerY = cursorPx.Y > (screenPxH / 2) ? 60.0 : (screenPxH - 80.0);
                    double vx = cornerX - _mouseP0X;
                    double vy = cornerY - _mouseP0Y;
                    double len = Math.Max(1.0, Math.Sqrt(vx * vx + vy * vy));

                    const double pullDistancePx = 260.0;
                    _mouseP2X = Math.Clamp(_mouseP0X + (vx / len) * pullDistancePx, 24, screenPxW - 24);
                    _mouseP2Y = Math.Clamp(_mouseP0Y + (vy / len) * pullDistancePx, 24, screenPxH - 24);

                    // Curved control point perpendicular to travel vector
                    double perpX = -(vy / len) * 75.0;
                    double perpY = (vx / len) * 75.0;
                    _mouseP1X = Math.Clamp(((_mouseP0X + _mouseP2X) * 0.5) + perpX, 24, screenPxW - 24);
                    _mouseP1Y = Math.Clamp(((_mouseP0Y + _mouseP2Y) * 0.5) + perpY, 24, screenPxH - 24);

                    _expectedCursorX = cursorPx.X;
                    _expectedCursorY = cursorPx.Y;
                    _escapeDir = _mouseP2X >= _mouseP0X ? 1 : -1;
                    _phaseElapsed = 0;
                    _phase = ChaosPhase.MouseHijackDrag;
                    OnChaosQuipRequested?.Invoke("chaos_mouse");
                }
                else if (_phaseElapsed > 2.8)
                {
                    AbortAll();
                }
                break;
            }

            case ChaosPhase.MouseHijackDrag:
            {
                if (Win32Interop.IsEscapePressed())
                {
                    AbortAll();
                    return;
                }

                // Instant safety abort if user shakes mouse hard (> 120px fight-back)
                if (Win32Interop.GetCursorPos(out Win32Interop.POINT currentPx))
                {
                    double fightDist = Math.Sqrt(
                        Math.Pow(currentPx.X - _expectedCursorX, 2) +
                        Math.Pow(currentPx.Y - _expectedCursorY, 2));
                    if (fightDist > 120.0)
                    {
                        AbortAll();
                        return;
                    }
                }

                const double dragDuration = 1.2;
                double t = Math.Clamp(_phaseElapsed / dragDuration, 0.0, 1.0);
                double u = 1.0 - t;

                // Quadratic Bezier curved path
                double bx = (u * u * _mouseP0X) + (2.0 * u * t * _mouseP1X) + (t * t * _mouseP2X);
                double by = (u * u * _mouseP0Y) + (2.0 * u * t * _mouseP1Y) + (t * t * _mouseP2Y);

                _expectedCursorX = Math.Clamp((int)Math.Round(bx), 16, screenPxW - 16);
                _expectedCursorY = Math.Clamp((int)Math.Round(by), 16, screenPxH - 16);
                _ = Win32Interop.SetCursorPos(_expectedCursorX, _expectedCursorY);

                _physics.FacingRight = _escapeDir > 0;
                _physics.X = (_expectedCursorX / dpiScaleX) - (_physics.PetWidth * 0.5);
                _physics.Y = (_expectedCursorY / dpiScaleY) - (_physics.PetHeight * 0.45);
                _physics.CurrentFrame = SpriteFrame.Troll;

                if (t >= 1.0)
                {
                    _phaseElapsed = 0;
                    _phase = ChaosPhase.MouseHijackEscape;
                }
                break;
            }

            case ChaosPhase.MouseHijackEscape:
            {
                _physics.FacingRight = _escapeDir > 0;
                _physics.X = Math.Clamp(_physics.X + (_escapeDir * 440.0 * dt), workArea.Left, workArea.Right - _physics.PetWidth);
                _physics.Y = Math.Min(workArea.Bottom - _physics.PetHeight, _physics.Y + 460.0 * dt);
                _physics.CurrentFrame = SpriteFrame.Troll;
                if (_phaseElapsed >= 0.85)
                {
                    AbortAll();
                }
                break;
            }

            // 2A. REAL WINDOW SHOVE (120px 60fps push)
            case ChaosPhase.WindowShoveApproach:
                if (!Win32Interop.IsWindowSafeToInteract(_targetWindow, screenPxW, screenPxH, false, out _))
                {
                    AbortAll();
                    return;
                }
                if (MoveTowards(_approachX, _approachY, 440.0, dt))
                {
                    _phaseElapsed = 0;
                    _physics.FacingRight = _pushDirection > 0;
                    _phase = ChaosPhase.WindowShovePush;
                    OnChaosQuipRequested?.Invoke("chaos_shove");
                }
                else if (_phaseElapsed > 3.5)
                {
                    AbortAll();
                }
                break;

            case ChaosPhase.WindowShovePush:
            {
                const double pushDuration = 0.85;
                double t = Math.Clamp(_phaseElapsed / pushDuration, 0.0, 1.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 3.0);
                double shiftPx = _pushDirection * 120.0 * eased;

                int nextLeft = _origLeft + (int)Math.Round(shiftPx);
                if (!Win32Interop.SafeSetWindowPosition(_targetWindow, nextLeft, _origTop, screenPxW, screenPxH))
                {
                    AbortAll();
                    return;
                }

                _physics.X = _approachX + (shiftPx / dpiScaleX);
                _physics.CurrentFrame = SpriteFrame.Troll;
                if (t >= 1.0)
                {
                    AbortAll();
                }
                break;
            }

            // 2B. VIOLENT WINDOW SHAKE (±14px for 0.6s)
            case ChaosPhase.WindowShakeApproach:
                if (!Win32Interop.IsWindowSafeToInteract(_targetWindow, screenPxW, screenPxH, false, out _))
                {
                    AbortAll();
                    return;
                }
                if (MoveTowards(_approachX, _approachY, 460.0, dt))
                {
                    _phaseElapsed = 0;
                    _physics.FacingRight = _pushDirection > 0;
                    _phase = ChaosPhase.WindowShakeVibrate;
                    OnChaosQuipRequested?.Invoke("chaos_shake");
                }
                else if (_phaseElapsed > 3.5)
                {
                    AbortAll();
                }
                break;

            case ChaosPhase.WindowShakeVibrate:
            {
                const double shakeDuration = 0.60;
                double t = Math.Clamp(_phaseElapsed / shakeDuration, 0.0, 1.0);
                double offsetPx = t < 1.0 ? Math.Sin(t * Math.PI * 12.0) * 14.0 : 0.0;

                int nextLeft = _origLeft + (int)Math.Round(offsetPx);
                if (!Win32Interop.SafeSetWindowPosition(_targetWindow, nextLeft, _origTop, screenPxW, screenPxH))
                {
                    AbortAll();
                    return;
                }

                _physics.X = _approachX + (offsetPx / dpiScaleX);
                _physics.CurrentFrame = SpriteFrame.Troll;
                if (t >= 1.0)
                {
                    AbortAll();
                }
                break;
            }

            // 3. WINDOW RESIZE / SQUISH PRANK (100-150px via MoveWindow + playful restore)
            case ChaosPhase.WindowResizeApproach:
                if (!Win32Interop.IsWindowSafeToInteract(_targetWindow, screenPxW, screenPxH, false, out _))
                {
                    AbortAll();
                    return;
                }
                if (MoveTowards(_approachX, _approachY, 440.0, dt))
                {
                    _phaseElapsed = 0;
                    _phase = ChaosPhase.WindowResizeSquish;
                    OnChaosQuipRequested?.Invoke("chaos_resize");
                }
                else if (_phaseElapsed > 3.5)
                {
                    AbortAll();
                }
                break;

            case ChaosPhase.WindowResizeSquish:
            {
                const double squishDuration = 0.70;
                double t = Math.Clamp(_phaseElapsed / squishDuration, 0.0, 1.0);
                double eased = Math.Sin(t * Math.PI * 0.5);

                int newW = _origWidth + (int)Math.Round(_resizeDeltaW * eased);
                int newH = _origHeight + (int)Math.Round(_resizeDeltaH * eased);
                if (!Win32Interop.SafeResizeWindow(_targetWindow, _origLeft, _origTop, newW, newH, screenPxW, screenPxH))
                {
                    AbortAll();
                    return;
                }

                _physics.X = ((_origLeft + newW) / dpiScaleX) - (_physics.PetWidth * 0.45);
                _physics.Y = ((_origTop + newH) / dpiScaleY) - (_physics.PetHeight * 0.75);
                _physics.CurrentFrame = SpriteFrame.Troll;

                if (t >= 1.0)
                {
                    _phaseElapsed = 0;
                    _phase = ChaosPhase.WindowResizeRestore;
                }
                break;
            }

            case ChaosPhase.WindowResizeRestore:
            {
                // Hold squished briefly for comedic effect, then smoothly restore original size
                if (_phaseElapsed < 0.75)
                {
                    _physics.CurrentFrame = SpriteFrame.Troll;
                    return;
                }

                double t = Math.Clamp((_phaseElapsed - 0.75) / 0.55, 0.0, 1.0);
                double inv = 1.0 - t;
                int newW = _origWidth + (int)Math.Round(_resizeDeltaW * inv);
                int newH = _origHeight + (int)Math.Round(_resizeDeltaH * inv);
                _ = Win32Interop.SafeResizeWindow(_targetWindow, _origLeft, _origTop, newW, newH, screenPxW, screenPxH);

                if (t >= 1.0)
                {
                    AbortAll();
                }
                break;
            }

            // 4. SNEAKY MINIMIZE (ShowWindow SW_MINIMIZE + hide at bottom laughing)
            case ChaosPhase.MinimizeApproach:
                if (!Win32Interop.IsWindowSafeToInteract(_targetWindow, screenPxW, screenPxH, true, out _))
                {
                    AbortAll();
                    return;
                }
                if (MoveTowards(_approachX, _approachY, 460.0, dt))
                {
                    _phaseElapsed = 0;
                    _physics.CurrentFrame = SpriteFrame.Troll;
                    _phase = ChaosPhase.MinimizeTrigger;
                }
                else if (_phaseElapsed > 3.2)
                {
                    AbortAll();
                }
                break;

            case ChaosPhase.MinimizeTrigger:
                _physics.CurrentFrame = SpriteFrame.Troll;
                if (_phaseElapsed >= 0.35)
                {
                    if (Win32Interop.SafeMinimizeWindow(_targetWindow, screenPxW, screenPxH))
                    {
                        OnChaosQuipRequested?.Invoke("chaos_minimize");
                    }
                    _phaseElapsed = 0;
                    _phase = ChaosPhase.MinimizeLaughHide;
                }
                break;

            case ChaosPhase.MinimizeLaughHide:
                _physics.Y = Math.Min(workArea.Bottom - _physics.PetHeight, _physics.Y + 680.0 * dt);
                _physics.CurrentFrame = SpriteFrame.Troll;
                if (_phaseElapsed >= 1.0)
                {
                    AbortAll();
                }
                break;
        }
    }

    private bool MoveTowards(double destX, double destY, double speed, double dt)
    {
        double dx = destX - _physics.X;
        double dy = destY - _physics.Y;
        double dist = Math.Sqrt(dx * dx + dy * dy);

        if (dist <= speed * dt + 8.0)
        {
            _physics.X = destX;
            _physics.Y = destY;
            return true;
        }

        _physics.FacingRight = dx >= 0;
        _physics.X += (dx / dist) * speed * dt;
        _physics.Y += (dy / dist) * speed * dt;
        _physics.CurrentFrame = _walkToggle ? SpriteFrame.Walk1 : SpriteFrame.Walk2;
        return false;
    }
}
