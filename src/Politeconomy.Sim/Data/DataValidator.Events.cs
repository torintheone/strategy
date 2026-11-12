using Politeconomy.Sim.Laws;

namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    static readonly HashSet<string> WorldPrices = ["oil_price", "ores_price", "food_price"];
    static readonly HashSet<string> MinisterTraits = ["corruption", "competence", "loyalty", "popularity"];
    static readonly HashSet<string> RelationTargets = ["partner", "neighbors", "powers", "all"];
    static readonly HashSet<string> Regimes = ["democracy", "hybrid", "authoritarian"];
    static readonly HashSet<string> Incomes = ["low", "lower_middle", "upper_middle", "high"];

    /// <summary>События (events.json): ссылки на показатели, законы, классы, группы, посты, страны; диапазоны; цепочки.</summary>
    static void ValidateEvents(GameData data, Action<string, string> add)
    {
        const string file = "events.json";
        var events = data.Events;
        var countries = data.Countries.Countries.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var categories = events.Categories.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var posts = data.Government.Posts.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var classes = data.Classes.Classes.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        var groups = data.InterestGroups.Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
        var indicators = data.Indicators.Indicators.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var spending = data.Budget.Spending.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
        var technologies = data.Science.Technologies.Select(t => t.Id).ToHashSet(StringComparer.Ordinal);
        var programs = data.Programs.Programs.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
        var systems = data.Politics.Systems.Keys.ToHashSet(StringComparer.Ordinal);
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var hazard in events.Hazards.Keys.Where(h => events.HazardNames?.ContainsKey(h) != true))
            add(file, $"угроза {hazard}: нет названия в hazard_names");
        foreach (var (hazard, table) in events.Hazards)
            foreach (var (country, level) in table)
            {
                if (!countries.Contains(country))
                    add(file, $"угроза {hazard}: нет страны {country}");
                if (level is < 1 or > 3)
                    add(file, $"угроза {hazard}, {country}: уровень {level} вне 1…3");
            }
        var b = data.EventsBalance;
        if (b.Rate <= 0 || b.Days.Count == 0 || b.Days.Any(d => d is < 1 or > 28) || b.DeadlineDays <= 0 || b.QuietDays < 0)
            add("balance.json", "events: rate и deadline_days должны быть больше нуля, days — числа месяца от 1 до 28");

        void Condition(string where, EventCondition x, bool subjectAllowed)
        {
            var kinds = new object?[] { x.Stat, x.World, x.Law, x.Regime, x.Income, x.System, x.Tech, x.Program, x.Class, x.Group, x.Funding, x.Minister, x.Partner, x.Hazard, x.War, x.Any }
                .Count(v => v != null);
            if (kinds != 1)
                add(file, $"{where}: в условии должно быть ровно одно «что проверяем», а их {kinds}");
            if (x.Min is { } min && x.Max is { } max && min > max)
                add(file, $"{where}: min {min} больше max {max}");
            var ranged = x.Stat != null || x.World != null || x.Class != null || x.Group != null || x.Funding != null || x.Minister != null;
            if (ranged && x.Min == null && x.Max == null)
                add(file, $"{where}: нужен min или max");
            if (x.Stat is { } stat && !indicators.Contains(stat))
                add(file, $"{where}: нет показателя «{stat}» в indicators.json");
            if (x.World is { } world && !WorldPrices.Contains(world))
                add(file, $"{where}: мир «{world}» — нужно oil_price, ores_price или food_price");
            if (x.Law is { } law)
            {
                var record = data.Laws.Laws.FirstOrDefault(l => l.Id == law);
                if (record == null)
                    add(file, $"{where}: нет закона «{law}»");
                else if (x.In is not { Count: > 0 } variants)
                    add(file, $"{where}: у условия по закону нужен список вариантов in");
                else
                    foreach (var v in variants.Where(v => record.Variants.All(r => r.Id != v)))
                        add(file, $"{where}: у закона «{law}» нет варианта «{v}»");
            }
            foreach (var r in (x.Regime ?? []).Where(r => !Regimes.Contains(r)))
                add(file, $"{where}: нет режима «{r}»");
            foreach (var i in (x.Income ?? []).Where(i => !Incomes.Contains(i)))
                add(file, $"{where}: нет группы дохода «{i}»");
            foreach (var s in (x.System ?? []).Where(s => !systems.Contains(s)))
                add(file, $"{where}: нет политической системы «{s}»");
            if (x.Tech is { } tech && !technologies.Contains(tech))
                add(file, $"{where}: нет технологии «{tech}»");
            if (x.Program is { } program && !programs.Contains(program))
                add(file, $"{where}: нет программы «{program}»");
            if (x.Class is { } cls)
            {
                if (!classes.Contains(cls))
                    add(file, $"{where}: нет класса «{cls}»");
                if (x.What is not (null or "satisfaction" or "radicalism"))
                    add(file, $"{where}: у класса what — satisfaction или radicalism");
            }
            if (x.Group is { } group && !groups.Contains(group))
                add(file, $"{where}: нет группы интересов «{group}»");
            if (x.Funding is { } item && !spending.Contains(item))
                add(file, $"{where}: нет статьи расходов «{item}»");
            if (x.Minister is { } post)
            {
                if (post != "any" && !posts.Contains(post))
                    add(file, $"{where}: нет поста «{post}»");
                if (x.What == null || !MinisterTraits.Contains(x.What))
                    add(file, $"{where}: у министра what — corruption, competence, loyalty или popularity");
                if (!subjectAllowed)
                    add(file, $"{where}: условие по министру допустимо только в when события");
            }
            if (x.Partner != null && !subjectAllowed)
                add(file, $"{where}: условие по партнёру допустимо только в when события");
            if (x.Partner?.Recent is { } recent && !categories.Contains(recent))
                add(file, $"{where}: у партнёра recent «{recent}» — такой категории нет");
            if (x.Hazard is { } hazard && !events.Hazards.ContainsKey(hazard))
                add(file, $"{where}: нет природной угрозы «{hazard}»");
            foreach (var nested in x.Any ?? [])
                Condition(where, nested, subjectAllowed: false);
        }

        foreach (var e in events.Events)
        {
            var where = $"событие {e.Id}";
            if (!ids.Add(e.Id))
                add(file, $"{where}: id повторяется");
            if (!categories.Contains(e.Category))
                add(file, $"{where}: нет категории «{e.Category}»");
            if (!posts.Contains(e.Reporter))
                add(file, $"{where}: нет поста докладчика «{e.Reporter}»");
            if (string.IsNullOrWhiteSpace(e.Title) || string.IsNullOrWhiteSpace(e.Text))
                add(file, $"{where}: нужны заголовок и текст");
            if (!e.Chain && e.Weight <= 0)
                add(file, $"{where}: вес должен быть больше нуля (или chain: true)");
            if (!e.Chain && e.CooldownMonths < 12)
                add(file, $"{where}: перезарядка меньше года — событие будет повторяться в тот же год");
            if (e.Scale is { } scale && (!indicators.Contains(scale.Stat) || scale.Factor <= 0 || scale.From == scale.To))
                add(file, $"{where}: scale — нужен показатель из indicators.json, разные from и to, factor больше нуля");
            foreach (var condition in e.When)
                Condition(where, condition, subjectAllowed: true);
            var subject = e.When.Any(c => c.Minister != null);
            var partner = e.When.Any(c => c.Partner != null);
            foreach (var token in new[] { "{subject}", "{subject_post}" })
                if (!subject && !e.Chain && (e.Title.Contains(token) || e.Text.Contains(token)))
                    add(file, $"{where}: в тексте {token}, но в условиях нет министра");
            if (!partner && !e.Chain && (e.Title.Contains("{partner}") || e.Text.Contains("{partner}")))
                add(file, $"{where}: в тексте {{partner}}, но в условиях нет партнёра");
            if (e.Options.Count is < 2 or > 4)
                add(file, $"{where}: вариантов {e.Options.Count}, а нужно от 2 до 4");
            if (e.Options.Count(o => o.Default) != 1)
                add(file, $"{where}: ровно один вариант должен быть по умолчанию (default: true)");
            if (e.Options.FirstOrDefault(o => o.Default) is { Requires.Count: > 0 })
                add(file, $"{where}: вариант по умолчанию не может иметь условий requires");
            var optionIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in e.Options)
            {
                var at = $"{where}, вариант {o.Id}";
                if (!optionIds.Add(o.Id))
                    add(file, $"{at}: id повторяется");
                if (string.IsNullOrWhiteSpace(o.Name) || string.IsNullOrWhiteSpace(o.Text))
                    add(file, $"{at}: нужны название и описание");
                if (o.Months is < 0 or > 600)
                    add(file, $"{at}: срок {o.Months} вне 0…600 месяцев");
                if (o.Months == 0 && (o.Effects is { Count: > 0 } || o.Moods is { Count: > 0 }))
                    add(file, $"{at}: поправки и настроение без срока (months: 0) не подействуют");
                foreach (var key in o.Effects?.Keys ?? [])
                {
                    if (!LawModel.IsKnownEffect(data, key))
                        add(file, $"{at}: эффект «{key}» модель не умеет применять");
                    else if (!data.Laws.Effects.ContainsKey(key))
                        add(file, $"{at}: эффект «{key}» не описан в laws.json → effects (нечем назвать его на экране)");
                }
                foreach (var key in (o.Moods?.Keys ?? []).Where(k => !classes.Contains(k)))
                    add(file, $"{at}: нет класса «{key}»");
                foreach (var key in (o.Groups?.Keys ?? []).Where(k => !groups.Contains(k)))
                    add(file, $"{at}: нет группы интересов «{key}»");
                foreach (var key in (o.Loyalty?.Keys ?? []).Where(k => k is not ("police" or "army")))
                    add(file, $"{at}: лояльность «{key}» — нужно police или army");
                if (Math.Abs(o.Cost) > 10)
                    add(file, $"{at}: цена {o.Cost}% ВВП — слишком много");
                if (Math.Abs(o.Popularity) > 20)
                    add(file, $"{at}: популярность {o.Popularity} вне −20…20");
                if (o.Relations is { } relations)
                {
                    if (!RelationTargets.Contains(relations.With))
                        add(file, $"{at}: отношения with «{relations.With}» — нужно partner, neighbors, powers или all");
                    if (relations.With == "partner" && !partner && !e.Chain)
                        add(file, $"{at}: отношения с партнёром, но в условиях события партнёра нет");
                }
                if (o.Subject != null)
                {
                    if (o.Subject != "fire" && !double.TryParse(o.Subject, System.Globalization.CultureInfo.InvariantCulture, out _))
                        add(file, $"{at}: subject — fire или число (сдвиг лояльности)");
                    if (!subject && !e.Chain)
                        add(file, $"{at}: действие с министром, но в условиях события министра нет");
                }
                foreach (var next in o.Next ?? [])
                {
                    if (events.Event(next.Event) is not { } target)
                        add(file, $"{at}: продолжение «{next.Event}» не найдено");
                    else if (!target.Chain)
                        add(file, $"{at}: продолжение «{next.Event}» должно быть помечено chain: true");
                    if (next.Months <= 0 || next.Chance is <= 0 or > 1)
                        add(file, $"{at}: у продолжения нужны months больше нуля и chance от 0 до 1");
                }
                foreach (var condition in o.Requires ?? [])
                    Condition(at, condition, subjectAllowed: false);
                if (o.Requires is { Count: > 0 } && string.IsNullOrWhiteSpace(o.RequiresText))
                    add(file, $"{at}: у варианта с условиями нужен requires_text — почему он закрыт");
                if (o.Ai is not { Count: 4 })
                    add(file, $"{at}: ai — четыре числа (рост, стабильность, сила, переизбрание)");
            }
        }
        foreach (var chain in events.Events.Where(e => e.Chain))
            if (!events.Events.Any(e => e.Options.Any(o => o.Next?.Any(n => n.Event == chain.Id) == true)))
                add(file, $"событие {chain.Id}: помечено chain, но ни один вариант к нему не ведёт");
    }
}
