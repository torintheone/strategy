using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Engine;

/// <summary>
/// Неизменяемый снимок мира для интерфейса. Симуляция в фоновом потоке публикует новый снимок
/// после каждого шага, интерфейс читает последний — без блокировок и гонок.
/// </summary>
public sealed class WorldSnapshot
{
    public required DateOnly Date { get; init; }
    public required int Speed { get; init; }
    public required bool Paused { get; init; }
    public required string? PlayerCountry { get; init; }
    public required IReadOnlyDictionary<string, CountrySnapshot> Countries { get; init; }
    /// <summary>Регионы, которые контролирует не их владелец: регион → контролирующая страна.</summary>
    public required IReadOnlyDictionary<string, string> Occupied { get; init; }
    /// <summary>Регионы, которые контролирует не страна из данных карты (занятые и переданные по договору): регион → контролирующая страна.</summary>
    public IReadOnlyDictionary<string, string> Controllers { get; init; } = new Dictionary<string, string>();
    /// <summary>Регионы, сменившие владельца по мирному договору: регион → новый владелец.</summary>
    public IReadOnlyDictionary<string, string> Owners { get; init; } = new Dictionary<string, string>();
    /// <summary>Бюджет страны игрока (проект, поправки, прогноз); null, если игрок ещё не выбрал страну.</summary>
    public BudgetSnapshot? Budget { get; init; }
    /// <summary>Правление игрока, поражение на выборах (ждёт решения) и итоги закончившегося правления.</summary>
    public ReignSnapshot? Reign { get; init; }
    public Politics.Defeat? Defeat { get; init; }
    public Politics.ReignSummary? LastReign { get; init; }
    /// <summary>Последние записи журнала (отставки, назначения, голосования), новые — в конце.</summary>
    public IReadOnlyList<JournalEntry> Journal { get; init; } = [];
    /// <summary>Дипломатия страны игрока: отношения, торговля и договоры с каждой страной; null, если игрок не выбрал страну.</summary>
    public DiplomacySnapshot? Diplomacy { get; init; }
    /// <summary>Войны, перемирия и законченные войны (последние) — для карты и экрана дипломатии.</summary>
    public WarsSnapshot Wars { get; init; } = WarsSnapshot.Empty;
    /// <summary>Доля мира (по ВВП по ППС), которая освоила каждую технологию.</summary>
    public IReadOnlyDictionary<string, double> Adoption { get; init; } = new Dictionary<string, double>();
    /// <summary>Валюта каждой страны: код, режим курса, якорь и сколько стран ею пользуются.</summary>
    public required IReadOnlyDictionary<string, CurrencySnapshot> Currencies { get; init; }
    public required long Version { get; init; }

    /// <summary>Кто контролирует регион (mapCountry — страна региона в данных карты).</summary>
    public string ControllerOf(string regionId, string mapCountry) => Controllers.GetValueOrDefault(regionId, mapCountry);

    /// <summary>Кому регион принадлежит: новому владельцу по мирному договору или стране из данных карты.</summary>
    public string OwnerOf(string regionId, string mapCountry) => Owners.GetValueOrDefault(regionId, mapCountry);

    const int JournalShown = 60;

