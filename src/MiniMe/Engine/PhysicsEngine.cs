using System.Windows;
using MiniMe.Native;

namespace MiniMe.Engine;

public enum PetBehaviorState
{
    Walking,
    Idle,
    Sitting,
    Sleeping,
    Slapped,
    Dragged,
    Airborne,
    JumpingToWindow,
    Perched,
    ExternalControl
}

public sealed class PhysicsEngine
{
    private const double Gravity = 1750.0;
    private const double WalkSpeed = 110.0;
    private const double BounceRestitution = 0.58;
    private const double GroundFriction = 0.84;

    private readonly Random _rng = new();

    public double X { get; set; }
    public double Y { get; set; }
    public double VelocityX { get; set; }
    public double VelocityY { get; set; }
    public double PetWidth { get; set; } = 128.0;
    public double PetHeight { get; set; } = 128.0;

    public bool FacingRight { get; set; } = true;
    public PetBehaviorState State { get; private set; } = PetBehaviorState.Walking;
    public SpriteFrame CurrentFrame { get; set; } = SpriteFrame.Idle;

    public double BreathScaleY { get; private set; } = 1.0;
    public double TiltAngle { get; private set; } = 0.0;

    public bool IsDizzy => _dizzyTimer > 0.0;
    public double DizzyOrbitAngle { get; private set; }

    public IntPtr PerchedWindowHandle { get; private set; } = IntPtr.Zero;

    private double _stateTimer;
    private double _walkAnimTimer;
    private double _breathPhase;
    private double _dizzyTimer;
    private bool _walkToggle;

    private Point _lastDragCursor;
    private double _dragOffsetX;
    private double _dragOffsetY;

    private double _jumpStartX;
    private double _jumpStartY;
    private double _jumpTargetX;
    private double _jumpTargetY;
    private double _jumpElapsed;
    private double _jumpDuration;
    private double _perchRelativeOffsetX;

    public event Action? OnLandedOnWindow;

    public void InitializeAtCenter(Rect workArea)
    {
        X = workArea.Left + (workArea.Width - PetWidth) * 0.5;
        Y = workArea.Bottom - PetHeight;
        VelocityX = WalkSpeed;
        VelocityY = 0;
        FacingRight = true;
        EnterState(PetBehaviorState.Walking, 4.0);
    }

    public void SummonToCenter(Rect workArea)
    {
        PerchedWindowHandle = IntPtr.Zero;
        _dizzyTimer = 0;
        X = workArea.Left + (workArea.Width - PetWidth) * 0.5;
        Y = workArea.Top + (workArea.Height - PetHeight) * 0.35;
        VelocityX = 0;
        VelocityY = 0;
        EnterState(PetBehaviorState.Airborne, 0);
    }

    public void SetExternalControl(bool active)
    {
        if (active)
        {
            PerchedWindowHandle = IntPtr.Zero;
            State = PetBehaviorState.ExternalControl;
            TiltAngle = 0;
        }
        else if (State == PetBehaviorState.ExternalControl)
        {
            VelocityX = 0;
            VelocityY = 0;
            State = PetBehaviorState.Airborne;
        }
    }

    public void TriggerSlap(double clickLocalX)
    {
        PerchedWindowHandle = IntPtr.Zero;
        double dir = clickLocalX > (PetWidth * 0.5) ? -1.0 : 1.0;
        if (_rng.NextDouble() < 0.25)
        {
            dir = -dir;
        }

        VelocityX = dir * _rng.Next(380, 580);
        VelocityY = -_rng.Next(700, 880);
        FacingRight = VelocityX >= 0;
        _dizzyTimer = 2.4;
        State = PetBehaviorState.Slapped;
        CurrentFrame = SpriteFrame.HitDizzy;
    }

