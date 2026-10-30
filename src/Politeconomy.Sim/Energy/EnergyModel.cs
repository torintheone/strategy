using Politeconomy.Sim.Data;
using Politeconomy.Sim.Engine;
using Politeconomy.Sim.Laws;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Energy;

/// <summary>
/// Электроэнергетика. Спрос — от отраслей (по их электроёмкости) и населения (по доходу и доступу к сетям).
/// Станции шести типов: атом, гидро, ветер и солнце работают всегда, уголь, газ и мазут догружаются по спросу.
/// Энергетики строят новые станции с запасом, но стройка занимает годы, а денег на неё может не хватить; старые
/// станции списываются. Если выработки и импорта мало — веерные отключения: выпуск отраслей падает, люди злятся.
/// MODEL.md, «Энергетика».
/// </summary>
public static class EnergyModel
{
    public static IReadOnlyList<ISimSystem> Systems() => [new Systems.EnergySystem()];

    /// <summary>Часов в году / 1000: ГВт мощности × доля часов работы × это = ТВт·ч в год.</summary>
    const double Hours = 8.76;

    public static void Setup(World world)
    {
        var data = world.Data;
        var b = data.EnergyBalance;
        var plants = data.Energy.Plants;
        var energy = new WorldEnergy();
        world.Energy = energy;
        foreach (var country in world.Countries)
        {
            var income = Math.Clamp((int)country.Income, 0, 3);
            var e = world.Economy![country.Id];
            var d = world.Demography![country.Id];
            var kwh = Math.Max(10, Stat(country, "electricity_kwh_per_capita", 1000));
            var demand = kwh * d.Total / 1e9;
            var imports = Math.Clamp(Stat(country, "power_imports_pct", 0) / 100, -b.ExportMax, b.ImportMax);
            var own = demand * (1 - imports);

            var shares = plants.Select(p => Math.Max(0, Stat(country, p.Stat, 0))).ToArray();
            var sum = shares.Sum();
            if (sum <= 0)
            {
                shares[Math.Max(0, plants.ToList().FindIndex(p => p.Id == "oil"))] = 1;
                sum = 1;
            }
            for (var t = 0; t < shares.Length; t++)
                shares[t] /= sum;

            // Мощность: станции «по спросу» держат запас; если их нет, запас — у всех.
            var reserve = b.Reserve[income];
            var flexible = Enumerable.Range(0, plants.Count).Where(t => !plants[t].Base).Sum(t => shares[t]);
            var capacity = new double[plants.Count];
            for (var t = 0; t < plants.Count; t++)
            {
                var margin = flexible > 0.01 ? (plants[t].Base ? 0 : Math.Min(3, reserve / flexible)) : reserve;
                capacity[t] = shares[t] * own * (1 + margin) / (plants[t].CapacityFactor * Hours);
            }
            // Стройка в равновесии: каждый год вводится столько, сколько списывается и сколько нужно на рост спроса.
            var growth = Math.Clamp(e.TrendGrowth - b.Efficiency, 0, b.GrowthMax);
            var building = new double[plants.Count];
            for (var t = 0; t < plants.Count; t++)
                building[t] = capacity[t] * (1 / plants[t].LifeYears + growth) * plants[t].BuildYears;

            var c = new CountryEnergy
            {
                Capacity = capacity, Capacity0 = (double[])capacity.Clone(), Building = building,
                Generation = new double[plants.Count], Share0 = shares,
                Demand = demand, Demand0 = demand, ImportShare = imports, Growth = growth,
                Access = Stat(country, "electricity_access_pct", 100), Households = b.Households[income],
                Business0 = Business(world, e), Population0 = d.Total, Income0 = SocietyModel.IncomeGrowth(e),
                Spending0 = Spending(world, country, "energy"), Investment0 = e.InvestmentRate,
                Corruption0 = Stat(country, "corruption_index", 50), HydroYear = world.Date.Year,
                Cut = new double[data.Industries.Industries.Count], Mood = new double[ClassIds.Count],
            };
            c.Access0 = Math.Max(1, c.Access);
            c.Cost0 = Enumerable.Range(0, plants.Count).Select(t => Cost(world, country, plants[t])).ToArray();
            Dispatch(world, country, c);
            c.AverageCost0 = AverageCost(world, country, c);
            c.Pollution0 = Pollution(plants, c);
            energy.Countries[country.Id] = c;
            Publish(world, country, c);
        }
    }

