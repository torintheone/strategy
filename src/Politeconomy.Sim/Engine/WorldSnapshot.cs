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
    /// <summary>Бюджет страны игрока (проект, поправки, прогноз); null, если игрок ещё не выбрал страну.</summary>
    public BudgetSnapshot? Budget { get; init; }
    /// <summary>Правление игрока, поражение на выборах (ждёт решения) и итоги закончившегося правления.</summary>
    public ReignSnapshot? Reign { get; init; }
    public Politics.Defeat? Defeat { get; init; }
    public Politics.ReignSummary? LastReign { get; init; }
    /// <summary>Последние записи журнала (отставки, назначения, голосования), новые — в конце.</summary>
    public IReadOnlyList<JournalEntry> Journal { get; init; } = [];
    /// <summary>Валюта каждой страны: код, режим курса, якорь и сколько стран ею пользуются.</summary>
    public required IReadOnlyDictionary<string, CurrencySnapshot> Currencies { get; init; }
    public required long Version { get; init; }

    /// <summary>Кто контролирует регион: оккупант или, если его нет, владелец.</summary>
    public string ControllerOf(string regionId, string owner) => Occupied.GetValueOrDefault(regionId, owner);

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
            countries[c.Id] = new CountrySnapshot(c.Id, c.System, c.Income, new Dictionary<string, StatValue>(c.Stats, StringComparer.Ordinal),
                ages, classes, government, c.Regime, laws, history, spending, politics, programs, energy, infrastructure);
        }
        var occupied = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var r in world.Regions)
            if (r.IsOccupied)
                occupied[r.Id] = r.Controller;
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
            Currencies = currencies,
            Budget = BudgetSnapshot.Capture(world),
            Reign = world.Reign is { } reign ? new ReignSnapshot(reign.Country, reign.Since, reign.ElectionsWon, reign.ElectionsLost, reign.LawsPassed) : null,
            Defeat = world.Defeat,
            LastReign = world.LastReign,
            Journal = world.Journal.Count <= JournalShown ? world.Journal.ToList() : world.Journal.GetRange(world.Journal.Count - JournalShown, JournalShown),
            Version = version,
        };
    }
}

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
    InfrastructureSnapshot? Infrastructure = null)
{
    public StatValue? Stat(string id) => Stats.GetValueOrDefault(id);
}

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