    public static WorldSnapshot Capture(World world, int speed, bool paused, long version)
    {
        var countries = new Dictionary<string, CountrySnapshot>(world.Countries.Count, StringComparer.Ordinal);
        foreach (var c in world.Countries)
        {
            double[]? ages = null;
            if (world.Demography?.Countries.GetValueOrDefault(c.Id) is { } d)
                ages = [.. d.Male, .. d.Female];
            List<ClassSnapshot>? classes = null;
            if (world.Society?.Countries.GetValueOrDefault(c.Id) is { } s)
            {
                var total = s.Total;
                classes = new List<ClassSnapshot>(s.Size.Length);
                for (var k = 0; k < s.Size.Length; k++)
                    classes.Add(new ClassSnapshot(world.Data.Classes.Classes[k].Id, s.Size[k], total > 0 ? s.Size[k] / total * 100 : 0,
                        s.Income[k], s.Savings[k], s.StandardOfLiving[k], s.Expectations[k], s.Satisfaction[k], s.Radicalism[k], s.Influence[k]));
            }
            var government = world.Government?.Countries.GetValueOrDefault(c.Id) is { } g ? GovernmentSnapshot.Capture(world, c, g) : null;
            var laws = world.Laws?.Countries.GetValueOrDefault(c.Id) is { } l ? LawsSnapshot.Capture(world.Date, l) : null;
            var history = world.History is { } h && h.Values.TryGetValue(c.Id, out var rows) ? HistorySnapshot.Capture(h, rows) : null;
            double[]? spending = null;
            if (world.Economy?.Countries.GetValueOrDefault(c.Id) is { } e)
            {
                var gdp = Math.Max(1e-9, e.NominalGdp);
                spending = new double[e.Spending.Length];
                for (var k = 0; k < spending.Length; k++)
                    spending[k] = e.Spending[k] / gdp;
            }
            var politics = world.Politics?.Countries.GetValueOrDefault(c.Id) is { } pc ? PoliticsSnapshot.Capture(world, c, pc) : null;
            var programs = world.Programs?.Countries.GetValueOrDefault(c.Id)?.Current.Order(StringComparer.Ordinal).ToList();
            var energy = world.Energy?.Countries.GetValueOrDefault(c.Id) is { } en ? EnergySnapshot.Capture(world, en) : null;
            var infrastructure = world.Infrastructure?.Countries.GetValueOrDefault(c.Id) is { } inf ? InfrastructureSnapshot.Capture(world, c, inf) : null;
            var science = world.Science?.Countries.GetValueOrDefault(c.Id) is { } sc ? ScienceSnapshot.Capture(world, c, sc) : null;
            countries[c.Id] = new CountrySnapshot(c.Id, c.System, c.Income, new Dictionary<string, StatValue>(c.Stats, StringComparer.Ordinal),
                ages, classes, government, c.Regime, laws, history, spending, politics, programs, energy, infrastructure, science,
                world.Diplomacy is null ? null : Sim.Diplomacy.DiplomacyModel.BlocsOf(world, c.Id).Select(b => b.Id).ToList(),
                c.Id != world.PlayerCountry && world.Ai?.Countries.GetValueOrDefault(c.Id) is { } ai
                    ? new AiSnapshot(ai.Priorities.ToArray(), ai.Why.ToList(), ai.Decisions.AsEnumerable().Reverse().Take(8).ToList())
                    : null);
        }
        var occupied = new Dictionary<string, string>(StringComparer.Ordinal);
        var controllers = new Dictionary<string, string>(StringComparer.Ordinal);
        var owners = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in world.Regions)
        {
            if (r.IsOccupied)
                occupied[r.Id] = r.Controller;
            if (r.Controller != r.Info.Country)
                controllers[r.Id] = r.Controller;
            if (r.Owner != r.Info.Country)
                owners[r.Id] = r.Owner;
        }
        var currencies = new Dictionary<string, CurrencySnapshot>(StringComparer.Ordinal);
        if (world.Economy is { } economy)
        {
            var banks = world.Data.Currencies.Currencies.ToDictionary(c => c.Id, c => c.Bank, StringComparer.Ordinal);
            foreach (var cur in economy.Currencies.Values)
            {
                var info = new CurrencySnapshot(cur.Id, cur.IsFloating, cur.Anchor, cur.Members.Count, banks.GetValueOrDefault(cur.Id));
                foreach (var id in cur.Members)
                    currencies[id] = info;
            }
        }
        return new WorldSnapshot
        {
            Date = world.Date,
            Speed = speed,
            Paused = paused,
            PlayerCountry = world.PlayerCountry,
            Countries = countries,
            Occupied = occupied,
            Controllers = controllers,
            Owners = owners,
            Currencies = currencies,
            Budget = BudgetSnapshot.Capture(world),
            Reign = world.Reign is { } reign ? new ReignSnapshot(reign.Country, reign.Since, reign.ElectionsWon, reign.ElectionsLost, reign.LawsPassed) : null,
            Defeat = world.Defeat,
            LastReign = world.LastReign,
            Adoption = world.Science?.Adoption.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal) ?? new Dictionary<string, double>(),
            Diplomacy = world.PlayerCountry is { } me && world.Diplomacy != null && world.ById(me) != null ? DiplomacySnapshot.Capture(world, me) : null,
            Wars = WarsSnapshot.Capture(world),
            Journal = world.Journal.Count <= JournalShown ? world.Journal.ToList() : world.Journal.GetRange(world.Journal.Count - JournalShown, JournalShown),
            Version = version,
        };
    }
}