    /// <summary>Месяц: спрос, стройка и списание, выработка и нехватка, цена, доступ к сетям, настроение и поправки.</summary>
    public static void Update(World world, Country country, CountryEnergy c)
    {
        var data = world.Data;
        var b = data.EnergyBalance;
        var plants = data.Energy.Plants;
        var e = world.Economy![country.Id];
        var d = world.Demography![country.Id];
        var years = (world.Date.DayNumber - data.Scenario.StartDate.DayNumber) / 365.25;
        double Fx(string key) => LawModel.Effect(world, country.Id, key);

        // Вода для ГЭС — раз в год: засуха или полноводье.
        if (world.Date.Year != c.HydroYear)
        {
            c.HydroYear = world.Date.Year;
            var random = new DeterministicRandom(world.Seed ^ Hash(country.Id) ^ ((ulong)world.Date.Year * 0x9E3779B97F4A7C15UL));
            c.Hydro = Math.Clamp(1 + b.HydroVariation * random.NextGaussian(), b.HydroMin, b.HydroMax);
        }

        // Спрос: отрасли по электроёмкости, население — по доходу и доступу к сетям; энергоэффективность растёт.
        var business = c.Business0 > 0 ? Business(world, e, c.Cut) / c.Business0 : 1;
        var income = c.Income0 > 0 ? SocietyModel.IncomeGrowth(e) / c.Income0 : 1;
        var households = d.Total / c.Population0 * Math.Pow(Math.Max(0.1, income), b.HouseholdIncome) * c.Access / c.Access0;
        var demand = c.Demand0 * ((1 - c.Households) * business + c.Households * households)
                     * Math.Exp(-b.Efficiency * years) * Math.Max(0.5, 1 + Fx("power_demand"));
        if (c.Demand > 0 && demand > 0)
            c.Growth = Math.Clamp(c.Growth + (Math.Log(demand / c.Demand) * 12 - c.Growth) / b.GrowthMonths, -b.GrowthMax, b.GrowthMax);
        c.Demand = demand;

        // Стройка заканчивается, старые станции списываются.
        for (var t = 0; t < plants.Count; t++)
        {
            var done = c.Building[t] / (12 * plants[t].BuildYears);
            c.Building[t] -= done;
            c.Capacity[t] += done - c.Capacity[t] / (12 * plants[t].LifeYears);
        }

        // Деньги на стройку: бюджет энергетики, инвестиции в экономике, законы и программы, коррупция.
        var spending = Math.Clamp(c.Spending0 > 0 ? Spending(world, country, "energy") / c.Spending0 : 1, 1 / b.SpendingRange, b.SpendingRange);
        var investment = c.Investment0 > 0 ? Math.Max(0.1, e.InvestmentRate / c.Investment0) : 1;
        var corruption = Stat(country, "corruption_index", c.Corruption0) - c.Corruption0;
        c.Funding = Math.Clamp(Math.Pow(spending, b.FundingSpending) * Math.Pow(investment, b.FundingInvestment)
                               * Math.Max(0, 1 + Fx("power_investment")) * (1 - b.FundingCorruption * corruption), b.FundingMin, b.FundingMax);

        Order(world, country, c, years);
        Dispatch(world, country, c);
        c.CostRatio = c.AverageCost0 > 0 ? AverageCost(world, country, c) / c.AverageCost0 : 1;
        c.Price = 100 * c.CostRatio * (1 + b.ShortagePrice * c.Shortage);

        // Доступ к электричеству растёт с деньгами; при отключениях — медленнее.
        c.Access += (100 - c.Access) * b.AccessRate / 12 * Math.Min(c.Funding, 1.5) * Math.Max(0, 1 + Fx("power_access"))
                    * Math.Max(0, 1 - 2 * c.Shortage);

        // Отключения бьют по отраслям по их электроёмкости.
        var industries = data.Industries.Industries;
        var weighted = 0.0;
        var output = 0.0;
        for (var i = 0; i < industries.Count && i < e.Output.Length; i++)
        {
            weighted += Intensity(data, industries[i].Id) * e.Output[i];
            output += e.Output[i];
        }
        var mean = output > 0 && weighted > 0 ? weighted / output : 1;
        if (c.Cut.Length != industries.Count)
            c.Cut = new double[industries.Count];
        for (var i = 0; i < industries.Count; i++)
            c.Cut[i] = Math.Min(b.CutMax, b.PowerCut * c.Shortage * Intensity(data, industries[i].Id) / mean);

        // Настроение: отключения злят всех, рост тарифов — тех, кто их платит.
        var pass = data.Energy.Tariffs.GetValueOrDefault(Variant(world, country, "electricity_tariffs", "regulated"), 0.4);
        var tariff = b.TariffMood * pass * 100 * Math.Log(Math.Max(0.1, c.CostRatio));
        if (c.Mood.Length != ClassIds.Count)
            c.Mood = new double[ClassIds.Count];
        for (var k = 0; k < ClassIds.Count; k++)
            c.Mood[k] = -b.BlackoutMood * c.Shortage * 100 - tariff * (k is ClassIds.Capitalists or ClassIds.Officials ? 0.5 : 1);

        var fx = c.Effects;
        fx.Clear();
        fx["pollution"] = b.PollutionShare * (Pollution(plants, c) - c.Pollution0);
        // Рост тормозит электричество, подорожавшее сильнее, чем в мире: общее удешевление уже в прогнозах роста.
        fx["potential_growth"] = -b.PriceGrowth * Math.Log(Math.Max(0.1, c.CostRatio) / Math.Max(0.1, world.Energy!.CostRatio));
        Publish(world, country, c);
    }

