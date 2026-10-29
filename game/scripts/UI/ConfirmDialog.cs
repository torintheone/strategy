namespace Politeconomy.Game.UI;

/// <summary>Подтверждение решения: заголовок, пояснение и две кнопки.</summary>
public partial class ConfirmDialog : ModalDialog
{
    Action? _confirmed;

    public void Setup() => InitDialog(520);

    public void Show(string title, string text, string confirm, Action confirmed, string cancel = "Отмена")
    {
        _confirmed = confirmed;
        Title.Text = title;
        Subtitle.Text = "";
        ClearContent();
        Content.AddChild(Line(text, 14, UiTheme.TextPrimary));
        ClearButtons();
        AddButton(cancel, false, () => Visible = false);
        AddButton(confirm, true, () =>
        {
            Visible = false;
            _confirmed?.Invoke();
        });
        Visible = true;
    }
}
