using System.Windows;
using MiniMe.Models;
using MiniMe.Native;

namespace MiniMe.Engine;

public sealed class ChaosEngine
{
    private enum ChaosAction
    {
        None,
        WindowPushApproach,
        WindowPushShove,
        MouseThiefApproach,
        MouseThiefPull,
        MouseThiefEscape,
        MinimizeApproach,
        MinimizeTrigger
    }

    private readonly PhysicsEngine _physics;
    private readonly Random _rng = new();

    private ChaosAction _action = ChaosAction.None;
    private double _cooldownSeconds = 18.0;
    private double _phaseElapsed;
    private double _walkAnimTimer;
    private bool _walkToggle;

    private IntPtr _targetWindow = IntPtr.Zero;
    private int _pushDirection = 1;
    private int _origWindowLeft;
    private int _origWindowTop;
    private double _targetApproachX;
    private double _targetApproachY;

    // Mouse thief state
    private int _mouseStartX;
    private int _mouseStartY;
    private int _mousePullDirX;
    private int _mousePullDirY;
    private int _mouseEscapeDir = 1;

    public bool IsActive => _action != ChaosAction.None;

    public event Action<string>? OnChaosQuipRequested;

    public ChaosEngine(PhysicsEngine physics)
    {
        _physics = physics;
    }

