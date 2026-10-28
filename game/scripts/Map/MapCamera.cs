using Godot;

namespace Politeconomy.Game.Map;

/// <summary>Камера карты: колесо — масштаб к курсору, перетаскивание и WASD/стрелки — сдвиг.</summary>
public partial class MapCamera : Camera2D
{
    const float MaxZoom = 40f;
    const float KeyPanSpeed = 900f;

    Rect2 _bounds;
    float _minZoom = 0.1f;

    /// <summary>Камера сдвинулась или изменила масштаб.</summary>
    public event Action? Changed;

    public float ZoomLevel => Zoom.X;

    public void Setup(Rect2 worldBounds)
    {
        _bounds = worldBounds;
        PositionSmoothingEnabled = false;
        FitWorld();
    }

    public void FitWorld()
    {
        var view = GetViewportRect().Size;
        var fit = Mathf.Min(view.X / _bounds.Size.X, (view.Y - 80) / _bounds.Size.Y);
        _minZoom = fit * 0.85f;
        Zoom = new Vector2(fit, fit);
        Position = _bounds.GetCenter() + new Vector2(0, 10 / fit);
        Clamp();
    }

    /// <summary>Масштаб так, чтобы точка мира под курсором осталась под курсором.</summary>
    public void ZoomAt(Vector2 screenPos, float factor)
    {
        var center = GetViewportRect().Size / 2;
        var oldZoom = Zoom.X;
        var newZoom = Mathf.Clamp(oldZoom * factor, _minZoom, MaxZoom);
        if (Mathf.IsEqualApprox(newZoom, oldZoom))
            return;
        var world = Position + (screenPos - center) / oldZoom;
        Zoom = new Vector2(newZoom, newZoom);
        Position = world - (screenPos - center) / newZoom;
        Clamp();
    }

    public void PanByScreen(Vector2 screenDelta)
    {
        Position -= screenDelta / Zoom.X;
        Clamp();
    }

    /// <summary>Плавно не нужно: просто показать точку мира в центре экрана.</summary>
    public void CenterOn(Vector2 world)
    {
        Position = world;
        Clamp();
    }

    /// <summary>Точка мира в центре экрана, масштаб — во сколько раз ближе, чем «весь мир».</summary>
    public void ShowAt(Vector2 world, float closer)
    {
        var view = GetViewportRect().Size;
        var fit = Mathf.Min(view.X / _bounds.Size.X, (view.Y - 80) / _bounds.Size.Y);
        var zoom = Mathf.Clamp(fit * closer, _minZoom, MaxZoom);
        Zoom = new Vector2(zoom, zoom);
        Position = world;
        Clamp();
    }

    public Vector2 ScreenToWorld(Vector2 screenPos) => Position + (screenPos - GetViewportRect().Size / 2) / Zoom.X;

    public override void _Process(double delta)
    {
        var dir = Vector2.Zero;
        if (Input.IsKeyPressed(Key.A) || Input.IsKeyPressed(Key.Left)) dir.X -= 1;
        if (Input.IsKeyPressed(Key.D) || Input.IsKeyPressed(Key.Right)) dir.X += 1;
        if (Input.IsKeyPressed(Key.W) || Input.IsKeyPressed(Key.Up)) dir.Y -= 1;
        if (Input.IsKeyPressed(Key.S) || Input.IsKeyPressed(Key.Down)) dir.Y += 1;
        if (dir != Vector2.Zero)
        {
            Position += dir.Normalized() * KeyPanSpeed * (float)delta / Zoom.X;
            Clamp();
        }
    }

    void Clamp()
    {
        Position = new Vector2(
            Mathf.Clamp(Position.X, _bounds.Position.X, _bounds.End.X),
            Mathf.Clamp(Position.Y, _bounds.Position.Y, _bounds.End.Y));
        Changed?.Invoke();
    }
}
