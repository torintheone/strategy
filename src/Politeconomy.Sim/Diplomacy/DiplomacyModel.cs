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
public static partial class DiplomacyModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.DiplomacySystem()];

    public static void Setup(World world)
    {
        var d = world.Diplomacy = new WorldDiplomacy();
        var data = world.Data.Diplomacy;
        bool Exists(string id) => world.ById(id) is not null;
        foreach (var bloc in data.Blocs)
            d.Blocs[bloc.Id] = bloc.Members.Where(Exists).Distinct().OrderBy(m => m, StringComparer.Ordinal).ToList();
        foreach (var s in data.Sanctions)
            foreach (var by in s.By.Where(by => Exists(by) && Exists(s.Target) && by != s.Target))
                d.Sanctions.Add(new Sanction(by, s.Target, s.Level, world.Date));
        d.CouncilSanctions.AddRange(data.Council.Sanctions.Where(Exists));
        d.Seats.AddRange(data.Council.Elected.Where(e => Exists(e.Country)).Select(e => new CouncilSeat(e.Country, e.Until)));
        foreach (var a in data.Alliances.Where(a => Exists(a.A) && Exists(a.B)))
            d.Treaties.Add(new Treaty(a.A, a.B, "alliance", world.Date));
        Ensure(world);
        Update(world);
    }

    /// <summary>Пересчитать то, что не сохраняется: порядок стран, торговлю пар, отношения (после загрузки — тоже).</summary>
    public static void Ensure(World world)
    {
        var d = world.Diplomacy ??= new WorldDiplomacy();
        if (d.Ids.Length == world.Countries.Count && d.Trade.Length == d.Ids.Length && d.Relations.Length == d.Ids.Length
            && d.Gravity.Length == d.Ids.Length)
            return;
        d.Ids = world.Countries.Select(c => c.Id).ToArray();
        d.Index = d.Ids.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i, StringComparer.Ordinal);
        foreach (var c in world.Countries)
            if (!d.Countries.ContainsKey(c.Id))
                d.Countries[c.Id] = new CountryDiplomacy { Mood = new double[ClassIds.Count] };
        d.Gravity = Gravity(world, d);
        (_, d.StartAccess, d.StartPressure) = Trade(world, d, StartSetting(world));
        RecomputeTrade(world);
        d.Neighbors = world.Data.Map.Countries.SelectMany(c => c.Neighbors.Select(n => WorldDiplomacy.Key(c.Id, n))).ToHashSet(StringComparer.Ordinal);
        d.History = world.Data.Diplomacy.History.GroupBy(h => WorldDiplomacy.Key(h.A, h.B))
            .ToDictionary(g => g.Key, g => g.Sum(h => h.Value), StringComparer.Ordinal);
        d.Relations = d.Ids.Select(_ => new double[d.Ids.Length]).ToArray();
        foreach (var id in d.Ids.Where(id => !d.Stances.ContainsKey(id)))
            d.Stances[id] = LiveStance(world, id);
        Recompute(world);
    }

    /// <summary>Строй, партия главы и её взгляды сейчас.</summary>
    static Stance LiveStance(World world, string id) =>
        new(world[id].Regime, world.Government?.Countries.GetValueOrDefault(id)?.Leader.Party,
            Politics.PoliticsModel.HeadParty(world, id)?.Position.ToArray());

    /// <summary>
    /// Торговля пары по гравитационной модели: ВВП × ВВП / расстояние^TradeDistance между столицами (по карте, с
    /// переходом через край), у соседей × TradeNeighbor. Без блоков, санкций и договоров — их учитывает <see cref="Trade"/>.
    /// </summary>
    static double[][] Gravity(World world, WorldDiplomacy d)
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
            shares[i] = row;
        }
        return shares;
    }

    /// <summary>Что меняет торговлю и давление на страны: санкции (кто, против кого, уровень), санкции Совета, блоки, договоры о свободной торговле.</summary>
    sealed record Setting(
        IReadOnlyList<(string By, string Target, string Level)> Sanctions,
        IReadOnlyList<string> Council,
        IReadOnlyList<(BlocRecord Bloc, IReadOnlyList<string> Members)> Blocs,
        IReadOnlyList<(string A, string B)> FreeTrade);

    static Setting CurrentSetting(World world)
    {
        var d = world.Diplomacy!;
        return new Setting(
            d.Sanctions.Select(x => (x.By, x.Target, x.Level)).ToList(),
            d.CouncilSanctions,
            world.Data.Diplomacy.Blocs.Select(b => (b, (IReadOnlyList<string>)(d.Blocs.GetValueOrDefault(b.Id) ?? []))).ToList(),
            d.Treaties.Where(t => t.Type == "free_trade").Select(t => (t.A, t.B)).ToList());
    }

    /// <summary>Как было 1 января 2026 года — по данным, а не по сохранению: от этого считаются поправки к экономике.</summary>
    static Setting StartSetting(World world)
    {
        var data = world.Data.Diplomacy;
        return new Setting(
            data.Sanctions.SelectMany(x => x.By.Select(by => (by, x.Target, x.Level))).ToList(),
            data.Council.Sanctions,
            data.Blocs.Select(b => (b, b.Members)).ToList(),
            []);
    }

    /// <summary>
    /// Торговля с учётом блоков, санкций и договоров. Пара: × (1 − самые жёсткие санкции между ними) × (1 + глубина
    /// общего торгового блока) × (1 + FreeTrade при договоре). Доли — от всей торговли страны; доступ к рынкам —
    /// торговля с поправками / без них; давление — доля мирового ВВП у санкционеров (частичные — SanctionPartial / SanctionFull).
    /// </summary>
    static (double[][] Shares, double[] Access, double[] Pressure) Trade(World world, WorldDiplomacy d, Setting setting)
    {
        var b = world.Data.DiplomacyBalance;
        var n = d.Ids.Length;
        int Pair(int i, int j) => i < j ? i * n + j : j * n + i;
        var cut = new Dictionary<int, double>();
        var directed = new Dictionary<int, double>();
        void Cut(int by, int target, double value)
        {
            var k = Pair(by, target);
            cut[k] = Math.Max(cut.GetValueOrDefault(k), value);
            var dk = by * n + target;
            directed[dk] = Math.Max(directed.GetValueOrDefault(dk), value / b.SanctionFull);
        }
        foreach (var (by, target, level) in setting.Sanctions)
            if (d.Index.TryGetValue(by, out var i) && d.Index.TryGetValue(target, out var j) && i != j)
                Cut(i, j, level == "full" ? b.SanctionFull : b.SanctionPartial);
        foreach (var target in setting.Council)
            if (d.Index.TryGetValue(target, out var j))
                for (var i = 0; i < n; i++)
                    if (i != j && world[d.Ids[i]].Info.UnMember)
                        Cut(i, j, b.SanctionPartial);
        var boost = new Dictionary<int, double>();
        foreach (var (bloc, members) in setting.Blocs)
        {
            if (bloc.Trade <= 0)
                continue;
            var idx = members.Where(d.Index.ContainsKey).Select(m => d.Index[m]).ToArray();
            for (var x = 0; x < idx.Length; x++)
                for (var y = x + 1; y < idx.Length; y++)
                {
                    var k = Pair(idx[x], idx[y]);
                    boost[k] = Math.Max(boost.GetValueOrDefault(k), bloc.Trade);
                }
        }
        var free = new HashSet<int>();
        foreach (var (a, c) in setting.FreeTrade)
            if (d.Index.TryGetValue(a, out var i) && d.Index.TryGetValue(c, out var j))
                free.Add(Pair(i, j));

        var gdp = StartGdp(world, d);
        var world0 = gdp.Sum();
        var pressure = new double[n];
        foreach (var (k, value) in directed)
            pressure[k % n] += value * gdp[k / n] / world0;

        var shares = new double[n][];
        var access = new double[n];
        for (var i = 0; i < n; i++)
        {
            var row = new double[n];
            double sum = 0, raw = 0;
            for (var j = 0; j < n; j++)
            {
                if (i == j)
                    continue;
                var k = Pair(i, j);
                var m = (1 - cut.GetValueOrDefault(k)) * (1 + boost.GetValueOrDefault(k)) * (free.Contains(k) ? 1 + b.FreeTrade : 1);
                row[j] = d.Gravity[i][j] * m;
                sum += row[j];
                raw += d.Gravity[i][j];
            }
            for (var j = 0; j < n; j++)
                row[j] = sum > 0 ? row[j] / sum : 0;
            shares[i] = row;
            access[i] = raw > 0 ? sum / raw : 1;
        }
        return (shares, access, Array.ConvertAll(pressure, p => Math.Min(1, p)));
    }

    /// <summary>ВВП стран на старте (долл.) в порядке Ids.</summary>
    static double[] StartGdp(World world, WorldDiplomacy d) =>
        d.Ids.Select(id => Math.Max(1e6, world[id].Info.Stats.GetValueOrDefault("gdp_usd")?.Value ?? 1e9)).ToArray();

    /// <summary>Торговля, доступ к рынкам, санкционное давление и отношения от блоков и санкций — по нынешнему состоянию.</summary>
    static void RecomputeTrade(World world)
    {
        var d = world.Diplomacy!;
        (d.Trade, d.Access, d.Pressure) = Trade(world, d, CurrentSetting(world));
        d.Ties = Ties(world);
    }

    /// <summary>Отношения от блоков (общий блок +, соперничающие военные блоки −) и санкций (по каждой стороне) по парам.</summary>
    static Dictionary<string, (double Blocs, double Sanctions)> Ties(World world)
    {
        var d = world.Diplomacy!;
        var b = world.Data.DiplomacyBalance;
        var ties = new Dictionary<string, (double Blocs, double Sanctions)>(StringComparer.Ordinal);
        void Add(string x, string y, double blocs, double sanctions)
        {
            if (x == y)
                return;
            var key = WorldDiplomacy.Key(x, y);
            var t = ties.GetValueOrDefault(key);
            ties[key] = (t.Blocs + blocs, t.Sanctions + sanctions);
        }
        foreach (var bloc in world.Data.Diplomacy.Blocs)
        {
            var members = d.Blocs.GetValueOrDefault(bloc.Id) ?? [];
            var value = bloc.Military ? b.BlocMilitaryRelations : b.BlocTradeRelations;
            for (var x = 0; x < members.Count; x++)
                for (var y = x + 1; y < members.Count; y++)
                    Add(members[x], members[y], value, 0);
            if (bloc.Military && bloc.Rival is { } rival && string.CompareOrdinal(bloc.Id, rival) < 0)
                foreach (var m in members)
                    foreach (var r in d.Blocs.GetValueOrDefault(rival) ?? [])
                        Add(m, r, -b.RivalRelations, 0);
        }
        foreach (var s in d.Sanctions)
            Add(s.By, s.Target, 0, -b.SanctionRelations * (s.Level == "full" ? 1 : 0.5));
        return ties;
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
        if (world.Date.Month == 1)
            ElectCouncil(world);
        ReviewPrograms(world);
        foreach (var id in d.Ids)
            d.Stances[id] = LiveStance(world, id);
        RecomputeTrade(world);
        Recompute(world);
        foreach (var country in world.Countries)
            Apply(world, country);
        UpdatePower(world);
        Publish(world);
    }

    /// <summary>
    /// День: если где-то сменились строй или партия главы (выборы, переворот, отставка), отношения пересчитываются
    /// сразу, а не 1-го числа, — иначе число и его разбор расходятся до конца месяца.
    /// </summary>
    public static void Sync(World world)
    {
        if (world.Diplomacy is not { } d || d.Ids.Length == 0)
            return;
        var changed = new List<int>();
        for (var i = 0; i < d.Ids.Length; i++)
        {
            var id = d.Ids[i];
            var regime = world[id].Regime;
            var party = world.Government?.Countries.GetValueOrDefault(id)?.Leader.Party;
            if (d.Stances.TryGetValue(id, out var stance) && stance.Regime == regime && stance.Party == party)
                continue;
            d.Stances[id] = LiveStance(world, id);
            changed.Add(i);
        }
        if (changed.Count == 0)
            return;
        // Договоры и торговля от власти не зависят — пересчитываются только отношения с этими странами.
        Recompute(world, changed);
        Publish(world);
    }

    /// <summary>Отношения всех пар — то же, что сумма <see cref="Breakdown"/>, но без списков причин (19,5 тыс. пар в месяц).</summary>
    /// <param name="only">Если задано — только пары с этими странами (сменилась власть), остальные не изменились.</param>
    static void Recompute(World world, IReadOnlyList<int>? only = null)
    {
        var d = world.Diplomacy!;
        var bal = world.Data.DiplomacyBalance;
        var n = d.Ids.Length;
        var regimes = d.Ids.Select(id => d.Stances[id].Regime).ToArray();
        var positions = d.Ids.Select(id => d.Stances[id].Position).ToArray();
        d.Version++;
        var treaties = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var t in d.Treaties)
        {
            var key = WorldDiplomacy.Key(t.A, t.B);
            treaties[key] = treaties.GetValueOrDefault(key) + (world.Data.Diplomacy.Treaties.FirstOrDefault(r => r.Id == t.Type)?.Relations ?? 0);
        }
        void Pair(int i, int j)
        {
            var key = WorldDiplomacy.Key(d.Ids[i], d.Ids[j]);
            var ties = d.Ties.GetValueOrDefault(key);
            var value = bal.TradeRelations * Math.Sqrt(Math.Max(d.Trade[i][j], d.Trade[j][i]))
                        + Regime(bal, regimes[i], regimes[j])
                        + (positions[i] is { } pa && positions[j] is { } pb ? Ideology(bal, pa, pb) : 0)
                        + (d.Neighbors.Contains(key) ? bal.Neighbors : 0)
                        + d.History.GetValueOrDefault(key) + treaties.GetValueOrDefault(key) + d.Memory.GetValueOrDefault(key)
                        + ties.Blocs + ties.Sanctions;
            d.Relations[i][j] = d.Relations[j][i] = Math.Clamp(value, -100, 100);
        }
        if (only == null)
        {
            for (var i = 0; i < n; i++)
                for (var j = i + 1; j < n; j++)
                    Pair(i, j);
            return;
        }
        // Пара всегда считается в одном порядке (меньший индекс первым) — число совпадает с полным пересчётом до бита.
        foreach (var k in only)
            for (var j = 0; j < n; j++)
                if (j != k)
                    Pair(Math.Min(k, j), Math.Max(k, j));
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
        var (sa, sb) = (d.Stances[a], d.Stances[b]);
        var list = new List<StatFactor>();
        list.Add(new StatFactor("Торговля", bal.TradeRelations * Math.Sqrt(Math.Max(d.Trade[i][j], d.Trade[j][i])), "number0"));
        list.Add(new StatFactor("Строй", Regime(bal, sa.Regime, sb.Regime), "number0"));
        if (sa.Position is { } pa && sb.Position is { } pb)
            list.Add(new StatFactor("Взгляды правящих партий", Ideology(bal, pa, pb), "number0"));
        var key = WorldDiplomacy.Key(a, b);
        if (d.Neighbors.Contains(key))
            list.Add(new StatFactor("Соседи", bal.Neighbors, "number0"));
        if (d.History.TryGetValue(key, out var history) && history != 0)
            list.Add(new StatFactor("История", history, "number0"));
        var treaties = d.Treaties.Where(t => WorldDiplomacy.Key(t.A, t.B) == key)
            .Sum(t => data.Diplomacy.Treaties.FirstOrDefault(r => r.Id == t.Type)?.Relations ?? 0);
        if (treaties != 0)
            list.Add(new StatFactor("Договоры", treaties, "number0"));
        if (d.Ties.TryGetValue(key, out var ties))
        {
            if (ties.Blocs != 0)
                list.Add(new StatFactor("Блоки", ties.Blocs, "number0"));
            if (ties.Sanctions != 0)
                list.Add(new StatFactor("Санкции", ties.Sanctions, "number0"));
        }
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
    public static double Weight(World world, string country, string partner)
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
        // Защита инвестиций и безвизовый режим со многими партнёрами — не больше, чем с одним, у кого весь мир:
        // сумма весов партнёров не больше 1.
        double investment = 0, visa = 0;
        foreach (var t in d.Treaties.Where(t => t.Involves(country.Id)))
        {
            var partner = t.Other(country.Id);
            var share = TradeShare(world, country.Id, partner);
            var weight = Weight(world, country.Id, partner);
            switch (t.Type)
            {
                case "free_trade":
                    c.Mood[ClassIds.Skilled] -= b.ImportMood * share;
                    c.Mood[ClassIds.Farmers] -= b.ImportMood * share;
                    break;
                case "investment":
                    investment += weight;
                    break;
                case "visa_free":
                    visa += weight;
                    break;
                case "energy":
                    if (EnergyExporter(world, country.Id))
                        Add(c, "potential_growth", b.EnergyGrowth * weight);
                    else
                        c.Effects["fuel_cost"] = -b.EnergyFuel;
                    break;
            }
        }
        if (investment > 0)
            Add(c, "potential_growth", b.InvestmentGrowth * Math.Min(1, investment));
        if (visa > 0)
        {
            visa = Math.Min(1, visa);
            Add(c, "potential_growth", b.VisaGrowth * visa);
            Add(c, "migration", b.VisaMigration * visa);
            c.Mood[ClassIds.Unskilled] -= b.VisaMood * visa;
        }
        // Блоки, санкции и договоры о свободной торговле — через доступ к рынкам, в сравнении со стартом.
        var i = d.Index[country.Id];
        var trade = d.StartAccess[i] > 0 ? d.Access[i] / d.StartAccess[i] : 1;
        c.TradeFactor = trade;
        c.Productivity = 1 + b.TradeGain * e.Openness * (trade - 1);
        // Санкции отрезают от денег: медленнее рост, дороже долг (тоже в сравнении со стартом).
        var pressure = d.Pressure[i] - d.StartPressure[i];
        if (Math.Abs(pressure) > 1e-9)
        {
            Add(c, "potential_growth", -b.SanctionGrowth * pressure);
            Add(c, "spread", b.SanctionSpread * pressure);
        }
        if (d.Programs.TryGetValue(country.Id, out var program))
        {
            if (program.Status == "active")
                Add(c, "spread", -b.FundSpread);
            else if (program.PenaltyUntil is { } until && world.Date < until)
                Add(c, "spread", b.FundPenalty);
        }
    }

    static void Add(CountryDiplomacy c, string key, double value) => c.Effects[key] = c.Effects.GetValueOrDefault(key) + value;

    /// <summary>Отношения и торговля каждой страны со страной игрока — для досье и карты.</summary>
    static void Publish(World world)
    {
        var year = world.Date.Year;
        var player = world.PlayerCountry;
        var d = world.Diplomacy!;
        var assembly = world.Countries.Count(c => c.Info.UnMember);
        var against = d.Sanctions.GroupBy(s => s.Target).ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
        foreach (var country in world.Countries)
        {
            // Сколько стран ограничивают торговлю со страной: санкции Совета Наций соблюдают все его члены.
            var list = against.GetValueOrDefault(country.Id) ?? [];
            var council = d.CouncilSanctions.Contains(country.Id);
            var count = council ? assembly - (country.Info.UnMember ? 1 : 0) : list.Select(s => s.By).Distinct().Count();
            var factors = new List<StatFactor>();
            if (council)
                factors.Add(new StatFactor($"Санкции «{world.Data.Diplomacy.Council.Name}»", count, "number0"));
            var full = list.Where(s => s.Level == "full").Select(s => s.By).Distinct().Count();
            if (full > 0)
                factors.Add(new StatFactor("Жёсткие", full, "number0"));
            if (list.Count(s => s.Level == "partial") is var partial and > 0)
                factors.Add(new StatFactor("Частичные", partial, "number0"));
            country.Stats["sanctions_against"] = new StatValue(count, year, StatSources.Model, null, null, factors);

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
        if (type == "free_trade" && BlocsOf(world, a).FirstOrDefault(x => x.Trade > 0 && InBloc(world, x.Id, b)) is { } common)
            return (false, $"Вы уже вместе в «{common.Name}»: торговля между вами и так свободная.");
        if (type == "alliance" && (NeutralReason(world, a) ?? NeutralReason(world, b)) is { } neutral)
            return (false, neutral);
        if (type == "alliance" && BlocsOf(world, a).FirstOrDefault(x => x.Military && InBloc(world, x.Id, b)) is { } pact)
            return (false, $"Вы уже союзники по «{pact.Name}».");
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
        if (!Acts(world, a) || a == b || world.ById(b) is null)
            return false;
        var (accepts, reason) = Acceptance(world, a, b, type);
        var name = world.Data.Diplomacy.Treaties.FirstOrDefault(t => t.Id == type)?.Name ?? type;
        if (!accepts)
        {
            Note(world, a, "diplomacy", $"{world[b].Name} отказывается: «{name}». {reason}");
            return false;
        }
        world.Diplomacy!.Treaties.Add(new Treaty(a, b, type, world.Date));
        Note(world, a, "diplomacy", $"Подписан договор со {Instrumental(world, b)}: «{name}».");
        Refresh(world);
        return true;
    }

    /// <summary>Расторгнуть договор (игрок): отношения портятся.</summary>
    public static bool Cancel(World world, string a, string b, string type)
    {
        if (!Acts(world, a))
            return false;
        var d = world.Diplomacy!;
        var treaty = d.Treaties.FirstOrDefault(t => t.Type == type && WorldDiplomacy.Key(t.A, t.B) == WorldDiplomacy.Key(a, b));
        if (treaty == null)
            return false;
        d.Treaties.Remove(treaty);
        Remember(world, a, b, -world.Data.DiplomacyBalance.CancelRelations);
        Note(world, a, "diplomacy", $"Расторгнут договор со {Instrumental(world, b)}: «{world.Data.Diplomacy.Treaties.FirstOrDefault(t => t.Id == type)?.Name ?? type}».");
        Refresh(world);
        return true;
    }

    /// <summary>Визит главы (игрок): отношения лучше; к той же стране — не чаще раза в полгода; визит к недругу критикуют дома.</summary>
    public static bool Visit(World world, string a, string b)
    {
        var bal = world.Data.DiplomacyBalance;
        if (!Acts(world, a) || a == b || world.ById(b) is null || !CanVisit(world, a, b))
            return false;
        var relations = Relations(world, a, b);
        world.Diplomacy!.Visits[WorldDiplomacy.Key(a, b)] = world.Date;
        Remember(world, a, b, bal.VisitRelations);
        var criticism = relations < bal.VisitCriticismBelow && world.Government?.Countries.GetValueOrDefault(a) is { } g;
        if (criticism)
            world.Government![a].Leader.Popularity = Math.Clamp(world.Government[a].Leader.Popularity - bal.VisitCriticism, 1, 99);
        Note(world, a, "diplomacy", $"Визит главы в страну «{world[b].Name}»: отношения улучшились." + (criticism ? " Дома визит к недругу критикуют: популярность главы упала." : ""));
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
        if (!Acts(world, a) || a == b || world.ById(b) is null)
            return false;
        var (ea, eb) = (world.Economy![a], world.Economy![b]);
        var usd = bal.AidShare / 100 * ea.GdpUsd;
        var share = eb.GdpUsd > 0 ? usd / eb.GdpUsd : 0;
        ea.Debt += bal.AidShare / 100 * ea.NominalGdp;
        eb.Debt = Math.Max(0, eb.Debt - share * eb.NominalGdp);
        var gain = Math.Min(bal.AidMax, bal.AidRelations * Math.Log(1 + share * 100));
        Remember(world, a, b, gain);
        Note(world, a, "diplomacy", $"Помощь {Dative(world, b)}: {Fmt.Number(bal.AidShare, 2)}% нашего ВВП ({Fmt.Number(share * 100, 2)}% их ВВП). Отношения {Fmt.Signed(gain, 0)}.");
        Refresh(world);
        return true;
    }

    /// <summary>Выслать дипломатов (игрок): сигнал недовольства, отношения резко хуже.</summary>
    public static bool Expel(World world, string a, string b)
    {
        if (!Acts(world, a) || a == b || world.ById(b) is null)
            return false;
        Remember(world, a, b, -world.Data.DiplomacyBalance.ExpelRelations);
        Note(world, a, "diplomacy", $"Высланы дипломаты: {world[b].Name}. Отношения резко ухудшились.");
        Refresh(world);
        return true;
    }

    /// <summary>
    /// Нападение: отношения агрессора со всеми, кроме союзников, −Aggression (если есть спор за регионы или нарушено
    /// перемирие — вполовину), с жертвой и её союзниками — ещё −Aggression (с союзниками — вполовину).
    /// </summary>
    public static void Aggression(World world, string attacker, IReadOnlyList<string> victims, bool justified)
    {
        var b = world.Data.WarBalance;
        var k = justified ? 0.5 : 1;
        foreach (var c in world.Countries)
            if (c.Id != attacker && !Allies(world, attacker, c.Id))
                Remember(world, attacker, c.Id, -b.Aggression * k);
        for (var i = 0; i < victims.Count; i++)
            Remember(world, attacker, victims[i], -b.Aggression * (i == 0 ? 1 : 0.5));
        Refresh(world);
    }

    /// <summary>Закон принят: национализация разрывает договоры о защите инвестиций.</summary>
    public static void LawPassed(World world, string country, string law, string variant)
    {
        if (law != "state_ownership" || variant != "nationalization" || world.Diplomacy is not { } d)
            return;
        if (d.Programs.TryGetValue(country, out var program) && program.Status == "active")
            Fail(world, country, program, "национализация нарушила условия");
        foreach (var t in d.Treaties.Where(t => t.Type == "investment" && t.Involves(country)).ToList())
        {
            d.Treaties.Remove(t);
            Remember(world, country, t.Other(country), -world.Data.DiplomacyBalance.CancelRelations);
            world.Log(country, "diplomacy", $"Национализация разорвала договор о защите инвестиций со {Instrumental(world, t.Other(country))}: отношения ухудшились.");
        }
        Refresh(world);
    }

    static void Remember(World world, string a, string b, double value)
    {
        var d = world.Diplomacy!;
        var key = WorldDiplomacy.Key(a, b);
        d.Memory[key] = d.Memory.GetValueOrDefault(key) + value;
    }

    /// <summary>
    /// Действует ли страна: игрок за свою страну или ИИ этой страны в свой ход (<see cref="WorldDiplomacy.Actor"/>).
    /// </summary>
    static bool Acts(World world, string country) => world.PlayerCountry == country || world.Diplomacy?.Actor == country;

    /// <summary>Сообщение о действии: у игрока — в журнал, у ИИ — в его журнал решений.</summary>
    static void Note(World world, string country, string kind, string text)
    {
        if (world.Diplomacy is { Actor: { } actor, OnNote: { } note } && actor == country && world.PlayerCountry != country)
            note(text);
        else
            world.Log(country, kind, text);
    }

    /// <summary>
    /// Ход ИИ страны: действия разрешены, сообщения идут в note, пересчёт откладывается до конца хода всех стран
    /// (<see cref="Flush"/>) — иначе сотни действий в месяц пересчитывали бы мир сотни раз.
    /// </summary>
    public static void BeginTurn(World world, string country, Action<string> note)
    {
        var d = world.Diplomacy!;
        d.Actor = country;
        d.OnNote = note;
    }

    public static void EndTurn(World world)
    {
        var d = world.Diplomacy!;
        d.Actor = null;
        d.OnNote = null;
    }

    /// <summary>Пересчитать мир, если за ходы ИИ что-то изменилось.</summary>
    public static void Flush(World world)
    {
        if (world.Diplomacy is not { Dirty: true } d)
            return;
        d.Dirty = false;
        Refresh(world);
    }

    /// <summary>После действия игрока — сразу пересчитать торговлю, отношения и поправки (не ждать конца месяца).</summary>
    static void Refresh(World world)
    {
        if (world.Diplomacy!.Actor != null)
        {
            world.Diplomacy.Dirty = true;
            return;
        }
        RecomputeTrade(world);
        Recompute(world);
        foreach (var country in world.Countries)
            Apply(world, country);
        Publish(world);
    }

    static string Instrumental(World world, string id) => $"страной «{world[id].Name}»";

    static string Dative(World world, string id) => $"стране «{world[id].Name}»";
}