    public void AbortAll()
    {
        if (_action != ChaosAction.None)
        {
            _action = ChaosAction.None;
            _targetWindow = IntPtr.Zero;
            _physics.SetExternalControl(false);
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

        if (_action != ChaosAction.None)
        {
            StepActiveChaos(clampedDt, workArea, dpiScaleX, dpiScaleY, screenPxW, screenPxH);
            return;
        }

        // Only trigger new chaos when MiniMe is calmly on the desktop
        if (_physics.State is not (PetBehaviorState.Walking or PetBehaviorState.Idle or PetBehaviorState.Sitting))
        {
            return;
        }

        _cooldownSeconds -= clampedDt;
        if (_cooldownSeconds > 0)
        {
            return;
        }

        _cooldownSeconds = _rng.Next(22, 42);
        TryTriggerRandomBehavior(settings, screenPxW, screenPxH, dpiScaleX, dpiScaleY);
    }

    private void TryTriggerRandomBehavior(
        AppSettings settings,
        int screenPxW,
        int screenPxH,
        double dpiScaleX,
        double dpiScaleY)
    {
        var candidates = new List<string>();
        if (settings.EnableWindowClimber)
        {
            candidates.Add("climb");
            candidates.Add("climb"); // Favor harmless window climbing slightly
        }
        if (settings.EnableWindowPusher)
        {
            candidates.Add("push");
        }
        if (settings.EnableMouseThief)
        {
            candidates.Add("mouse");
        }
        if (settings.EnableSneakyMinimizer && _rng.NextDouble() < 0.35)
        {
            candidates.Add("minimize");
        }

        if (candidates.Count == 0)
        {
            return;
        }

        string chosen = candidates[_rng.Next(candidates.Count)];

        if (chosen == "mouse")
        {
            StartMouseThief();
            return;
        }

        List<Win32Interop.WindowInfo> windows = Win32Interop.GetTargetableWindows(screenPxW, screenPxH);
        if (windows.Count == 0)
        {
            return;
        }

        Win32Interop.WindowInfo target = windows[_rng.Next(windows.Count)];
        if (chosen == "climb")
        {
            _physics.TryStartWindowClimb(target, dpiScaleX, dpiScaleY);
        }
        else if (chosen == "push")
        {
            StartWindowPush(target, dpiScaleX, dpiScaleY);
        }
        else if (chosen == "minimize")
        {
            StartSneakyMinimize(target, dpiScaleX, dpiScaleY);
        }
    }

    private void StartWindowPush(Win32Interop.WindowInfo target, double dpiScaleX, double dpiScaleY)
    {
        _targetWindow = target.Handle;
        _origWindowLeft = target.Rect.Left;
        _origWindowTop = target.Rect.Top;

        double winLeftDip = target.Rect.Left / dpiScaleX;
        double winRightDip = target.Rect.Right / dpiScaleX;
        double winMidYDip = ((target.Rect.Top + target.Rect.Bottom) * 0.5) / dpiScaleY;

        bool pushFromLeft = winLeftDip > _physics.PetWidth + 24;
        _pushDirection = pushFromLeft ? 1 : -1;
        _targetApproachX = pushFromLeft ? (winLeftDip - _physics.PetWidth + 14) : (winRightDip - 14);
        _targetApproachY = Math.Max(0, winMidYDip - (_physics.PetHeight * 0.5));

        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _action = ChaosAction.WindowPushApproach;
    }

    private void StartMouseThief()
    {
        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _action = ChaosAction.MouseThiefApproach;
    }

    private void StartSneakyMinimize(Win32Interop.WindowInfo target, double dpiScaleX, double dpiScaleY)
    {
        _targetWindow = target.Handle;
        double winRightDip = target.Rect.Right / dpiScaleX;
        double winTopDip = target.Rect.Top / dpiScaleY;

        // Aim near the top-right minimize button area
        _targetApproachX = Math.Max(0, winRightDip - 135 - (_physics.PetWidth * 0.5));
        _targetApproachY = Math.Max(0, winTopDip - _physics.PetHeight + 22);

        _phaseElapsed = 0;
        _physics.SetExternalControl(true);
        _action = ChaosAction.MinimizeApproach;
    }

    private void StepActiveChaos(
        double dt,
        Rect workArea,
        double dpiScaleX,
        double dpiScaleY,
        int screenPxW,
        int screenPxH)
    {
        _phaseElapsed += dt;
        _walkAnimTimer += dt;
        if (_walkAnimTimer >= 0.13)
        {
            _walkAnimTimer = 0;
            _walkToggle = !_walkToggle;
        }

        switch (_action)
        {
            case ChaosAction.WindowPushApproach:
                if (!Win32Interop.IsWindowSafeToInteract(_targetWindow, screenPxW, screenPxH, out _))
                {
                    AbortAll();
                    return;
                }

                if (MoveTowards(_targetApproachX, _targetApproachY, 340.0, dt))
                {
                    _phaseElapsed = 0;
                    _physics.FacingRight = _pushDirection > 0;
                    _action = ChaosAction.WindowPushShove;
                    OnChaosQuipRequested?.Invoke("chaos_push");
                }
                else if (_phaseElapsed > 4.0)
                {
                    AbortAll();
                }
                break;

            case ChaosAction.WindowPushShove:
            {
                const double shoveDuration = 1.15;
                double t = Math.Clamp(_phaseElapsed / shoveDuration, 0.0, 1.0);
                double shake = Math.Sin(t * Math.PI * 6.0) * 14.0 * (1.0 - t * 0.35);
                double netShift = _pushDirection * 48.0 * t;

                int nextLeft = _origWindowLeft + (int)Math.Round(netShift + shake);
                if (!Win32Interop.SafeMoveWindow(_targetWindow, nextLeft, _origWindowTop, screenPxW, screenPxH))
                {
                    AbortAll();
                    return;
                }

                _physics.X = _targetApproachX + ((netShift + shake) / dpiScaleX);
                _physics.CurrentFrame = SpriteFrame.Troll;

                if (t >= 1.0)
                {
                    AbortAll();
                }
                break;
            }

            case ChaosAction.MouseThiefApproach:
            {
                if (!Win32Interop.GetCursorPos(out Win32Interop.POINT cursorPx))
                {
                    AbortAll();
                    return;
                }

                double targetX = (cursorPx.X / dpiScaleX) - (_physics.PetWidth * 0.5);
                double targetY = (cursorPx.Y / dpiScaleY) - (_physics.PetHeight * 0.45);

                if (MoveTowards(targetX, targetY, 460.0, dt))
                {
                    _mouseStartX = cursorPx.X;
                    _mouseStartY = cursorPx.Y;
                    _mousePullDirX = cursorPx.X > (screenPxW / 2) ? -1 : 1;
                    _mousePullDirY = _rng.NextDouble() < 0.5 ? -1 : 1;
                    _mouseEscapeDir = _mousePullDirX;
                    _phaseElapsed = 0;
                    _action = ChaosAction.MouseThiefPull;
                    OnChaosQuipRequested?.Invoke("chaos_mouse");
                }
                else if (_phaseElapsed > 3.2)
                {
                    AbortAll();
                }
                break;
            }

            case ChaosAction.MouseThiefPull:
            {
                const double pullDuration = 1.2;
                const double pullDistancePx = 120.0;
                double t = Math.Clamp(_phaseElapsed / pullDuration, 0.0, 1.0);
                double eased = 1.0 - Math.Pow(1.0 - t, 2.0);

                int desiredX = Math.Clamp(_mouseStartX + (int)Math.Round(_mousePullDirX * pullDistancePx * eased), 20, screenPxW - 20);
                int desiredY = Math.Clamp(_mouseStartY + (int)Math.Round(_mousePullDirY * 36.0 * eased), 20, screenPxH - 20);
                _ = Win32Interop.SetCursorPos(desiredX, desiredY);

                _physics.FacingRight = _mousePullDirX > 0;
                _physics.X = (desiredX / dpiScaleX) - (_physics.PetWidth * 0.5);
                _physics.Y = (desiredY / dpiScaleY) - (_physics.PetHeight * 0.45);
                _physics.CurrentFrame = SpriteFrame.Troll;

                if (t >= 1.0)
                {
                    _phaseElapsed = 0;
                    _action = ChaosAction.MouseThiefEscape;
                }
                break;
            }

            case ChaosAction.MouseThiefEscape:
            {
                _physics.FacingRight = _mouseEscapeDir > 0;
                _physics.X = Math.Clamp(_physics.X + (_mouseEscapeDir * 360.0 * dt), workArea.Left, workArea.Right - _physics.PetWidth);
                _physics.Y = Math.Min(workArea.Bottom - _physics.PetHeight, _physics.Y + 420.0 * dt);
                _physics.CurrentFrame = SpriteFrame.Troll;

                if (_phaseElapsed >= 0.95)
                {
                    AbortAll();
                }
                break;
            }

            case ChaosAction.MinimizeApproach:
                if (!Win32Interop.IsWindowSafeToInteract(_targetWindow, screenPxW, screenPxH, out _))
                {
                    AbortAll();
                    return;
                }

                if (MoveTowards(_targetApproachX, _targetApproachY, 360.0, dt))
                {
                    _phaseElapsed = 0;
                    _physics.CurrentFrame = SpriteFrame.Troll;
                    _action = ChaosAction.MinimizeTrigger;
                }
                else if (_phaseElapsed > 3.5)
                {
                    AbortAll();
                }
                break;

            case ChaosAction.MinimizeTrigger:
                _physics.CurrentFrame = SpriteFrame.Troll;
                if (_phaseElapsed >= 0.45)
                {
                    if (Win32Interop.SafeMinimizeWindow(_targetWindow, screenPxW, screenPxH))
                    {
                        OnChaosQuipRequested?.Invoke("chaos_minimize");
                    }
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

        if (dist <= speed * dt + 6.0)
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