    public void BeginDrag(Point screenCursorDip)
    {
        PerchedWindowHandle = IntPtr.Zero;
        _dizzyTimer = 0;
        _dragOffsetX = screenCursorDip.X - X;
        _dragOffsetY = screenCursorDip.Y - Y;
        _lastDragCursor = screenCursorDip;
        VelocityX = 0;
        VelocityY = 0;
        State = PetBehaviorState.Dragged;
        CurrentFrame = SpriteFrame.Dragged;
    }

    public void UpdateDrag(Point screenCursorDip, double dt)
    {
        if (State != PetBehaviorState.Dragged)
        {
            return;
        }

        double safeDt = Math.Max(0.004, dt);
        double instVx = (screenCursorDip.X - _lastDragCursor.X) / safeDt;
        double instVy = (screenCursorDip.Y - _lastDragCursor.Y) / safeDt;

        VelocityX = (VelocityX * 0.65) + (instVx * 0.35);
        VelocityY = (VelocityY * 0.65) + (instVy * 0.35);
        _lastDragCursor = screenCursorDip;

        X = screenCursorDip.X - _dragOffsetX;
        Y = screenCursorDip.Y - _dragOffsetY;
        TiltAngle = Math.Clamp(VelocityX * 0.018, -22, 22);
        CurrentFrame = SpriteFrame.Dragged;
    }

    public void EndDrag()
    {
        if (State != PetBehaviorState.Dragged)
        {
            return;
        }

        VelocityX = Math.Clamp(VelocityX, -1550, 1550);
        VelocityY = Math.Clamp(VelocityY, -1450, 1200);
        if (Math.Abs(VelocityX) > 40)
        {
            FacingRight = VelocityX > 0;
        }

        State = PetBehaviorState.Airborne;
    }

    public bool TryStartWindowClimb(Win32Interop.WindowInfo target, double dpiScaleX, double dpiScaleY, bool forceOverride = false)
    {
        if (!forceOverride && State is PetBehaviorState.Dragged or PetBehaviorState.Slapped or PetBehaviorState.Airborne or PetBehaviorState.ExternalControl)
        {
            return false;
        }

        double winLeft = target.Rect.Left / dpiScaleX;
        double winRight = target.Rect.Right / dpiScaleX;
        double winTop = target.Rect.Top / dpiScaleY;

        double safeSpan = Math.Max(10, (winRight - winLeft) - PetWidth - 40);
        _perchRelativeOffsetX = 20 + (_rng.NextDouble() * safeSpan);
        double landX = winLeft + _perchRelativeOffsetX;
        double landY = Math.Max(0, winTop - PetHeight + 14);

        PerchedWindowHandle = target.Handle;
        _jumpStartX = X;
        _jumpStartY = Y;
        _jumpTargetX = landX;
        _jumpTargetY = landY;
        _jumpElapsed = 0;
        _jumpDuration = 0.62;
        FacingRight = landX >= X;
        State = PetBehaviorState.JumpingToWindow;
        CurrentFrame = SpriteFrame.Walk2;
        return true;
    }

    public void Update(double dt, Rect workArea, double dpiScaleX, double dpiScaleY)
    {
        double clampedDt = Math.Clamp(dt, 0.001, 0.05);

        _breathPhase += clampedDt * 3.4;
        BreathScaleY = State is PetBehaviorState.Idle or PetBehaviorState.Sitting or PetBehaviorState.Sleeping or PetBehaviorState.Perched
            ? 1.0 + Math.Sin(_breathPhase) * 0.028
            : 1.0;

        if (_dizzyTimer > 0)
        {
            _dizzyTimer = Math.Max(0, _dizzyTimer - clampedDt);
            DizzyOrbitAngle = (DizzyOrbitAngle + clampedDt * 320.0) % 360.0;
        }

        switch (State)
        {
            case PetBehaviorState.Dragged:
            case PetBehaviorState.ExternalControl:
                return;

            case PetBehaviorState.Slapped:
            case PetBehaviorState.Airborne:
                StepBallisticPhysics(clampedDt, workArea);
                return;

            case PetBehaviorState.JumpingToWindow:
                StepWindowJump(clampedDt);
                return;

            case PetBehaviorState.Perched:
                StepPerched(clampedDt, workArea, dpiScaleX, dpiScaleY);
                return;

            case PetBehaviorState.Walking:
                StepWalking(clampedDt, workArea);
                return;

            case PetBehaviorState.Idle:
            case PetBehaviorState.Sitting:
            case PetBehaviorState.Sleeping:
                StepStationary(clampedDt, workArea);
                return;
        }
    }