/// <summary>
/// Наука страны в снимке: очки в год (% ВВП) и из чего они, эффективность, утечка мозгов, освоенные технологии,
/// распределение очков по ветвям (доли), приоритет. У страны игрока ещё — что изучается в каждой ветви и сколько
/// готово (0–1), сколько лет осталось.
/// </summary>
public sealed record ScienceSnapshot(double Points, double Government, double Private, double Efficiency, double Drain,
    IReadOnlySet<string> Researched, IReadOnlyList<double> Allocation, string? Priority, IReadOnlyDictionary<string, double>? Progress,
    IReadOnlyDictionary<string, double>? YearsLeft)
{
    public static ScienceSnapshot Capture(World world, Country country, Science.CountryScience c)
    {
        Dictionary<string, double>? progress = null, years = null;
        if (world.PlayerCountry == country.Id)
        {
            progress = new Dictionary<string, double>(StringComparer.Ordinal);
            years = new Dictionary<string, double>(StringComparer.Ordinal);
            var data = world.Data;
            var b = data.ScienceBalance;
            var perYear = c.Points / 100 * Society.SocietyModel.PppGdp(world.Economy![country.Id]) / 1e9;
            var branches = data.Science.Branches;
            var priority = c.Priority != null ? data.Science.Technologies.FirstOrDefault(t => t.Id == c.Priority) : null;
            var share = priority != null ? 1 - b.PriorityShare : 1;
            var targets = branches.Select(br => Science.ScienceModel.Target(data, c, br.Id)).ToArray();
            var weight = Enumerable.Range(0, branches.Count).Where(k => targets[k] != null).Sum(k => c.Allocation[k]);
            foreach (var t in data.Science.Technologies.Where(t => Science.ScienceModel.Available(data, c, t)))
            {
                var cost = Science.ScienceModel.Cost(world, country, t);
                var done = c.Progress.GetValueOrDefault(t.Id);
                progress[t.Id] = cost > 0 ? Math.Clamp(done / cost, 0, 1) : 0;
                var k = branches.ToList().FindIndex(br => br.Id == t.Branch);
                var rate = (t.Id == priority?.Id ? perYear * b.PriorityShare : 0)
                           + (targets[k]?.Id == t.Id && weight > 0 ? perYear * share * c.Allocation[k] / weight : 0);
                if (rate > 0)
                    years[t.Id] = Math.Max(0, cost - done) / rate;
            }
        }
        return new ScienceSnapshot(c.Points, Science.ScienceModel.Government(world, country, c), Science.ScienceModel.Private(world, country),
            c.Efficiency, c.Drain, c.Researched.ToHashSet(StringComparer.Ordinal), c.Allocation.ToArray(), c.Priority, progress, years);
    }
}

