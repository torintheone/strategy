using Godot;

namespace Politeconomy.Game.Map;

/// <summary>
/// Камера карты. Колесо — плавное приближение к курсору (точка под курсором остаётся на месте),
/// перетаскивание — сдвиг с инерцией после отпускания, WASD/стрелки — сдвиг, переходы к стране — плавный перелёт.
/// </summary>
public partial class MapCamera : Camera2D
{
    const float MaxZoom = 40f;
    const float KeyPanSpeed = 900f;
    const float ZoomSharpness = 14f;
    const float FlySharpness = 7f;
    const float InertiaDecay = 5.5f;

    Rect2 _bounds;
    float _minZoom = 0.1f;
    float _targetZoom = 1f;
    Vector2? _flyTo;
    Vector2? _anchorScreen;
    Vector2 _anchorWorld;
    Vector2 _velocity;
    Vector2 _dragVelocity;

    /// <summary>Камера сдвинулась или изменила масштаб.</summary>
    public event Action? Changed;

    public float ZoomLevel => Zoom.X;

    public void Setup(Rect2 worldBounds)
    {
        _bounds = worldBounds;
        PositionSmoothingEnabled = false;
        FitWorld(instant: true);
    }

    float FitZoom()
    {
        var view = GetViewportRect().Size;
        return Mathf.Min(view.X / _bounds.Size.X, (view.Y - 80) / _bounds.Size.Y);
    }

    public void FitWorld(bool instant = false)
    {
        var fit = FitZoom();
        _minZoom = fit * 0.85f;
        var center = _bounds.GetCenter() + new Vector2(0, 10 / fit);
        if (instant)
            SetView(center, fit);
        else
        {
            _anchorScreen = null;
            _targetZoom = fit;
            _flyTo = center;
        }
    }

    /// <summary>Приблизить или отдалить так, чтобы точка мира под курсором осталась под курсором.</summary>
    public void ZoomAt(Vector2 screenPos, float factor)
    {
        _targetZoom = Mathf.Clamp(_targetZoom * factor, _minZoom, MaxZoom);
        _anchorScreen = screenPos;
        _anchorWorld = ScreenToWorld(screenPos);
        _flyTo = null;
        _velocity = Vector2.Zero;
    }

    public void PanByScreen(Vector2 screenDelta)
    {
        Position -= screenDelta / Zoom.X;
        _anchorScreen = null;
        _flyTo = null;
        _velocity = Vector2.Zero;
        // Скорость перетаскивания для инерции — сглаженная по последним движениям мыши.
        var dt = Mathf.Max((float)GetProcessDeltaTime(), 1f / 240);
        _dragVelocity = _dragVelocity.Lerp(screenDelta / dt, 0.5f);
        Clamp();
    }

    /// <summary>Отпустили карту: она немного проезжает по инерции.</summary>
    public void ReleaseDrag()
    {
        _velocity = _dragVelocity.Length() > 60 ? _dragVelocity : Vector2.Zero;
        _dragVelocity = Vector2.Zero;
    }

    public void StartDrag() => _dragVelocity = _velocity = Vector2.Zero;

    /// <summary>Плавно перелететь к точке мира.</summary>
    public void CenterOn(Vector2 world)
    {
        _anchorScreen = null;
        _flyTo = world;
        _velocity = Vector2.Zero;
    }

    /// <summary>Сразу показать точку мира в центре экрана с масштабом «во столько-то раз ближе, чем весь мир».</summary>
    public void ShowAt(Vector2 world, float closer) =>
        SetView(world, Mathf.Clamp(FitZoom() * closer, _minZoom, MaxZoom));

    void SetView(Vector2 world, float zoom)
    {
        _targetZoom = zoom;
        Zoom = new Vector2(zoom, zoom);
        Position = world;
        _flyTo = null;
        _anchorScreen = null;
        _velocity = Vector2.Zero;
        Clamp();
    }

    public Vector2 ScreenToWorld(Vector2 screenPos) => Position + (screenPos - GetViewportRect().Size / 2) / Zoom.X;

    public override void _Process(double delta)
    {
        var dt = (float)delta;
        var moved = false;

        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (dir != Vector2.Zero)
        {
            Position += dir.Normalized() * KeyPanSpeed * dt / Zoom.X;
            _flyTo = null;
            moved = true;
        }

        if (!Mathf.IsEqualApprox(Zoom.X, _targetZoom, _targetZoom * 1e-4f))
        {
            var k = 1 - Mathf.Exp(-ZoomSharpness * dt);
            var zoom = Mathf.Exp(Mathf.Lerp(Mathf.Log(Zoom.X), Mathf.Log(_targetZoom), k));
            Zoom = new Vector2(zoom, zoom);
            if (_anchorScreen is { } screen)
                Position = _anchorWorld - (screen - GetViewportRect().Size / 2) / zoom;
            moved = true;
        }
        else if (Zoom.X != _targetZoom)
        {
            Zoom = new Vector2(_targetZoom, _targetZoom);
            moved = true;
        }

        if (_flyTo is { } target)
        {
            var k = 1 - Mathf.Exp(-FlySharpness * dt);
            Position = Position.Lerp(target, k);
            if (Position.DistanceTo(target) * Zoom.X < 0.3f)
            {
                Position = target;
                _flyTo = null;
            }
            moved = true;
        }

        if (_velocity.LengthSquared() > 1)
        {
            Position -= _velocity * dt / Zoom.X;
            _velocity *= Mathf.Exp(-InertiaDecay * dt);
            moved = true;
        }

        if (moved)
            Clamp();
    }

    void Clamp()
    {
        Position = new Vector2(
            Mathf.Clamp(Position.X, _bounds.Position.X, _bounds.End.X),
            Mathf.Clamp(Position.Y, _bounds.Position.Y, _bounds.End.Y));
        Changed?.Invoke();
    }
}
