using Godot;

namespace Politeconomy.Game;

/// <summary>
/// Настройки игрока, общие для всех партий: хранятся в user://settings.cfg (в папке данных Godot), а не в сохранении.
/// Пока одна — показывать телевыпуск окном или только в ленте; на этапе 8.5 сюда же лягут звук, голос и масштаб.
/// </summary>
public static class GameSettings
{
    const string Path = "user://settings.cfg";
    static ConfigFile? _file;

    static ConfigFile File
    {
        get
        {
            if (_file != null)
                return _file;
            _file = new ConfigFile();
            _file.Load(Path); // файла ещё нет — значения по умолчанию
            return _file;
        }
    }

    /// <summary>Телевыпуск по понедельникам открывается окном и ставит игру на паузу (иначе — только строка в ленте).</summary>
    public static bool BroadcastWindow
    {
        get => File.GetValue("news", "broadcast_window", true).AsBool();
        set
        {
            File.SetValue("news", "broadcast_window", value);
            File.Save(Path);
        }
    }
}
