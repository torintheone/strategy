using Godot;
using Politeconomy.Sim.Model;

namespace Politeconomy.Game.UI;

/// <summary>Уведомления под верхней панелью: новые записи журнала страны игрока. Каждое держится несколько секунд.</summary>
public partial class Toasts : VBoxContainer
{
    const int MaxShown = 3;
    const double Lifetime = 8;
    readonly HashSet<JournalEntry> _seen = [];

    public void Setup()
    {
        SetAnchorsPreset(LayoutPreset.CenterTop);
        GrowHorizontal = GrowDirection.Both;
        OffsetTop = 76;
        OffsetLeft = -280;
        OffsetRight = 280;
        MouseFilter = MouseFilterEnum.Ignore;
        AddThemeConstantOverride("separation", 6);
    }

    /// <summary>Показать новые записи журнала о стране игрока. Первый вызов только запоминает уже бывшие записи.</summary>
    public void Feed(IReadOnlyList<JournalEntry> journal, string? player, bool silent = false)
    {
        foreach (var e in journal)
        {
            if (!_seen.Add(e) || silent || e.Country != player)
                continue;
            Push(e.Text);
        }
    }

    public void Push(string text)
    {
        while (GetChildCount() >= MaxShown)
        {
            var oldest = GetChild(0);
            RemoveChild(oldest);
            oldest.QueueFree();
        }
        var toast = new Toast();
        AddChild(toast);
        toast.Setup(text, Lifetime);
    }
}