/// <summary>
/// Дипломатия страны игрока: партнёры по порядку стран, блоки (можно ли вступить), Совет Наций, Валютный совет.
/// </summary>
public sealed record DiplomacySnapshot(string Country, IReadOnlyList<PartnerSnapshot> Partners, IReadOnlyList<BlocSnapshot> Blocs,
    CouncilSnapshot Council, FundSnapshot Fund)
{
    public static DiplomacySnapshot Capture(World world, string me)
    {
        var types = world.Data.Diplomacy.Treaties;
        var resolutions = world.Data.Diplomacy.Council.Resolutions;
        var partners = new List<PartnerSnapshot>(world.Countries.Count);
        foreach (var c in world.Countries)
        {
            if (c.Id == me)
                continue;
            var treaties = types.Where(t => Diplomacy.DiplomacyModel.HasTreaty(world, me, c.Id, t.Id)).Select(t => t.Id).ToList();
            var offers = types.Where(t => !treaties.Contains(t.Id))
                .ToDictionary(t => t.Id, t => Diplomacy.DiplomacyModel.Acceptance(world, me, c.Id, t.Id), StringComparer.Ordinal);
            var council = resolutions.ToDictionary(r => r.Id, r => Diplomacy.DiplomacyModel.CanPropose(world, me, r.Id, c.Id), StringComparer.Ordinal);
            var war = world.War != null ? WarOption.Capture(world, me, c.Id) : null;
            partners.Add(new PartnerSnapshot(c.Id, Diplomacy.DiplomacyModel.Relations(world, me, c.Id), Diplomacy.DiplomacyModel.TradeShare(world, me, c.Id),
                Diplomacy.DiplomacyModel.Breakdown(world, me, c.Id), treaties, offers, Diplomacy.DiplomacyModel.CanVisit(world, me, c.Id),
                Diplomacy.DiplomacyModel.SanctionLevel(world, me, c.Id), Diplomacy.DiplomacyModel.SanctionLevel(world, c.Id, me), council, war));
        }
        var blocs = world.Data.Diplomacy.Blocs.Select(b =>
        {
            var (accepts, reason) = Diplomacy.DiplomacyModel.JoinAcceptance(world, me, b.Id);
            return new BlocSnapshot(b.Id, Diplomacy.DiplomacyModel.Members(world, b.Id).ToList(), Diplomacy.DiplomacyModel.InBloc(world, b.Id, me), accepts, reason);
        }).ToList();
        return new DiplomacySnapshot(me, partners, blocs, CouncilSnapshot.Capture(world, me), FundSnapshot.Capture(world, me));
    }
}

/// <summary>
/// Партнёр: отношения (−100…100) и из чего они, доля в нашей торговле (0–1), действующие договоры, какие договоры
/// партнёр подпишет и почему, можно ли ехать с визитом, наши санкции против него и его против нас (full, partial или
/// null), какие резолюции Совета Наций о нём можно вынести и почему нет.
/// </summary>
public sealed record PartnerSnapshot(string Id, double Relations, double TradeShare, IReadOnlyList<StatFactor> Factors,
    IReadOnlyList<string> Treaties, IReadOnlyDictionary<string, (bool Accepts, string Reason)> Offers, bool CanVisit,
    string? OurSanction = null, string? TheirSanction = null,
    IReadOnlyDictionary<string, (bool Allowed, string Reason)>? Resolutions = null, WarOption? War = null);

/// <summary>
/// Война с партнёром: можно ли объявить и почему нет; наша сила и сила тех, кто вступится (относительные числа),
/// кто вступится, спорные регионы (их названия), ядерная ли держава; идущая война (её номер) и перемирие (с какой даты).
/// </summary>
public sealed record WarOption(bool CanDeclare, string Reason, double Ours, double Theirs, IReadOnlyList<string> Defenders,
    IReadOnlyList<string> Claims, bool Nuclear, int? WarId, DateOnly? Ceasefire)
{
    public static WarOption Capture(World world, string me, string other)
    {
        var b = world.Data.WarBalance;
        var (can, reason) = Military.WarModel.CanDeclare(world, me, other);
        var war = Military.WarModel.WarBetween(world, me, other);
        var defenders = can ? Military.WarModel.Defenders(world, me, other) : (IReadOnlyList<string>)[other];
        var theirs = defenders.Sum(d => Military.WarModel.Strength(world, d) * (d == other ? b.CommitMain : b.CommitAlly));
        return new WarOption(can, reason, Military.WarModel.Strength(world, me) * b.CommitMain, theirs, defenders,
            Military.WarModel.Claims(world, me, other).Select(r => Military.WarModel.RegionName(world, r)).ToList(),
            world.Data.Diplomacy.Nuclear.Contains(other), war?.Id, Military.WarModel.CeasefireBetween(world, me, other)?.Since);
    }
}

