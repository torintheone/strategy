using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Diplomacy;

/// <summary>
/// Дипломатия. Торговля пары стран — по гравитационной модели (размер экономик и расстояние между столицами, соседям
/// больше). Отношения (−100…100) = торговля + сходство строя и взглядов правящих партий + соседство + история
/// (data/diplomacy.json) + договоры + память о событиях. Договоры меняют торговлю, инвестиции, туризм и миграцию,
/// энергию и науку. MODEL.md, «Дипломатия».
/// </summary>
public static class DiplomacyModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.DiplomacySystem()];

    public static void Setup(World world)
    {
        world.Diplomacy = new WorldDiplomacy();
        Ensure(world);
        Update(world);
    }

    /// <summary>Пересчитать то, что не сохраняется: порядок стран, торговлю пар, отношения (после загрузки — тоже).</summary>
    public static void Ensure(World world)
    {
        var d = world.Diplomacy ??= new WorldDiplomacy();
        if (d.Ids.Length == world.Countries.Count && d.Trade.Length == d.Ids.Length && d.Relations.Length == d.Ids.Length)
            return;
        d.Ids = world.Countries.Select(c => c.Id).ToArray();
        d.Index = d.Ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        foreach (var c in world.Countries)
            if (!d.Countries.ContainsKey(c.Id))
                d.Countries[c.Id] = new CountryDiplomacy { Mood = new double[ClassIds.Count] };
        d.Trade = TradeShares(world, d);
        d.Neighbors = world.Data.Map.Countries.SelectMany(c => c.Neighbors.Select(n => WorldDiplomacy.Key(c.Id, n))).ToHashSet(StringComparer.Ordinal);
        d.History = world.Data.Diplomacy.History.GroupBy(h => WorldDiplomacy.Key(h.A, h.B))
            .ToDictionary(g => g.Key, g => g.Sum(h => h.Value), StringComparer.Ordinal);
        d.Relations = d.Ids.Select(_ => new double[d.Ids.Length]).ToArray();
        Recompute(world);
    }

    /// <summary>
    /// Доли торговли: Trade[i][j] — какая доля торговли страны i приходится на j. Вес пары = ВВП × ВВП /
    /// расстояние^TradeDistance между столицами (по карте, с переходом через край), у соседей × TradeNeighbor.
    /// </summary>
    static double[][] TradeShares(World world, WorldDiplomacy d)
    {
        var data = world.Data;
        var b = data.DiplomacyBalance;
        var n = d.Ids.Length;
        var gdp = d.Ids.Select(id => Math.Max(1e6, world[id].Info.Stats.GetValueOrDefault("gdp_usd")?.Value ?? 1e9)).ToArray();
        var capitals = data.Cities.Cities.Where(c => c.Capital).GroupBy(c => c.Country).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        var map = data.Map.Countries.ToDictionary(c => c.Id, StringComparer.Ordinal);
        var points = d.Ids.Select(id => capitals.TryGetValue(id, out var city) ? ((double)city.X, (double)city.Y)
            : map.TryGetValue(id, out var m) && m.Label.Count >= 2 ? (m.Label[0], m.Label[1]) : (0.0, 0.0)).ToArray();
        var neighbors = d.Ids.Select(id => map.TryGetValue(id, out var m) ? m.Neighbors.ToHashSet(StringComparer.Ordinal) : []).ToArray();
        var width = data.Map.Width;
        var shares = new double[n][];
        for (var i = 0; i < n; i++)
        {
            var row = new double[n];
            for (var j = 0; j < n; j++)
            {
                if (i == j)
                    continue;
                var dx = Math.Abs(points[i].Item1 - points[j].Item1);
                dx = Math.Min(dx, Math.Max(0, width - dx));
                var dy = points[i].Item2 - points[j].Item2;
                var distance = Math.Sqrt(dx * dx + dy * dy) + 5;
                row[j] = gdp[i] * gdp[j] / Math.Pow(distance, b.TradeDistance) * (neighbors[i].Contains(d.Ids[j]) ? b.TradeNeighbor : 1);
            }
            var sum = row.Sum();
            for (var j = 0; j < n; j++)
                row[j] = sum > 0 ? row[j] / sum : 0;
            shares[i] = row;
        }
        return shares;
    }

    /// <summary>Месяц: память о событиях тает, отношения пересчитываются, договоры действуют, показатели для игрока.</summary>
    public static void Update(World world)
    {
        Ensure(world);
        var d = world.Diplomacy!;
        var b = world.Data.DiplomacyBalance;
        var fade = 1 - 1 / (12 * b.MemoryYears);
        foreach (var key in d.Memory.Keys.ToList())
        {
            d.Memory[key] *= fade;
            if (Math.Abs(d.Memory[key]) < 0.05)
                d.Memory.Remove(key);
        }
        Recompute(world);
        foreach (var country in world.Countries)
            Apply(world, country);
        Publish(world);
    }

    /// <summary>Отношения всех пар — то же, что сумма <see cref="Breakdown"/>, но без списков причин (19,5 тыс. пар в месяц).</summary>
    static void Recompute(World world)
    {
        var d = world.Diplomacy!;
        var bal = world.Data.DiplomacyBalance;
        var n = d.Ids.Length;
        var regimes = d.Ids.Select(id => world[id].Regime).ToArray();
        var positions = d.Ids.Select(id => Politics.PoliticsModel.HeadParty(world, id)?.Position).ToArray();
        var treaties = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var t in d.Treaties)
        {
            var key = WorldDiplomacy.Key(t.A, t.B);
            treaties[key] = treaties.GetValueOrDefault(key) + (world.Data.Diplomacy.Treaties.FirstOrDefault(r => r.Id == t.Type)?.Relations ?? 0);
        }
        for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var key = WorldDiplomacy.Key(d.Ids[i], d.Ids[j]);
                var value = bal.TradeRelations * Math.Sqrt(Math.Max(d.Trade[i][j], d.Trade[j][i]))
                            + Regime(bal, regimes[i], regimes[j])
                            + (positions[i] is { } pa && positions[j] is { } pb ? Ideology(bal, pa, pb) : 0)
                            + (d.Neighbors.Contains(key) ? bal.Neighbors : 0)
                            + d.History.GetValueOrDefault(key) + treaties.GetValueOrDefault(key) + d.Memory.GetValueOrDefault(key);
                d.Relations[i][j] = d.Relations[j][i] = Math.Clamp(value, -100, 100);
            }
    }

    static double Regime(DiplomacyBalance bal, Regime a, Regime b) =>
        a == b ? bal.RegimeSame : (a, b) is (Model.Regime.Democracy, Model.Regime.Authoritarian) or (Model.Regime.Authoritarian, Model.Regime.Democracy) ? -bal.RegimeOpposite : 0;

    static double Ideology(DiplomacyBalance bal, double[] a, double[] b)
    {
        var sum = 0.0;
        for (var k = 0; k < a.Length && k < b.Length; k++)
            sum += (a[k] - b[k]) * (a[k] - b[k]);
        return Math.Clamp(bal.Ideology * (1 - Math.Sqrt(sum) / 100), -bal.Ideology, bal.Ideology);
    }

    /// <summary>Из чего складываются отношения пары: торговля, строй, взгляды правящих партий, соседство, история, договоры, события.</summary>
    public static List<StatFactor> Breakdown(World world, string a, string b)
    {
        var data = world.Data;
        var bal = data.DiplomacyBalance;
        var d = world.Diplomacy!;
        var (i, j) = (d.Index[a], d.Index[b]);
        var (ca, cb) = (world[a], world[b]);
        var list = new List<StatFactor>();
        list.Add(new StatFactor("Торговля", bal.TradeRelations * Math.Sqrt(Math.Max(d.Trade[i][j], d.Trade[j][i])), "number0"));
        list.Add(new StatFactor("Строй", Regime(bal, ca.Regime, cb.Regime), "number0"));
        if (Politics.PoliticsModel.HeadParty(world, a) is { } pa && Politics.PoliticsModel.HeadParty(world, b) is { } pb)
            list.Add(new StatFactor("Взгляды правящих партий", Ideology(bal, pa.Position, pb.Position), "number0"));
        var key = WorldDiplomacy.Key(a, b);
        if (d.Neighbors.Contains(key))
            list.Add(new StatFactor("Соседи", bal.Neighbors, "number0"));
        if (d.History.TryGetValue(key, out var history) && history != 0)
            list.Add(new StatFactor("История", history, "number0"));
        var treaties = d.Treaties.Where(t => WorldDiplomacy.Key(t.A, t.B) == key)
            .Sum(t => data.Diplomacy.Treaties.FirstOrDefault(r => r.Id == t.Type)?.Relations ?? 0);
        if (treaties != 0)
            list.Add(new StatFactor("Договоры", treaties, "number0"));
        if (d.Memory.TryGetValue(key, out var memory))
            list.Add(new StatFactor("События", memory, "number0"));
        return list;
    }

    /// <summary>Отношения пары (−100…100).</summary>
    public static double Relations(World world, string a, string b)
    {
        Ensure(world);
        var d = world.Diplomacy!;
        return a == b ? 100 : d.Relations[d.Index[a]][d.Index[b]];
    }

    /// <summary>Доля партнёра в торговле страны (0–1).</summary>
    public static double TradeShare(World world, string country, string partner)
    {
        Ensure(world);
        var d = world.Diplomacy!;
        return country == partner ? 0 : d.Trade[d.Index[country]][d.Index[partner]];
    }

    /// <summary>Вес партнёра для страны: его ВВП / сумма двух ВВП (на старте).</summary>
    static double Weight(World world, string country, string partner)
    {
        double Gdp(string id) => Math.Max(1e6, world[id].Info.Stats.GetValueOrDefault("gdp_usd")?.Value ?? 1e9);
        return Gdp(partner) / (Gdp(country) + Gdp(partner));
    }

    /// <summary>Экспортёр нефти и газа: рента больше EnergyRent % ВВП.</summary>
    public static bool EnergyExporter(World world, string country)
    {
        var info = world[country].Info.Stats;
        return (info.GetValueOrDefault("oil_rents_pct_gdp")?.Value ?? 0) + (info.GetValueOrDefault("gas_rents_pct_gdp")?.Value ?? 0)
               > world.Data.DiplomacyBalance.EnergyRent;
    }

    /// <summary>Поправки страны от договоров: торговля, производительность, настроение классов, рост, миграция, топливо.</summary>
    static void Apply(World world, Country country)
    {
        var d = world.Diplomacy!;
        var b = world.Data.DiplomacyBalance;
        var c = d.Countries[country.Id];
        var e = world.Economy![country.Id];
        if (c.Mood.Length != ClassIds.Count)
            c.Mood = new double[ClassIds.Count];
        Array.Clear(c.Mood);
        c.Effects.Clear();
        var trade = 1.0;
        foreach (var t in d.Treaties.Where(t => t.Involves(country.Id)))
        {
            var partner = t.Other(country.Id);
            var share = TradeShare(world, country.Id, partner);
            var weight = Weight(world, country.Id, partner);
            switch (t.Type)
            {
                case "free_trade":
                    trade += share * b.FreeTrade;
                    c.Mood[ClassIds.Skilled] -= b.ImportMood * share;
                    c.Mood[ClassIds.Farmers] -= b.ImportMood * share;
                    break;
                case "investment":
                    Add(c, "potential_growth", b.InvestmentGrowth * weight);
                    break;
                case "visa_free":
                    Add(c, "potential_growth", b.VisaGrowth * weight);
                    Add(c, "migration", b.VisaMigration * weight);
                    c.Mood[ClassIds.Unskilled] -= b.VisaMood * weight;
                    break;
                case "energy":
                    if (EnergyExporter(world, country.Id))
                        Add(c, "potential_growth", b.EnergyGrowth * weight);
                    else
                        c.Effects["fuel_cost"] = -b.EnergyFuel;
                    break;
            }
        }
        c.TradeFactor = trade;
        c.Productivity = 1 + b.TradeGain * e.Openness * (trade - 1);
    }

    static void Add(CountryDiplomacy c, string key, double value) => c.Effects[key] = c.Effects.GetValueOrDefault(key) + value;

    /// <summary>Отношения и торговля каждой страны со страной игрока — для досье и карты.</summary>
    static void Publish(World world)
    {
        var year = world.Date.Year;
        var player = world.PlayerCountry;
        foreach (var country in world.Countries)
        {
            if (player == null || player == country.Id || world.ById(player) is null)
            {
                country.Stats.Remove("relations_player");
                country.Stats.Remove("trade_share_player");
                continue;
            }
            country.Stats["relations_player"] = new StatValue(Relations(world, player, country.Id), year, StatSources.Model, null, null,
                Breakdown(world, player, country.Id));
            country.Stats["trade_share_player"] = new StatValue(TradeShare(world, player, country.Id) * 100, year, StatSources.Model, null, null, null);
        }
    }

    /// <summary>Партнёры по научному сотрудничеству.</summary>
    public static IEnumerable<string> SciencePartners(World world, string country) =>
        world.Diplomacy?.Treaties.Where(t => t.Type == "science" && t.Involves(country)).Select(t => t.Other(country)) ?? [];

    public static bool HasTreaty(World world, string a, string b, string type) =>
        world.Diplomacy?.Treaties.Any(t => t.Type == type && WorldDiplomacy.Key(t.A, t.B) == WorldDiplomacy.Key(a, b)) == true;

    /// <summary>Согласится ли партнёр на договор и почему (для подсказки игроку).</summary>
    public static (bool Accepts, string Reason) Acceptance(World world, string a, string b, string type)
    {
        var data = world.Data;
        var bal = data.DiplomacyBalance;
        if (data.Diplomacy.Treaties.FirstOrDefault(t => t.Id == type) is not { } record)
            return (false, "Такого договора нет.");
        if (HasTreaty(world, a, b, type))
            return (false, "Договор уже действует.");
        if (type == "energy" && EnergyExporter(world, a) == EnergyExporter(world, b))
            return (false, EnergyExporter(world, a) ? "Обе страны сами продают нефть и газ." : "Ни одна из стран не продаёт нефть и газ.");
        var need = record.MinRelations;
        var notes = new List<string>();
        if (type == "free_trade" && Politics.PoliticsModel.HeadParty(world, b) is { } party && party.Position[0] < bal.Protectionist)
        {
            need += bal.ProtectionistPenalty;
            notes.Add("правительство партнёра защищает своих производителей");
        }
        if (type == "alliance")
        {
            var enemy = world.Diplomacy!.Treaties.Where(t => t.Type == "alliance" && t.Involves(b)).Select(t => t.Other(b))
                .FirstOrDefault(x => x != a && Relations(world, a, x) < -40);
            if (enemy != null)
                return (false, $"У партнёра военный союз с вашим противником — {world[enemy].Name}.");
        }
        var relations = Relations(world, a, b);
        var text = $"Отношения {Fmt.Signed(relations, 0)}, нужно не меньше {Fmt.Signed(need, 0)}" + (notes.Count > 0 ? $" ({string.Join(", ", notes)})" : "") + ".";
        return (relations >= need, text);
    }

    /// <summary>Предложить договор (игрок): партнёр соглашается, если отношения достаточно хорошие.</summary>
    public static bool Propose(World world, string a, string b, string type)
    {
        if (world.PlayerCountry != a || a == b || world.ById(b) is null)
            return false;
        var (accepts, reason) = Acceptance(world, a, b, type);
        var name = world.Data.Diplomacy.Treaties.FirstOrDefault(t => t.Id == type)?.Name ?? type;
        if (!accepts)
        {
            world.Log(a, "diplomacy", $"{world[b].Name} отказывается: «{name}». {reason}");
            return false;
        }
        world.Diplomacy!.Treaties.Add(new Treaty(a, b, type, world.Date));
        world.Log(a, "diplomacy", $"Подписан договор с {Genitive(world, b)}: «{name}».");
        Refresh(world);
        return true;
    }

    /// <summary>Расторгнуть договор (игрок): отношения портятся.</summary>
    public static bool Cancel(World world, string a, string b, string type)
    {
        if (world.PlayerCountry != a)
            return false;
        var d = world.Diplomacy!;
        var treaty = d.Treaties.FirstOrDefault(t => t.Type == type && WorldDiplomacy.Key(t.A, t.B) == WorldDiplomacy.Key(a, b));
        if (treaty == null)
            return false;
        d.Treaties.Remove(treaty);
        Remember(world, a, b, -world.Data.DiplomacyBalance.CancelRelations);
        world.Log(a, "diplomacy", $"Расторгнут договор с {Genitive(world, b)}: «{world.Data.Diplomacy.Treaties.FirstOrDefault(t => t.Id == type)?.Name ?? type}».");
        Refresh(world);
        return true;
    }

    /// <summary>Визит главы (игрок): отношения лучше; к той же стране — не чаще раза в полгода; визит к недругу критикуют дома.</summary>
    public static bool Visit(World world, string a, string b)
    {
        var bal = world.Data.DiplomacyBalance;
        if (world.PlayerCountry != a || a == b || world.ById(b) is null || !CanVisit(world, a, b))
            return false;
        var relations = Relations(world, a, b);
        world.Diplomacy!.Visits[WorldDiplomacy.Key(a, b)] = world.Date;
        Remember(world, a, b, bal.VisitRelations);
        var criticism = relations < bal.VisitCriticismBelow && world.Government?.Countries.GetValueOrDefault(a) is { } g;
        if (criticism)
            world.Government![a].Leader.Popularity = Math.Clamp(world.Government[a].Leader.Popularity - bal.VisitCriticism, 1, 99);
        world.Log(a, "diplomacy", $"Визит главы в {world[b].Name}: отношения улучшились." + (criticism ? " Дома визит к недругу критикуют: популярность главы упала." : ""));
        Refresh(world);
        return true;
    }

    public static bool CanVisit(World world, string a, string b) =>
        world.Diplomacy?.Visits.TryGetValue(WorldDiplomacy.Key(a, b), out var last) != true
        || last.AddMonths(world.Data.DiplomacyBalance.VisitMonths) <= world.Date;

    /// <summary>Помощь (игрок): AidShare % своего ВВП — долг дарителя растёт, получателя уменьшается; отношения лучше тем сильнее, чем помощь заметнее для получателя.</summary>
    public static bool Aid(World world, string a, string b)
    {
        var bal = world.Data.DiplomacyBalance;
        if (world.PlayerCountry != a || a == b || world.ById(b) is null)
            return false;
        var (ea, eb) = (world.Economy![a], world.Economy![b]);
        var usd = bal.AidShare / 100 * ea.GdpUsd;
        var share = eb.GdpUsd > 0 ? usd / eb.GdpUsd : 0;
        ea.Debt += bal.AidShare / 100 * ea.NominalGdp;
        eb.Debt = Math.Max(0, eb.Debt - share * eb.NominalGdp);
        var gain = Math.Min(bal.AidMax, bal.AidRelations * Math.Log(1 + share * 100));
        Remember(world, a, b, gain);
        world.Log(a, "diplomacy", $"Помощь {Dative(world, b)}: {Fmt.Number(bal.AidShare, 2)}% нашего ВВП ({Fmt.Number(share * 100, 2)}% их ВВП). Отношения {Fmt.Signed(gain, 0)}.");
        Refresh(world);
        return true;
    }

    /// <summary>Выслать дипломатов (игрок): сигнал недовольства, отношения резко хуже.</summary>
    public static bool Expel(World world, string a, string b)
    {
        if (world.PlayerCountry != a || a == b || world.ById(b) is null)
            return false;
        Remember(world, a, b, -world.Data.DiplomacyBalance.ExpelRelations);
        world.Log(a, "diplomacy", $"Высланы дипломаты: {world[b].Name}. Отношения резко ухудшились.");
        Refresh(world);
        return true;
    }

    /// <summary>Закон принят: национализация разрывает договоры о защите инвестиций.</summary>
    public static void LawPassed(World world, string country, string law, string variant)
    {
        if (law != "state_ownership" || variant != "nationalization" || world.Diplomacy is not { } d)
            return;
        foreach (var t in d.Treaties.Where(t => t.Type == "investment" && t.Involves(country)).ToList())
        {
            d.Treaties.Remove(t);
            Remember(world, country, t.Other(country), -world.Data.DiplomacyBalance.CancelRelations);
            world.Log(country, "diplomacy", $"Национализация разорвала договор о защите инвестиций с {Genitive(world, t.Other(country))}: отношения ухудшились.");
        }
        Refresh(world);
    }

    static void Remember(World world, string a, string b, double value)
    {
        var d = world.Diplomacy!;
        var key = WorldDiplomacy.Key(a, b);
        d.Memory[key] = d.Memory.GetValueOrDefault(key) + value;
    }

    /// <summary>После действия игрока — сразу пересчитать отношения и поправки (не ждать конца месяца).</summary>
    static void Refresh(World world)
    {
        Recompute(world);
        foreach (var country in world.Countries)
            Apply(world, country);
        Publish(world);
    }

    static string Genitive(World world, string id) => $"страной «{world[id].Name}»";

    static string Dative(World world, string id) => $"стране «{world[id].Name}»";
}