    private void StepBallisticPhysics(double dt, Rect workArea)
    {
        VelocityY += Gravity * dt;
        X += VelocityX * dt;
        Y += VelocityY * dt;

        if (X < workArea.Left)
        {
            X = workArea.Left;
            VelocityX = Math.Abs(VelocityX) * BounceRestitution;
            FacingRight = true;
        }
        else if (X + PetWidth > workArea.Right)
        {
            X = workArea.Right - PetWidth;
            VelocityX = -Math.Abs(VelocityX) * BounceRestitution;
            FacingRight = false;
        }

        if (Y < workArea.Top)
        {
            Y = workArea.Top;
            VelocityY = Math.Abs(VelocityY) * BounceRestitution;
        }

        double floorY = workArea.Bottom - PetHeight;
        if (Y >= floorY)
        {
            Y = floorY;
            if (Math.Abs(VelocityY) > 170)
            {
                VelocityY = -VelocityY * BounceRestitution;
                VelocityX *= GroundFriction;
            }
            else
            {
                VelocityY = 0;
                VelocityX *= GroundFriction;
                if (Math.Abs(VelocityX) < 28)
                {
                    VelocityX = 0;
                    TiltAngle = 0;
                    if (_dizzyTimer > 0.2)
                    {
                        CurrentFrame = SpriteFrame.HitDizzy;
                    }
                    else
                    {
                        EnterState(PetBehaviorState.Idle, _rng.NextDouble() * 2.0 + 1.2);
                    }
                    return;
                }
            }
        }

        TiltAngle = State == PetBehaviorState.Slapped
            ? Math.Clamp(VelocityX * 0.04, -26, 26)
            : Math.Clamp(VelocityX * 0.02, -16, 16);

        CurrentFrame = State == PetBehaviorState.Slapped || _dizzyTimer > 0
            ? SpriteFrame.HitDizzy
            : SpriteFrame.Dragged;
    }

    private void StepWindowJump(double dt)
    {
        _jumpElapsed += dt;
        double t = Math.Clamp(_jumpElapsed / _jumpDuration, 0.0, 1.0);
        double arcHeight = 95.0 * Math.Sin(t * Math.PI);

        X = _jumpStartX + (_jumpTargetX - _jumpStartX) * t;
        Y = (_jumpStartY + (_jumpTargetY - _jumpStartY) * t) - arcHeight;
        TiltAngle = (FacingRight ? 1 : -1) * Math.Sin(t * Math.PI) * 10.0;

        if (t >= 1.0)
        {
            TiltAngle = 0;
            EnterState(PetBehaviorState.Perched, _rng.NextDouble() * 14.0 + 12.0);
            CurrentFrame = SpriteFrame.Sitting;
            OnLandedOnWindow?.Invoke();
        }
    }