/// <summary>
/// Войны мира: идущие, перемирия, последние законченные; мирные договоры (последние); переговоры игрока о мире с
/// каждым противником (идёт война или перемирие).
/// </summary>
public sealed record WarsSnapshot(IReadOnlyList<WarSnapshot> Active, IReadOnlyList<Military.Ceasefire> Ceasefires, IReadOnlyList<Military.WarRecord> History)
{
    public static readonly WarsSnapshot Empty = new([], [], []);

    public IReadOnlyList<PeaceTreatySnapshot> Treaties { get; init; } = [];
    public IReadOnlyList<PeaceOption> Peace { get; init; } = [];

    public static WarsSnapshot Capture(World world)
    {
        if (world.War is not { } w)
            return Empty;
        var active = w.Wars.Select(x => new WarSnapshot(x.Id, x.Attackers.ToList(), x.Defenders.ToList(), x.Goal.ToList(), x.Start,
            new Dictionary<string, double>(x.Front, StringComparer.Ordinal), x.AttackerLosses, x.DefenderLosses,
            x.News.AsEnumerable().Reverse().Take(8).ToList(), x.Offer,
            w.Armies[x.Attacker].Exhaustion, w.Armies[x.Defender].Exhaustion,
            Military.WarModel.SideStrength(world, x, 1), Military.WarModel.SideStrength(world, x, 2))).ToList();
        var treaties = w.Treaties.AsEnumerable().Reverse().Take(10)
            .Select(t => new PeaceTreatySnapshot(t.Winner, t.Loser, t.Date, t.Text, t.Until, t.Terms.Reparations, t.ReparationsUntil, t.Paid)).ToList();
        var peace = new List<PeaceOption>();
        if (world.PlayerCountry is { } me)
        {
            var partners = w.Wars.Where(x => x.IsMain(me)).Select(x => x.Side(me) == 1 ? x.Defender : x.Attacker)
                .Concat(w.Ceasefires.Where(c => c.A == me || c.B == me).Select(c => c.Other(me))).Distinct();
            foreach (var partner in partners.Where(x => Military.PeaceModel.CanTalk(world, me, x)))
                peace.Add(PeaceOption.Capture(world, me, partner));
        }
        return new WarsSnapshot(active, w.Ceasefires.ToList(), w.History.AsEnumerable().Reverse().Take(10).ToList()) { Treaties = treaties, Peace = peace };
    }
}

/// <summary>Мирный договор: кто получил условия, кто уступил, когда, условия словами, мир до, репарации (% ВВП в год, до какой даты, выплачено долл.).</summary>
public sealed record PeaceTreatySnapshot(string Winner, string Loser, DateOnly Date, string Text, DateOnly Until, double Reparations,
    DateOnly? ReparationsUntil, double Paid);

/// <summary>Регион в условиях мира: цена для уступающего (пункты счёта), спорный ли.</summary>
public sealed record PeaceRegion(string Id, string Name, double Cost, bool Claimed);