    /// <summary>
    /// Заказ новых станций (в ТВт·ч возможной выработки): замена списанных и рост спроса, плюс догон до нужного
    /// запаса и до нужного объёма стройки. Деньги ограничивают заказ: в год — не больше BuildMax × деньги от нужной
    /// выработки. Заказ делится между типами по их привлекательности.
    /// </summary>
    static void Order(World world, Country country, CountryEnergy c, double years)
    {
        var data = world.Data;
        var b = data.EnergyBalance;
        var plants = data.Energy.Plants;
        var income = Math.Clamp((int)country.Income, 0, 3);
        var need = c.Demand * (1 - c.ImportShare) * (1 + b.Reserve[income]);
        double available = 0, retiring = 0, pipeline = 0;
        for (var t = 0; t < plants.Count; t++)
        {
            var a = c.Capacity[t] * plants[t].CapacityFactor * Hours;
            available += a;
            retiring += a / plants[t].LifeYears;
            pipeline += c.Building[t] * plants[t].CapacityFactor * Hours;
        }
        var weights = Weights(world, country, c, years);
        var buildYears = Enumerable.Range(0, plants.Count).Sum(t => weights[t] * plants[t].BuildYears);
        var baseline = retiring + Math.Max(0, c.Growth) * need;
        var orders = Math.Min(baseline + (need - available) * 12 / b.ReserveMonths + (baseline * buildYears - pipeline) * 12 / b.PipelineMonths,
            b.BuildMax * c.Funding * need) / 12;
        if (orders <= 0)
            return;
        for (var t = 0; t < plants.Count; t++)
            c.Building[t] += orders * weights[t] / (plants[t].CapacityFactor * Hours);
    }

    /// <summary>Привлекательность нового строительства по типам (сумма — 1).</summary>
    public static double[] Weights(World world, Country country, CountryEnergy c, double years)
    {
        var data = world.Data;
        var b = data.EnergyBalance;
        var plants = data.Energy.Plants;
        var income = Math.Clamp((int)country.Income, 0, 3);
        var strategy = data.Energy.Strategies.GetValueOrDefault(Variant(world, country, "energy_strategy", "market"));
        var total = c.Generation.Sum();
        var weights = new double[plants.Count];
        for (var t = 0; t < plants.Count; t++)
        {
            var p = plants[t];
            if (c.Share0[t] <= 0 && income < p.MinIncome)
                continue;
            var w = p.Weight * (c.Share0[t] + b.Inertia) * (strategy?.GetValueOrDefault(p.Id, 1) ?? 1);
            if (p.Resource != null && Stat(country, p.Resource, 0) > b.ResourceRent)
                w *= 1 + b.ResourceBonus;
            var cost = Cost(world, country, p);
            if (cost > 0 && c.Cost0[t] > 0)
                w *= Math.Pow(c.Cost0[t] / cost, b.CostElasticity);
            if (p.Id == "hydro")
                w *= c.Capacity0[t] > 0 ? Math.Max(0, 1 - (c.Capacity[t] + c.Building[t]) / (c.Capacity0[t] * b.HydroRoom)) : 0;
            if (p.Id == "renewables")
            {
                var share = total > 0 ? c.Generation[t] / total : 0;
                w *= Math.Max(0, 1 + LawModel.Effect(world, country.Id, "power_renewables"))
                     * Math.Clamp(1 - (share - b.IntermittentFrom) / b.IntermittentRange, 0.05, 1);
            }
            weights[t] = Math.Max(0, w);
        }
        var sum = weights.Sum();
        if (sum <= 0)
        {
            weights[Math.Max(0, plants.ToList().FindIndex(p => p.Id == "gas"))] = 1;
            sum = 1;
        }
        for (var t = 0; t < weights.Length; t++)
            weights[t] /= sum;
        return weights;
    }