    private void StepPerched(double dt, Rect workArea, double dpiScaleX, double dpiScaleY)
    {
        _stateTimer -= dt;
        CurrentFrame = SpriteFrame.Sitting;
        TiltAngle = 0;

        if (PerchedWindowHandle == IntPtr.Zero ||
            !Win32Interop.IsWindowVisible(PerchedWindowHandle) ||
            Win32Interop.IsIconic(PerchedWindowHandle) ||
            !Win32Interop.GetWindowRect(PerchedWindowHandle, out Win32Interop.RECT rect))
        {
            DropFromWindow();
            return;
        }

        double winLeft = rect.Left / dpiScaleX;
        double winRight = rect.Right / dpiScaleX;
        double winTop = rect.Top / dpiScaleY;

        if (winTop < workArea.Top - 8 || winTop > workArea.Bottom - 36)
        {
            DropFromWindow();
            return;
        }

        // Follow the window smoothly as the user drags it around!
        double desiredX = Math.Clamp(winLeft + _perchRelativeOffsetX, winLeft + 8, Math.Max(winLeft + 8, winRight - PetWidth - 8));
        double desiredY = Math.Max(workArea.Top, winTop - PetHeight + 14);
        X += (desiredX - X) * Math.Min(1.0, dt * 22.0);
        Y += (desiredY - Y) * Math.Min(1.0, dt * 22.0);

        if (_stateTimer <= 0)
        {
            DropFromWindow();
        }
    }

    private void DropFromWindow()
    {
        PerchedWindowHandle = IntPtr.Zero;
        VelocityX = (FacingRight ? 1 : -1) * 95.0;
        VelocityY = -120.0;
        State = PetBehaviorState.Airborne;
    }

    private void StepWalking(double dt, Rect workArea)
    {
        double floorY = workArea.Bottom - PetHeight;
        Y = floorY;
        TiltAngle = 0;

        X += VelocityX * dt;
        if (X <= workArea.Left)
        {
            X = workArea.Left;
            VelocityX = Math.Abs(VelocityX);
            FacingRight = true;
        }
        else if (X + PetWidth >= workArea.Right)
        {
            X = workArea.Right - PetWidth;
            VelocityX = -Math.Abs(VelocityX);
            FacingRight = false;
        }

        _walkAnimTimer += dt;
        if (_walkAnimTimer >= 0.18)
        {
            _walkAnimTimer = 0;
            _walkToggle = !_walkToggle;
        }
        CurrentFrame = _dizzyTimer > 0
            ? SpriteFrame.HitDizzy
            : (_walkToggle ? SpriteFrame.Walk1 : SpriteFrame.Walk2);

        _stateTimer -= dt;
        if (_stateTimer <= 0)
        {
            PickNextAutonomousState();
        }
    }

    private void StepStationary(double dt, Rect workArea)
    {
        Y = workArea.Bottom - PetHeight;
        TiltAngle = 0;

        CurrentFrame = _dizzyTimer > 0
            ? SpriteFrame.HitDizzy
            : State switch
            {
                PetBehaviorState.Sitting => SpriteFrame.Sitting,
                PetBehaviorState.Sleeping => SpriteFrame.Sleeping,
                _ => SpriteFrame.Idle
            };

        _stateTimer -= dt;
        if (_stateTimer <= 0)
        {
            PickNextAutonomousState();
        }
    }

    private void PickNextAutonomousState()
    {
        double roll = _rng.NextDouble();
        if (roll < 0.56)
        {
            bool goRight = _rng.NextDouble() < 0.5;
            FacingRight = goRight;
            VelocityX = (goRight ? 1.0 : -1.0) * (WalkSpeed + _rng.Next(-18, 28));
            EnterState(PetBehaviorState.Walking, _rng.NextDouble() * 5.0 + 3.0);
        }
        else if (roll < 0.78)
        {
            VelocityX = 0;
            EnterState(PetBehaviorState.Idle, _rng.NextDouble() * 3.0 + 1.8);
        }
        else if (roll < 0.91)
        {
            VelocityX = 0;
            EnterState(PetBehaviorState.Sitting, _rng.NextDouble() * 5.0 + 3.5);
        }
        else
        {
            VelocityX = 0;
            EnterState(PetBehaviorState.Sleeping, _rng.NextDouble() * 7.0 + 5.0);
        }
    }

    private void EnterState(PetBehaviorState next, double durationSeconds)
    {
        State = next;
        _stateTimer = durationSeconds;
    }
}