/// <summary>
/// Переговоры игрока о мире с противником. Score — наш счёт войны (больше нуля — мы ставим условия, Winner); Regions —
/// регионы, которые можно потребовать (если мы победитель) или уступить; цены репараций (за 1% ВВП в год), торговых
/// уступок и нейтралитета. Need — сколько хочет победитель, Limit — на сколько согласен уступающий (если Willing — устал
/// или капитулировал; белый мир он принимает всегда). Offer — предложение противника (словами), если ждёт решения.
/// </summary>
public sealed record PeaceOption(string Partner, double Score, bool Winner, string Why, IReadOnlyList<PeaceRegion> Regions,
    IReadOnlyList<double> ReparationLevels, double ReparationUnit, double TradeCost, double NeutralityCost, double Need, double Limit,
    bool Willing, string? Offer, bool AtWar)
{
    public static PeaceOption Capture(World world, string me, string partner)
    {
        var p = world.Data.PeaceBalance;
        var score = Military.PeaceModel.Score(world, me, partner);
        var (winner, loser) = score >= 0 ? (me, partner) : (partner, me);
        var regions = Military.PeaceModel.Annexable(world, winner, loser)
            .Select(r => new PeaceRegion(r.Id, Military.WarModel.RegionName(world, r.Id), Military.PeaceModel.RegionCost(world, r, winner),
                Military.PeaceModel.Claimed(world, winner, r.Id)))
            .OrderByDescending(r => r.Claimed).ThenByDescending(r => r.Cost).ThenBy(r => r.Id, StringComparer.Ordinal).ToList();
        var (need, limit, willing) = Military.PeaceModel.Bounds(world, winner, loser);
        var offer = world.War!.Offers.FirstOrDefault(o => o.From == partner && o.To == me);
        return new PeaceOption(partner, score, winner == me, Military.PeaceModel.ScoreWhy(world, me, partner), regions, p.ReparationLevels,
            Military.PeaceModel.ReparationCost(world, 1), p.TradeCost, p.NeutralityCost, need, limit, willing,
            offer is null ? null : Military.PeaceModel.Describe(world, offer.Winner, offer.Loser, offer.Terms),
            Military.PeaceModel.MainWar(world, me, partner) != null);
    }
}

/// <summary>
/// Идущая война: стороны (первые — главные), цель, начало, фронт (регион → продвижение противника, 0–100), потери,
/// новости (новые первыми), кто предложил перемирие, усталость главных сторон, сила сторон.
/// </summary>
public sealed record WarSnapshot(int Id, IReadOnlyList<string> Attackers, IReadOnlyList<string> Defenders, IReadOnlyList<string> Goal,
    DateOnly Start, IReadOnlyDictionary<string, double> Front, double AttackerLosses, double DefenderLosses,
    IReadOnlyList<Military.WarNews> News, string? Offer, double AttackerExhaustion, double DefenderExhaustion,
    double AttackerStrength, double DefenderStrength)
{
    public int Side(string? country) => country == null ? 0 : Attackers.Contains(country) ? 1 : Defenders.Contains(country) ? 2 : 0;
}

/// <summary>Блок: члены, состоим ли мы в нём, примут ли нас и почему.</summary>
public sealed record BlocSnapshot(string Id, IReadOnlyList<string> Members, bool Member, bool Accepts, string Reason);

/// <summary>
/// Совет Наций: постоянные и выборные члены Совета безопасности, страны под санкциями Совета, последние резолюции,
/// когда игрок может вынести следующую, и таблица для прогноза голосования на экране.
/// </summary>
public sealed record CouncilSnapshot(IReadOnlyList<string> Permanent, IReadOnlyList<Diplomacy.CouncilSeat> Seats, IReadOnlyList<string> Sanctioned,
    IReadOnlyList<Diplomacy.Resolution> Resolutions, DateOnly? NextProposal, Diplomacy.VoteTable Votes)
{
    public static CouncilSnapshot Capture(World world, string me)
    {
        var d = world.Diplomacy!;
        var months = world.Data.DiplomacyBalance.CouncilMonths;
        DateOnly? next = d.Proposed.TryGetValue(me, out var last) && last.AddMonths(months) > world.Date ? last.AddMonths(months) : null;
        return new CouncilSnapshot(Diplomacy.DiplomacyModel.Permanent(world), d.Seats.ToList(), d.CouncilSanctions.ToList(),
            d.Resolutions.AsEnumerable().Reverse().Take(12).ToList(), next, Diplomacy.DiplomacyModel.Votes(world));
    }
}