    /// <summary>
    /// Выработка: атом, гидро, ветер и солнце дают всё, что могут (лишнее не нужно); уголь, газ и мазут догружают
    /// остаток пропорционально мощности. Чего не хватило — докупают у соседей (до ImportFlex спроса), остальное —
    /// нехватка.
    /// </summary>
    static void Dispatch(World world, Country country, CountryEnergy c)
    {
        var b = world.Data.EnergyBalance;
        var plants = world.Data.Energy.Plants;
        var need = c.Demand * (1 - c.ImportShare);
        var available = new double[plants.Count];
        double baseSum = 0, flexSum = 0;
        for (var t = 0; t < plants.Count; t++)
        {
            available[t] = c.Capacity[t] * plants[t].CapacityFactor * Hours * (plants[t].Id == "hydro" ? c.Hydro : 1);
            if (plants[t].Base)
                baseSum += available[t];
            else
                flexSum += available[t];
        }
        var deficit = 0.0;
        for (var t = 0; t < plants.Count; t++)
        {
            if (plants[t].Base)
                c.Generation[t] = baseSum > need ? available[t] * need / baseSum : available[t];
            else
            {
                var rest = Math.Max(0, need - baseSum);
                c.Generation[t] = flexSum > rest ? (flexSum > 0 ? available[t] * rest / flexSum : 0) : available[t];
            }
        }
        if (baseSum + flexSum < need)
            deficit = need - baseSum - flexSum;
        var extra = Math.Abs(c.ImportShare) > 0.005 ? Math.Min(deficit, b.ImportFlex * c.Demand) : 0;
        c.Imports = c.Demand * c.ImportShare + extra;
        c.Shortage = c.Demand > 0 ? Math.Max(0, deficit - extra) / c.Demand : 0;
    }

    /// <summary>Цена электричества от станций типа: постоянная часть (ветер и солнце дешевеют), топливо по мировой цене, углерод.</summary>
    static double Cost(World world, Country country, PlantRecord p)
    {
        var economy = world.Economy!;
        var years = (world.Date.DayNumber - world.Data.Scenario.StartDate.DayNumber) / 365.25;
        var fuel = p.Fuel switch
        {
            "oil" => economy.OilPrice,
            "ores" => economy.OresPrice,
            _ => 1,
        };
        var carbon = world.Data.Energy.CarbonPrices.GetValueOrDefault(Variant(world, country, "carbon_tax", "none"));
        var b = world.Data.EnergyBalance;
        var decline = b.CostFloor + (1 - b.CostFloor) * Math.Exp(-p.CostDecline * years);
        return p.Capex * decline + p.FuelCost * fuel + b.CarbonCost * carbon * p.Co2;
    }

    static double AverageCost(World world, Country country, CountryEnergy c)
    {
        var plants = world.Data.Energy.Plants;
        double sum = 0, generation = 0;
        for (var t = 0; t < plants.Count; t++)
        {
            sum += c.Generation[t] * Cost(world, country, plants[t]);
            generation += c.Generation[t];
        }
        return generation > 0 ? sum / generation : c.AverageCost0;
    }

    /// <summary>Грязность выработки: Σ доля × вклад станции в загрязнение.</summary>
    static double Pollution(IReadOnlyList<PlantRecord> plants, CountryEnergy c)
    {
        var total = c.Generation.Sum();
        return total > 0 ? Enumerable.Range(0, plants.Count).Sum(t => c.Generation[t] / total * plants[t].Pollution) : 0;
    }

    /// <summary>
    /// Выпуск отраслей, взвешенный по электроёмкости, — каким он был бы без отключений: иначе нехватка сама себя
    /// «гасит» (заводы стоят — и спрос на электричество падает до того, что есть).
    /// </summary>
    static double Business(World world, Economy.CountryEconomy e, double[]? cut = null)
    {
        var industries = world.Data.Industries.Industries;
        var sum = 0.0;
        for (var i = 0; i < industries.Count && i < e.Output.Length; i++)
            sum += Intensity(world.Data, industries[i].Id) * e.Output[i] / (cut != null && i < cut.Length ? 1 - cut[i] : 1);
        return sum;
    }

