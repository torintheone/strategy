namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    static readonly HashSet<string> NewsTones = ["good", "bad", "neutral"];

    /// <summary>Новости (news.json): рубрики, показатели, цены, источники, события; коэффициенты news в balance.json.</summary>
    static void ValidateNews(GameData data, Action<string, string> add)
    {
        const string file = "news.json";
        var news = data.News;
        var topics = new HashSet<string>(StringComparer.Ordinal);
        foreach (var t in news.Topics)
        {
            if (!topics.Add(t.Id))
                add(file, $"рубрика {t.Id} повторяется");
            if (string.IsNullOrWhiteSpace(t.Name))
                add(file, $"рубрика {t.Id}: нет названия");
            if (!IsHexColor(t.Color))
                add(file, $"рубрика {t.Id}: цвет «{t.Color}» — нужен вид #rrggbb");
        }

        void Source(string where, string topic, int weight, string tone)
        {
            if (!topics.Contains(topic))
                add(file, $"{where}: нет рубрики «{topic}»");
            if (weight is < 0 or > 100)
                add(file, $"{where}: важность {weight} вне 0…100");
            if (!NewsTones.Contains(tone))
                add(file, $"{where}: тон «{tone}» — нужно good, bad или neutral");
        }

        void Phrase(string where, NewsPhraseRecord p, int weight, params string[] allowed)
        {
            Source(where, news.Topics.Count > 0 ? news.Topics[0].Id : "", p.Weight ?? weight, p.Tone);
            if (string.IsNullOrWhiteSpace(p.Text))
                add(file, $"{where}: пустой текст");
            if (p.Min is { } min && p.Max is { } max && min > max)
                add(file, $"{where}: min {min} больше max {max}");
            foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(p.Text ?? "", @"\{[^}]*\}"))
                if (!allowed.Contains(m.Value))
                    add(file, $"{where}: неизвестная подстановка {m.Value}");
        }

        var indicators = data.Indicators.Indicators.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        var stats = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in news.Indicators)
        {
            var where = $"показатель {rule.Stat}";
            if (!indicators.Contains(rule.Stat))
                add(file, $"{where}: нет такого показателя в indicators.json");
            if (!stats.Add(rule.Stat))
                add(file, $"{where}: повторяется (у показателя одно правило)");
            if (rule.Step <= 0 || rule.Relative < 0)
                add(file, $"{where}: step должен быть больше нуля, relative — не меньше нуля");
            if (rule.Up is not { Count: > 0 } && rule.Down is not { Count: > 0 })
                add(file, $"{where}: нужна хотя бы одна формулировка в up или down");
            Source(where, rule.Topic, rule.Weight, "neutral");
            foreach (var p in (rule.Up ?? []).Concat(rule.Down ?? []))
                Phrase(where, p, rule.Weight, "{value}", "{abs}", "{was}", "{change}");
        }
        var prices = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rule in news.Prices)
        {
            var where = $"цена {rule.Price}";
            if (!WorldPrices.Contains(rule.Price))
                add(file, $"{where}: нужно oil_price, ores_price или food_price");
            if (!prices.Add(rule.Price))
                add(file, $"{where}: повторяется");
            if (rule.Step is <= 0 or >= 1)
                add(file, $"{where}: step — доля от 0 до 1");
            Source(where, rule.Topic, rule.Weight, "neutral");
            Phrase(where, rule.Up, rule.Weight, "{change}");
            Phrase(where, rule.Down, rule.Weight, "{change}");
        }
        foreach (var (id, s) in news.Sources)
        {
            Source($"источник {id}", s.Topic, s.Weight, s.Tone);
            if (s.Sentences < 0)
                add(file, $"источник {id}: sentences не может быть меньше нуля");
        }
        var categories = data.Events.Categories.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, s) in news.Events)
        {
            if (!categories.Contains(id))
                add(file, $"события: нет категории «{id}» в events.json");
            Source($"события {id}", s.Topic, s.Weight, s.Tone);
        }
        foreach (var c in categories.Where(c => !news.Events.ContainsKey(c)))
            add(file, $"события: у категории «{c}» нет рубрики и важности");

        var b = data.NewsBalance;
        if (b.HomeKeep < 20 || b.WorldKeep < 20)
            add("balance.json", "news: home_keep и world_keep — не меньше 20");
        if (b.WorldMin is < 0 or > 150 || b.HomeBonus < 0 || b.RankBonus < 0 || b.RankSpan <= 0 || b.NeighborBonus < 0 || b.AllyBonus < 0
            || b.EnemyBonus < 0 || b.TradeBonus < 0 || b.TradeShare < 0)
            add("balance.json", "news: поправки не могут быть отрицательными, rank_span — больше нуля");

        if (news.Broadcast is not { } tv)
            add(file, "нет раздела broadcast (тексты телевыпуска)");
        else
        {
            if (new[] { tv.Show, tv.Debut, tv.Home, tv.World, tv.HomeQuiet, tv.Markets }.Any(string.IsNullOrWhiteSpace))
                add(file, "broadcast: show, debut, home, world, home_quiet и markets не могут быть пустыми");
            if (tv.Openings is not { Count: > 0 } || tv.Closings is not { Count: > 0 })
                add(file, "broadcast: нужно хотя бы одно приветствие (openings) и прощание (closings)");
            if (tv.Figures is not { Stats.Count: > 0 } figures || string.IsNullOrWhiteSpace(figures.Lead) || string.IsNullOrWhiteSpace(figures.Title))
                add(file, "broadcast: в figures нужны lead, title и хотя бы один показатель");
            else
                foreach (var f in figures.Stats)
                    if (!indicators.Contains(f.Stat) || string.IsNullOrWhiteSpace(f.Label))
                        add(file, $"broadcast → figures: показатель {f.Stat} — нет в indicators.json или нет подписи");
            var all = new[] { tv.Debut, tv.Home, tv.World, tv.HomeQuiet, tv.Markets, tv.Figures?.Lead, tv.Figures?.Title }.Concat(tv.Openings ?? [])
                .Concat(tv.Closings ?? []).Concat(tv.Figures?.Stats.Select(f => f.Label) ?? []).OfType<string>();
            foreach (var text in all)
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text, @"\{[^}]*\}"))
                    if (m.Value is not ("{show}" or "{anchor}"))
                        add(file, $"broadcast: неизвестная подстановка {m.Value} в «{text}»");
                // Текст читает синтезатор речи: цифры с единицами и сокращения он прочтёт неверно.
                if (text.IndexOfAny(['%', '$', '№', '−']) >= 0)
                    add(file, $"broadcast: «{text}» — текст читается вслух, знаки %, $, №, − пишите словами");
            }
        }
        if (news.Newspaper is not { } paper)
            add(file, "нет раздела newspaper (тексты газеты)");
        else
        {
            foreach (var (id, press) in new[] { ("free", paper.Free), ("loyal", paper.Loyal), ("state", paper.State) })
                if (press is null || string.IsNullOrWhiteSpace(press.Name) || string.IsNullOrWhiteSpace(press.Motto)
                    || new[] { press.Good, press.Bad, press.Neutral, press.World }.Any(l => l is not { Count: > 0 } || l.Any(string.IsNullOrWhiteSpace)))
                    add(file, $"newspaper → {id}: нужны name, motto и хотя бы по одному абзацу в good, bad, neutral и world");
            if (paper.Praise is not { Count: > 0 } || new[] { paper.PraiseBody, paper.Quiet, paper.QuietBody, paper.Expose, paper.ExposeBody }.Any(string.IsNullOrWhiteSpace))
                add(file, "newspaper: нужны praise, praise_body, quiet, quiet_body, expose и expose_body");
            void Allowed(string? text, params string[] allowed)
            {
                foreach (System.Text.RegularExpressions.Match m in System.Text.RegularExpressions.Regex.Matches(text ?? "", @"\{[^}]*\}"))
                    if (!allowed.Contains(m.Value))
                        add(file, $"newspaper: неизвестная подстановка {m.Value} в «{text}»");
            }
            foreach (var t in (paper.Praise ?? []).Append(paper.PraiseBody))
                Allowed(t, "{leader}");
            Allowed(paper.Expose, "{post}", "{post_lower}", "{minister}", "{stat}", "{reported}", "{real}", "{a}");
            Allowed(paper.ExposeBody, "{post}", "{post_lower}", "{minister}", "{stat}", "{reported}", "{real}", "{a}");
            foreach (var press in new[] { paper.Free, paper.Loyal, paper.State }.Where(p => p != null))
                foreach (var t in press.Good.Concat(press.Bad).Concat(press.Neutral).Concat(press.World))
                    Allowed(t, "{leader}");
        }
        var pb = data.NewspaperBalance;
        if (pb.LoyalMin < 0 || pb.FreeMin <= pb.LoyalMin || pb.FreeMin > 100 || pb.Stories is < 1 or > 6 || pb.Keep < 1
            || pb.ExposeSpin < 0 || pb.ExposeGap < 0 || pb.ExposeWeight is < 0 or > 150 || pb.ExposeMonths < 1 || pb.ExposeEvery < 1)
            add("balance.json", "newspaper: нужно 0 ≤ loyal_min < free_min ≤ 100, stories от 1 до 6, keep, expose_months и expose_every — не меньше 1");
        var rb = data.ReportsBalance;
        if (rb.HonestCompetence is < 0 or > 100 || rb.CorruptFrom is < 0 or >= 100 || rb.BadAdviceCompetence is < 0 or > 100
            || new[] { rb.WeakSpin, rb.CorruptSpin, rb.TraitSpin, rb.PopulistSpin }.Any(x => x < 0) || rb.MaxSpin is < 0 or > 1)
            add("balance.json", "reports: пороги — от 0 до 100, прибавки — не меньше нуля, max_spin — от 0 до 1");

        var tb = data.BroadcastBalance;
        if (tb.StoriesMin < 1 || tb.StoriesMax < tb.StoriesMin || tb.StoriesMax > 8)
            add("balance.json", "broadcast: нужно 1 ≤ stories_min ≤ stories_max ≤ 8");
        if (tb.WindowDays is < 7 or > 60 || tb.Stale < 0 || tb.PerCountry < 1 || tb.Keep < 1)
            add("balance.json", "broadcast: window_days — от 7 до 60, stale — не меньше нуля, per_country и keep — не меньше 1");
        if (tb.AnchorAge is not [var a0, var a1] || a0 < 18 || a1 < a0 || tb.AnchorYears is not [var y0, var y1] || y0 < 1 || y1 < y0
            || tb.AnchorFemaleShare is < 0 or > 1 || tb.AnchorRetireAge <= (tb.AnchorAge is [_, var top] ? top : 0))
            add("balance.json", "broadcast: anchor_age и anchor_years — «от, до», anchor_female_share — от 0 до 1, anchor_retire_age — старше anchor_age");
    }

    static bool IsHexColor(string? s) => s is { Length: 7 } && s[0] == '#' && s.Skip(1).All(Uri.IsHexDigit);
}