/// <summary>Валютный совет: что он предлагает стране игрока, её программа (если была), рейтинг и дефицит (% ВВП).</summary>
public sealed record FundSnapshot(Diplomacy.DiplomacyModel.LoanOffer Offer, FundProgramSnapshot? Program, double Rating, double Deficit)
{
    public static FundSnapshot Capture(World world, string me)
    {
        var e = world.Economy![me];
        var program = world.Diplomacy!.Programs.TryGetValue(me, out var p)
            ? new FundProgramSnapshot(p.Since, p.Amount, p.Rate, p.Deadline, p.Until, p.Status, p.PenaltyUntil)
            : null;
        return new FundSnapshot(Diplomacy.DiplomacyModel.FundOffer(world, me), program, e.Rating, -e.BalanceRatio * 100);
    }
}

public sealed record FundProgramSnapshot(DateOnly Since, double Amount, double Rate, DateOnly Deadline, DateOnly Until, string Status, DateOnly? PenaltyUntil);

/// <summary>Правление игрока: страна, с какой даты, выборы выиграны и проиграны, законов принято.</summary>
public sealed record ReignSnapshot(string Country, DateOnly Since, int ElectionsWon, int ElectionsLost, int LawsPassed);

public sealed record CurrencySnapshot(string Code, bool Floating, string? Anchor, int Users, string? Bank);

/// <summary>
/// Класс в снимке: численность с семьями, доля населения (%), доход и сбережения на человека (долл. по ППС в год),
/// уровень жизни, ожидания, удовлетворённость, радикализм (0–100) и влияние (%).
/// </summary>
public sealed record ClassSnapshot(string Id, double Size, double Share, double Income, double Savings,
    double StandardOfLiving, double Expectations, double Satisfaction, double Radicalism, double Influence);

/// <summary>
/// Страна в снимке. Ages — численность по пятилетним группам: сначала 17 групп мужчин, затем 17 женщин.
/// Classes — классы в порядке data/classes.json. Spending — расходы бюджета по статьям data/budget.json в долях ВВП.
/// </summary>
public sealed record CountrySnapshot(
    string Id,
    PoliticalSystem System,
    IncomeGroup Income,
    IReadOnlyDictionary<string, StatValue> Stats,
    IReadOnlyList<double>? Ages = null,
    IReadOnlyList<ClassSnapshot>? Classes = null,
    GovernmentSnapshot? Government = null,
    Regime Regime = Regime.Democracy,
    LawsSnapshot? Laws = null,
    HistorySnapshot? History = null,
    IReadOnlyList<double>? Spending = null,
    PoliticsSnapshot? Politics = null,
    IReadOnlyList<string>? Programs = null,
    EnergySnapshot? Energy = null,
    InfrastructureSnapshot? Infrastructure = null,
    ScienceSnapshot? Science = null,
    IReadOnlyList<string>? Blocs = null,
    AiSnapshot? Ai = null)
{
    public StatValue? Stat(string id) => Stats.GetValueOrDefault(id);
}

/// <summary>ИИ правительства: приоритеты (рост, стабильность, сила, переизбрание), почему они такие, последние решения (новые первыми).</summary>
public sealed record AiSnapshot(IReadOnlyList<double> Priorities, IReadOnlyList<string> Why, IReadOnlyList<Ai.AiDecision> Decisions);

/// <summary>
/// Энергосистема в снимке: спрос и возможная выработка (ТВт·ч в год), нехватка (доля спроса), импорт (ТВт·ч, минус —
/// экспорт), деньги на стройку (1 — как на старте) и станции по типам из data/energy.json.
/// </summary>
public sealed record EnergySnapshot(double Demand, double Available, double Need, double Shortage, double Imports, double Funding,
    IReadOnlyList<PlantSnapshot> Plants)
{
    public static EnergySnapshot Capture(World world, Energy.CountryEnergy c)
    {
        var plants = world.Data.Energy.Plants;
        var total = c.Generation.Sum();
        var list = new List<PlantSnapshot>(plants.Count);
        for (var t = 0; t < plants.Count && t < c.Capacity.Length; t++)
            list.Add(new PlantSnapshot(plants[t].Id, plants[t].Name, c.Capacity[t], c.Building[t], total > 0 ? c.Generation[t] / total * 100 : 0));
        return new EnergySnapshot(c.Demand, Energy.EnergyModel.Available(world, c), c.Demand * (1 - c.ImportShare), c.Shortage, c.Imports, c.Funding, list);
    }
}