    static double Intensity(GameData data, string industry) => data.Energy.Intensity.GetValueOrDefault(industry, 1);

    /// <summary>Расходы бюджета на статью (доля ВВП): план плюс поправки законов, без денег программ.</summary>
    static double Spending(World world, Country country, string item)
    {
        var k = world.Data.Budget.Spending.ToList().FindIndex(s => s.Id == item);
        var e = world.Economy![country.Id];
        var programs = world.Programs?.Countries.GetValueOrDefault(country.Id)?.Effects.GetValueOrDefault("spending." + item) ?? 0;
        return k < 0 || k >= e.SpendingPlan.Length ? 0
            : Math.Max(0, e.SpendingPlan[k] + (LawModel.Effect(world, country.Id, "spending." + item) - programs) / 100);
    }

    /// <summary>Вариант закона в стране (до создания законов — стартовый по данным).</summary>
    static string Variant(World world, Country country, string law, string fallback)
    {
        if (world.Laws?.Countries.GetValueOrDefault(country.Id)?.Current.GetValueOrDefault(law) is { } current)
            return current;
        var record = world.Data.Laws.Laws.FirstOrDefault(l => l.Id == law);
        return record != null ? LawModel.StartVariant(record, country) : fallback;
    }

    static double Stat(Country country, string id, double fallback) =>
        country.Stat(id) is var v && double.IsFinite(v) ? v
        : country.Info.Stats.TryGetValue(id, out var data) && double.IsFinite(data.Value) ? data.Value : fallback;

    static ulong Hash(string text)
    {
        var h = 14695981039346656037UL;
        foreach (var ch in text)
            h = (h ^ ch) * 1099511628211UL;
        return h;
    }

    /// <summary>Сколько станции могут выработать за год (ТВт·ч) с нынешней водой для ГЭС.</summary>
    public static double Available(World world, CountryEnergy c)
    {
        var plants = world.Data.Energy.Plants;
        var sum = 0.0;
        for (var t = 0; t < plants.Count && t < c.Capacity.Length; t++)
            sum += c.Capacity[t] * plants[t].CapacityFactor * Hours * (plants[t].Id == "hydro" ? c.Hydro : 1);
        return sum;
    }

    static void Publish(World world, Country country, CountryEnergy c)
    {
        var year = world.Date.Year;
        var plants = world.Data.Energy.Plants;
        void Set(string id, double value, params StatFactor[] factors) =>
            country.Stats[id] = new StatValue(value, year, StatSources.Model, null, null, factors.Length > 0 ? factors : null);
        var total = c.Generation.Sum();
        var population = world.Demography![country.Id].Total;
        var available = Available(world, c);
        double lowCarbon = 0, co2 = 0, imported = 0;
        for (var t = 0; t < plants.Count; t++)
        {
            var p = plants[t];
            var share = total > 0 ? Math.Clamp(c.Generation[t] / total * 100, 0, 100) : 0;
            Set(p.Stat, share);
            if (p.Co2 <= 0)
                lowCarbon += share;
            co2 += c.Generation[t] * p.Co2;
            if (p.Resource != null && Stat(country, p.Resource, 0) <= world.Data.EnergyBalance.ResourceRent)
                imported += share;
        }
        var need = c.Demand * (1 - c.ImportShare);
        Set("electricity_kwh_per_capita", population > 0 ? c.Demand * 1e9 / population : 0);
        Set("power_shortage_pct", c.Shortage * 100,
            new StatFactor("Спрос, ТВт·ч в год", c.Demand, "number1"),
            new StatFactor("Станции могут выработать", available, "number1"),
            new StatFactor("Импорт", c.Imports, "number1_signed"));
        Set("power_reserve_pct", need > 0 ? (available / need - 1) * 100 : 0);
        Set("power_imports_pct", c.Demand > 0 ? c.Imports / c.Demand * 100 : 0);
        Set("electricity_price_index", c.Price);
        Set("electricity_access_pct", c.Access);
        Set("low_carbon_power_pct", Math.Clamp(lowCarbon, 0, 100));
        Set("power_co2_mt", co2);
        // Зависимость от чужого топлива: импорт электричества и выработка на привозном угле, газе и мазуте.
        Set("energy_import_dependence_pct", Math.Clamp(imported * (1 - Math.Max(0, c.ImportShare)) + Math.Max(0, c.ImportShare) * 100, 0, 100));
    }
}
