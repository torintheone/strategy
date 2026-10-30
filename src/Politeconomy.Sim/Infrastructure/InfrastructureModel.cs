using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;
using Politeconomy.Sim.Text;

namespace Politeconomy.Sim.Infrastructure;

/// <summary>
/// Инфраструктура — пропускная способность экономики: дороги, железные дороги, порты, аэропорты, трубопроводы,
/// электросети, связь, водопровод. Нагрузка растёт с отраслями, торговлей и населением; без денег на обслуживание
/// объекты ветшают; новые строят годами. Перегрузка режет выпуск тех отраслей, что на неё опираются. Игрок может
/// начать стройку сам — из бюджета, в кредит у другой страны или в концессию. MODEL.md, «Инфраструктура и стройки».
/// </summary>
public static class InfrastructureModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.InfrastructureSystem()];

    const double Hours = 8.76;

    public static void Setup(World world)
    {
        var data = world.Data;
        var b = data.InfrastructureBalance;
        var types = data.Infrastructure.Types;
        var all = new WorldInfrastructure();
        world.Infrastructure = all;
        foreach (var country in world.Countries)
        {
            var income = Math.Clamp((int)country.Income, 0, 3);
            var e = world.Economy![country.Id];
            var growth = Math.Clamp(e.TrendGrowth, 0, b.GrowthMax);
            var n = types.Count;
            var c = new CountryInfrastructure
            {
                Capacity = new double[n], Condition = new double[n], Building = new double[n], Need = new double[n],
                Coverage = new double[n], Growth = new double[n], Funding = new double[n],
                Business0 = types.Select(t => Business(world, country, e, t)).ToArray(),
                Population0 = world.Demography![country.Id].Total, Income0 = SocietyModel.IncomeGrowth(e),
                Investment0 = e.InvestmentRate, Corruption0 = Stat(country, "corruption_index", 50),
                Cut = new double[data.Industries.Industries.Count], Mood = new double[ClassIds.Count],
            };
            foreach (var item in types.Select(t => t.Budget).OfType<string>().Distinct())
                c.Spending0[item] = Spending(world, country, item, c);
            c.Spending0[Total] = TotalSpending(world, country, c);
            for (var t = 0; t < n; t++)
            {
                c.Coverage[t] = c.Funding[t] = 1;
                if (!Applies(types[t], country))
                    continue;
                c.Need[t] = 100;
                c.Condition[t] = b.Condition[income];
                c.Capacity[t] = 100 * (1 + b.Reserve[income]) / c.Condition[t];
                c.Building[t] = c.Capacity[t] * growth * types[t].BuildYears;
                c.Growth[t] = growth;
            }
            all.Countries[country.Id] = c;
            Publish(world, country, c);
        }
    }

    /// <summary>Порты нужны только странам с выходом к морю.</summary>
    static bool Applies(InfrastructureRecord type, Country country) => !(type.Coast && country.Info.Landlocked);

    /// <summary>Месяц: стройки игрока, нагрузка, износ и обслуживание, стройка, обеспеченность, поправки к модели.</summary>
    public static void Update(World world, Country country, CountryInfrastructure c)
    {
        var data = world.Data;
        var b = data.InfrastructureBalance;
        var types = data.Infrastructure.Types;
        var e = world.Economy![country.Id];
        var income = Math.Clamp((int)country.Income, 0, 3);
        var investment = c.Investment0 > 0 ? Math.Max(0.1, e.InvestmentRate / c.Investment0) : 1;
        var corruption = Stat(country, "corruption_index", c.Corruption0) - c.Corruption0;
        Projects(world, country, c);
        var total = TotalSpending(world, country, c);
        var austerity = c.Spending0.GetValueOrDefault(Total) > 0 ? Math.Clamp(total / c.Spending0[Total], 1 / b.SpendingRange, b.SpendingRange) : 1;

        for (var t = 0; t < types.Count; t++)
        {
            var type = types[t];
            if (!Applies(type, country))
                continue;
            var need = Need(world, country, c, t);
            if (c.Need[t] > 0 && need > 0)
                c.Growth[t] = Math.Clamp(c.Growth[t] + (Math.Log(need / c.Need[t]) * 12 - c.Growth[t]) / b.GrowthMonths, -b.GrowthMax, b.GrowthMax);
            c.Need[t] = need;

            // Деньги: доля статьи в бюджете (и, слабее, весь бюджет к ВВП) или частные инвестиции у связи; без них объекты
            // ветшают, лишние идут на ремонт. Общее сокращение бюджета бьёт слабее, чем урезанная статья.
            var maintenance = type.Budget != null && c.Spending0.GetValueOrDefault(type.Budget) > 0 && total > 0
                ? Math.Pow(Math.Clamp(Spending(world, country, type.Budget, c) / total / (c.Spending0[type.Budget] / c.Spending0[Total]), 1 / b.SpendingRange, b.SpendingRange), b.FundingSpending)
                  * Math.Pow(austerity, b.FundingTotal)
                : Math.Sqrt(investment);
            // Автопилот бюджета не расставляет приоритеты (урезает всё подряд), поэтому у стран без игрока он
            // содержит инфраструктуру полностью; в стране игрока всё решают деньги, которые он выделил.
            if (world.PlayerCountry != country.Id)
                maintenance = Math.Max(1, maintenance);
            c.Funding[t] = Math.Clamp(maintenance * Math.Pow(investment, b.FundingInvestment) * (1 - b.FundingCorruption * corruption), b.FundingMin, b.FundingMax);
            c.Condition[t] = Math.Clamp(c.Condition[t] + type.Wear * (Math.Min(maintenance, 1.5) - 1) / 12, b.ConditionMin, 1);

            // Стройка: достраивается за BuildYears; заказ — рост нагрузки плюс догон до нужной мощности, не больше, чем позволяют деньги.
            var done = c.Building[t] / (12 * type.BuildYears);
            c.Building[t] -= done;
            c.Capacity[t] += done;
            var target = need * (1 + b.Reserve[income]) / c.Condition[t];
            var baseline = Math.Max(0, c.Growth[t]) * target;
            var orders = Math.Min(baseline + (target - c.Capacity[t]) * 12 / b.ReserveMonths + (baseline * type.BuildYears - c.Building[t]) * 12 / b.PipelineMonths,
                b.BuildMax * c.Funding[t] * c.Capacity[t]) / 12;
            if (orders > 0)
                c.Building[t] += orders;
            c.Coverage[t] = need > 0 ? Math.Min(1, c.Capacity[t] * c.Condition[t] / need) : 1;
        }

        // Перегрузка режет выпуск отраслей по их весу у этого вида.
        var industries = data.Industries.Industries;
        if (c.Cut.Length != industries.Count)
            c.Cut = new double[industries.Count];
        Array.Clear(c.Cut);
        var output = e.Output.Sum();
        for (var t = 0; t < types.Count; t++)
        {
            if (c.Coverage[t] >= 1 || output <= 0)
                continue;
            var type = types[t];
            var mean = 0.0;
            for (var i = 0; i < industries.Count && i < e.Output.Length; i++)
                mean += Weight(type, industries[i].Id) * e.Output[i] / output;
            if (mean <= 0)
                continue;
            for (var i = 0; i < industries.Count; i++)
                c.Cut[i] += b.InfraCut * (1 - c.Coverage[t]) * Weight(type, industries[i].Id) / mean;
        }
        for (var i = 0; i < c.Cut.Length; i++)
            c.Cut[i] = Math.Min(b.CutMax, c.Cut[i]);

        var water = types.ToList().FindIndex(x => x.Id == "water");
        c.Effects["disease_burden"] = water >= 0 ? b.WaterHealth * (1 - c.Coverage[water]) : 0;
        Publish(world, country, c);
    }

    static double Weight(InfrastructureRecord type, string industry) => type.Weights.GetValueOrDefault(industry, type.Default);

    /// <summary>Нагрузка (100 — на старте): отрасли по весам (или торговля, или спрос на электричество) и население по доходу.</summary>
    static double Need(World world, Country country, CountryInfrastructure c, int t)
    {
        var type = world.Data.Infrastructure.Types[t];
        var e = world.Economy![country.Id];
        var business = c.Business0[t] > 0 ? Business(world, country, e, type) / c.Business0[t] : 1;
        var income = c.Income0 > 0 ? SocietyModel.IncomeGrowth(e) / c.Income0 : 1;
        var households = world.Demography![country.Id].Total / c.Population0 * Math.Pow(Math.Max(0.1, income), world.Data.InfrastructureBalance.HouseholdIncome);
        return 100 * ((1 - type.Households) * business + type.Households * households);
    }

    /// <summary>Нагрузка от экономики: выпуск отраслей по весам (без потерь от отключений и перегрузки), торговля или спрос на электричество.</summary>
    static double Business(World world, Country country, Economy.CountryEconomy e, InfrastructureRecord type)
    {
        if (type.Driver == "trade" && e.Exports + e.Imports > 0)
            return (e.Exports + e.Imports) * e.RealGdp;
        if (type.Driver == "power" && world.Energy?.Countries.GetValueOrDefault(country.Id) is { Demand: > 0 } energy)
            return energy.Demand;
        var industries = world.Data.Industries.Industries;
        var sum = 0.0;
        for (var i = 0; i < industries.Count && i < e.Output.Length; i++)
            sum += Weight(type, industries[i].Id) * e.Output[i] / Math.Max(0.1, Economy.Supply.Factor(world, country.Id, i));
        return sum;
    }

    /// <summary>
    /// Стройки игрока: идущие из бюджета прибавляют к расходам статьи, в кредит и в концессию — толкают спрос, как
    /// дефицит бюджета; достроенные добавляют мощность, состояние или станции; концессии люди оплачивают годами.
    /// </summary>
    static void Projects(World world, Country country, CountryInfrastructure c)
    {
        var b = world.Data.InfrastructureBalance;
        var e = world.Economy![country.Id];
        var spending = new Dictionary<string, double>(StringComparer.Ordinal);
        double flow = 0, fees = 0;
        foreach (var p in c.Projects)
        {
            if (!p.Done && world.Date >= p.Ends)
                Complete(world, country, c, p);
            if (!p.Done)
            {
                var months = Math.Max(1, (p.Ends.DayNumber - p.Started.DayNumber) / 30.44);
                var perYear = p.Cost * 12 / months;
                if (p.Financing == "budget")
                {
                    var item = Item(world.Data, world.Data.Infrastructure.Projects.First(x => x.Id == p.Template));
                    spending[item] = spending.GetValueOrDefault(item) + perYear * 100;
                }
                else
                    flow += perYear;
            }
            else if (p.Concession is { } until && world.Date < until)
                fees += b.PppMood * p.Cost * (1 + b.PppMargin) * 100;
        }
        foreach (var key in c.Effects.Keys.Where(k => k.StartsWith("spending.", StringComparison.Ordinal)).ToList())
            c.Effects.Remove(key);
        foreach (var (item, value) in spending)
            c.Effects["spending." + item] = value;
        e.ProjectFlow = flow;
        if (c.Mood.Length != ClassIds.Count)
            c.Mood = new double[ClassIds.Count];
        Array.Fill(c.Mood, -fees);
    }

    static void Complete(World world, Country country, CountryInfrastructure c, Project p)
    {
        var record = world.Data.Infrastructure.Projects.First(x => x.Id == p.Template);
        p.Done = true;
        if (record.Plant != null && world.Energy?.Countries.GetValueOrDefault(country.Id) is { } energy)
        {
            var t = world.Data.Energy.Plants.ToList().FindIndex(x => x.Id == record.Plant);
            if (t >= 0 && t < energy.Capacity.Length)
                energy.Capacity[t] += p.Amount;
        }
        else if (record.Type != null)
        {
            var t = world.Data.Infrastructure.Types.ToList().FindIndex(x => x.Id == record.Type);
            if (record.Kind == "repair")
                c.Condition[t] = Math.Min(1, c.Condition[t] + p.Amount);
            else
                c.Capacity[t] += p.Amount;
        }
        if (p.Financing == "ppp")
            p.Concession = world.Date.AddMonths((int)Math.Round(world.Data.InfrastructureBalance.ConcessionYears * 12));
        world.Log(country.Id, "project", $"Достроено: «{p.Name}»." + (p.Concession != null
            ? $" Концессия: {Fmt.Plural((int)world.Data.InfrastructureBalance.ConcessionYears, "год", "года", "лет")} люди платят за пользование."
            : ""));
    }

    /// <summary>Статья бюджета стройки: у инфраструктуры — своя, у электростанций — энергетика.</summary>
    public static string Item(GameData data, ProjectRecord record) =>
        record.Plant != null ? "energy" : data.Infrastructure.Types.First(t => t.Id == record.Type).Budget ?? "transport";

    /// <summary>Ведомство стройки.</summary>
    public static string Post(GameData data, ProjectRecord record) =>
        record.Plant != null ? "energy" : data.Infrastructure.Types.First(t => t.Id == record.Type).Post;

    /// <summary>
    /// Предложение стройки для страны: цена (% ВВП) и срок с поправкой на коррупцию, что даст, можно ли строить и
    /// можно ли в концессию.
    /// </summary>
    public sealed record Offer(ProjectRecord Record, double Cost, int Months, double Amount, string Effect, string? Unavailable, bool PppAllowed);

    public static Offer Quote(World world, Country country, ProjectRecord record)
    {
        var data = world.Data;
        var b = data.InfrastructureBalance;
        var income = Math.Clamp((int)country.Income, 0, 3);
        var corruptionIndex = Stat(country, "corruption_index", 50);
        var corruption = Math.Max(0, corruptionIndex - b.CorruptionFree) / 100;
        var costFactor = 1 + b.ProjectCorruption * corruption;
        var delay = 1 + b.ProjectDelay * corruption;
        string? unavailable = null;
        double cost, amount, months;
        string effect;
        if (record.Plant != null)
        {
            var t = data.Energy.Plants.ToList().FindIndex(x => x.Id == record.Plant);
            var plant = data.Energy.Plants[t];
            var energy = world.Energy![country.Id];
            var need = energy.Demand * (1 - energy.ImportShare);
            amount = record.Gain * need / (plant.CapacityFactor * Hours);
            var gdp = Math.Max(1e-6, world.Economy![country.Id].GdpUsd);
            cost = amount * plant.CostPerGw * 1e9 / gdp * costFactor;
            months = plant.BuildYears * 12 * delay;
            effect = $"+{Fmt.Number(amount, amount < 10 ? 1 : 0)} ГВт ({Fmt.Number(record.Gain * 100, 0)}% нужной выработки)";
            if (energy.Share0[t] <= 0 && income < Math.Max(record.MinIncome, plant.MinIncome))
                unavailable = "Стране пока не хватает технологий и специалистов.";
            if (plant.Id == "hydro" && energy.Capacity0[t] <= 0)
                unavailable = "Нет подходящих рек.";
        }
        else
        {
            var t = data.Infrastructure.Types.ToList().FindIndex(x => x.Id == record.Type);
            var type = data.Infrastructure.Types[t];
            var c = world.Infrastructure![country.Id];
            var repair = record.Kind == "repair";
            amount = repair ? Math.Min(record.Gain, 1 - c.Condition[t]) : record.Gain * c.Capacity[t];
            cost = record.Gain * type.Stock / 100 * (repair ? b.RepairCost : 1) * costFactor;
            months = record.Months * delay;
            effect = repair
                ? $"+{Fmt.Number(record.Gain * 100, 0)} п. п. к состоянию: {type.Name.ToLowerInvariant()}"
                : $"+{Fmt.Number(record.Gain * 100, 0)}% к мощности: {type.Name.ToLowerInvariant()}";
            if (!Applies(type, country))
                unavailable = "У страны нет выхода к морю.";
            else if (income < record.MinIncome)
                unavailable = "Стране пока не хватает технологий и специалистов.";
            else if (repair && c.Condition[t] >= 0.99)
                unavailable = "Ремонт не нужен: всё в хорошем состоянии.";
        }
        return new Offer(record, cost, Math.Max(1, (int)Math.Round(months)), amount, effect, unavailable,
            record.Kind != "repair" && corruptionIndex < b.PppMaxCorruption);
    }

    /// <summary>Начать стройку (только в своей стране): из бюджета (budget), в кредит у страны lender (loan) или в концессию (ppp).</summary>
    public static bool Start(World world, string countryId, string projectId, string financing, string? lender = null)
    {
        if (world.PlayerCountry != countryId || world.ById(countryId) is not { } country
            || world.Infrastructure?.Countries.GetValueOrDefault(countryId) is not { } c
            || world.Data.Infrastructure.Projects.FirstOrDefault(p => p.Id == projectId) is not { } record)
            return false;
        var offer = Quote(world, country, record);
        if (offer.Unavailable != null)
            return false;
        if (financing == "ppp" && !offer.PppAllowed)
            return false;
        if (financing == "loan" && (lender == null || lender == countryId || !world.Data.Infrastructure.Lenders.Contains(lender) || world.ById(lender) is null))
            return false;
        if (financing is not ("budget" or "loan" or "ppp"))
            return false;
        var project = new Project
        {
            Id = c.NextProject++, Template = record.Id, Name = record.Name, Financing = financing,
            Lender = financing == "loan" ? lender : null, Cost = offer.Cost, Amount = offer.Amount,
            Started = world.Date, Ends = world.Date.AddMonths(offer.Months),
        };
        c.Projects.Add(project);
        var price = $"{Fmt.Number(offer.Cost * 100, 2)}% ВВП, {Fmt.Plural(offer.Months, "месяц", "месяца", "месяцев")}";
        string how;
        if (financing == "loan")
        {
            var e = world.Economy![countryId];
            e.Debt += offer.Cost * e.NominalGdp;
            c.Loans.Add(new Loan(lender!, offer.Cost, world.Date, record.Name));
            how = $"в кредит: {world[lender!].Name} даёт {Fmt.Number(offer.Cost * 100, 2)}% ВВП, госдолг сразу вырос";
        }
        else
            how = financing == "ppp" ? "в концессию: строит частный инвестор, потом люди платят за пользование" : "из бюджета";
        world.Log(countryId, "project", $"Начата стройка «{record.Name}» ({price}) — {how}.");
        Projects(world, country, c);
        return true;
    }

    /// <summary>Ключ всех расходов бюджета в Spending0.</summary>
    const string Total = "total";

    /// <summary>Все расходы бюджета без процентов (доля ВВП), без денег программ и строек игрока.</summary>
    static double TotalSpending(World world, Country country, CountryInfrastructure c) =>
        world.Data.Budget.Spending.Sum(s => Spending(world, country, s.Id, c));

    /// <summary>Расходы бюджета на статью (доля ВВП): план плюс поправки законов, без денег программ и строек игрока.</summary>
    static double Spending(World world, Country country, string item, CountryInfrastructure c)
    {
        var k = world.Data.Budget.Spending.ToList().FindIndex(s => s.Id == item);
        var e = world.Economy![country.Id];
        var programs = world.Programs?.Countries.GetValueOrDefault(country.Id)?.Effects.GetValueOrDefault("spending." + item) ?? 0;
        var projects = c.Effects.GetValueOrDefault("spending." + item);
        return k < 0 || k >= e.SpendingPlan.Length ? 0
            : Math.Max(0, e.SpendingPlan[k] + (LawModel.Effect(world, country.Id, "spending." + item) - programs - projects) / 100);
    }

    static double Stat(Country country, string id, double fallback) =>
        country.Stat(id) is var v && double.IsFinite(v) ? v
        : country.Info.Stats.TryGetValue(id, out var data) && double.IsFinite(data.Value) ? data.Value : fallback;

    static void Publish(World world, Country country, CountryInfrastructure c)
    {
        var year = world.Date.Year;
        var types = world.Data.Infrastructure.Types;
        double stock = 0, index = 0, condition = 0;
        var factors = new List<StatFactor>();
        for (var t = 0; t < types.Count; t++)
        {
            if (!Applies(types[t], country) || c.Need[t] <= 0)
                continue;
            var ratio = c.Capacity[t] * c.Condition[t] / c.Need[t] * 100;
            stock += types[t].Stock;
            index += types[t].Stock * Math.Min(150, ratio);
            condition += types[t].Stock * c.Condition[t] * 100;
            factors.Add(new StatFactor(types[t].Name, ratio, "number0"));
        }
        country.Stats["infrastructure_index"] = new StatValue(stock > 0 ? index / stock : 100, year, StatSources.Model, null, null, factors);
        country.Stats["infrastructure_condition_pct"] = new StatValue(stock > 0 ? condition / stock : 100, year, StatSources.Model, null, null, null);
    }
}
