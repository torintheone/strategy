using Politeconomy.Sim.Events;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// События страны игрока: что ждёт решения (с докладчиком, причинами и вариантами), последние решённые события,
/// последствия, которые ещё действуют.
/// </summary>
public sealed record EventsSnapshot(IReadOnlyList<PendingEventSnapshot> Pending, IReadOnlyList<EventNews> History,
    IReadOnlyList<ActiveEffectSnapshot> Active, int Version)
{
    public static EventsSnapshot? Capture(World world)
    {
        if (world.PlayerCountry is not { } id || world.Events?.Countries.GetValueOrDefault(id) is not { } ce)
            return null;
        var country = world[id];
        var data = world.Data;
        var g = world.Government?.Countries.GetValueOrDefault(id);
        var pending = new List<PendingEventSnapshot>();
        foreach (var instance in ce.Pending)
        {
            if (data.Events.Event(instance.Event) is not { } record)
                continue;
            var category = data.Events.Category(record.Category);
            var post = data.Government.Posts.FirstOrDefault(p => p.Id == record.Reporter);
            var reporter = g?.Minister(record.Reporter) is { } person ? CharacterSnapshot.Of(person, world.Date) : null;
            var options = record.Options.Select(o =>
            {
                var enabled = EventModel.Enabled(world, country, o);
                return new EventOptionSnapshot(o.Id, o.Name, EventText.Fill(world, country, record, instance, o.Text), EventText.Consequences(data, o),
                    enabled, enabled ? null : o.RequiresText, o.Default);
            }).ToList();
            pending.Add(new PendingEventSnapshot(instance.Id, record.Id, category?.Name ?? record.Category, category?.Color ?? "#888888",
                EventText.Fill(world, country, record, instance, record.Title), EventText.Fill(world, country, record, instance, record.Text),
                instance.Date, instance.Deadline, post?.Name ?? "", reporter, instance.Why.ToList(), options, instance.Region));
        }
        var active = ce.Active.Select(a =>
        {
            var record = data.Events.Event(a.Event);
            var option = record?.Option(a.Option);
            return record == null || option == null ? null
                : new ActiveEffectSnapshot(record.Title.Replace(": {region}", "").Replace("{partner}: ", "").Replace(": {partner}", "")
                    .Replace(": {subject_post} {subject}", "").Replace(": {subject_post}", ""), option.Name, a.Until, EventText.Consequences(data, option with
                {
                    Cost = 0, Popularity = 0, Groups = null, Loyalty = null, Relations = null, Subject = null, Next = null,
                }));
        }).Where(x => x != null).Cast<ActiveEffectSnapshot>().OrderBy(x => x.Until).ToList();
        return new EventsSnapshot(pending, ce.History.AsEnumerable().Reverse().Take(12).ToList(), active, ce.Version);
    }
}

/// <summary>
/// Событие, которое ждёт решения: номер, категория (название и цвет), заголовок и текст (с подставленными именами),
/// когда случилось и до какого дня надо решить, докладчик (пост и человек), почему случилось, варианты, регион.
/// </summary>
public sealed record PendingEventSnapshot(int Id, string Event, string Category, string Color, string Title, string Text, DateOnly Date,
    DateOnly Deadline, string ReporterPost, CharacterSnapshot? Reporter, IReadOnlyList<string> Why, IReadOnlyList<EventOptionSnapshot> Options,
    string? Region);

/// <summary>Вариант решения: название, описание, что будет (строки из данных), доступен ли и почему нет, сработает ли сам по истечении срока.</summary>
public sealed record EventOptionSnapshot(string Id, string Name, string Text, IReadOnlyList<string> Consequences, bool Enabled, string? Reason, bool Default);

/// <summary>Действующее последствие решения: событие, выбранный вариант, до какой даты, что именно действует.</summary>
public sealed record ActiveEffectSnapshot(string Title, string Option, DateOnly Until, IReadOnlyList<string> Lines);