/// <summary>Станции одного типа: мощность и строящаяся мощность (ГВт), доля в выработке (%).</summary>
public sealed record PlantSnapshot(string Id, string Name, double Capacity, double Building, double Share);

/// <summary>
/// Инфраструктура в снимке: виды (загрузка — нагрузка к тому, что выдерживает, %; состояние, %; строится — % к
/// мощности), стройки игрока и — только у страны игрока — что можно начать.
/// </summary>
public sealed record InfrastructureSnapshot(IReadOnlyList<InfrastructureTypeSnapshot> Types, IReadOnlyList<ProjectSnapshot> Projects,
    IReadOnlyList<ProjectOfferSnapshot>? Offers)
{
    public static InfrastructureSnapshot Capture(World world, Country country, Infrastructure.CountryInfrastructure c)
    {
        var data = world.Data;
        var types = new List<InfrastructureTypeSnapshot>(data.Infrastructure.Types.Count);
        for (var t = 0; t < data.Infrastructure.Types.Count && t < c.Capacity.Length; t++)
        {
            var type = data.Infrastructure.Types[t];
            var usable = c.Capacity[t] * c.Condition[t];
            types.Add(new InfrastructureTypeSnapshot(type.Id, type.Name, type.Post, c.Need[t] > 0,
                usable > 0 ? c.Need[t] / usable * 100 : 0, c.Condition[t] * 100, c.Capacity[t] > 0 ? c.Building[t] / c.Capacity[t] * 100 : 0));
        }
        var projects = c.Projects.Select(p =>
        {
            var record = data.Infrastructure.Projects.First(x => x.Id == p.Template);
            return new ProjectSnapshot(p.Id, p.Template, p.Name, Infrastructure.InfrastructureModel.Post(data, record), p.Financing, p.Lender,
                p.Cost * 100, p.Started, p.Ends, p.Done, p.Concession);
        }).ToList();
        List<ProjectOfferSnapshot>? offers = null;
        if (world.PlayerCountry == country.Id)
            offers = data.Infrastructure.Projects.Select(r =>
            {
                var o = Infrastructure.InfrastructureModel.Quote(world, country, r);
                return new ProjectOfferSnapshot(r.Id, r.Name, r.Description, Infrastructure.InfrastructureModel.Post(data, r), o.Cost * 100, o.Months,
                    o.Effect, o.Unavailable, o.PppAllowed);
            }).ToList();
        return new InfrastructureSnapshot(types, projects, offers);
    }
}

public sealed record InfrastructureTypeSnapshot(string Id, string Name, string Post, bool Applies, double Load, double Condition, double Building);

/// <summary>Стройка игрока: цена — % ВВП; Concession — до какой даты люди платят за пользование.</summary>
public sealed record ProjectSnapshot(int Id, string Template, string Name, string Post, string Financing, string? Lender, double Cost,
    DateOnly Started, DateOnly Ends, bool Done, DateOnly? Concession)
{
    public double Progress(DateOnly date) => Done ? 1
        : Math.Clamp((date.DayNumber - Started.DayNumber) / (double)Math.Max(1, Ends.DayNumber - Started.DayNumber), 0, 1);
}

/// <summary>Стройка, которую можно начать: цена (% ВВП), срок (мес.), что даст, почему нельзя, можно ли в концессию.</summary>
public sealed record ProjectOfferSnapshot(string Id, string Name, string Description, string Post, double Cost, int Months, string Effect,
    string? Unavailable, bool PppAllowed);
