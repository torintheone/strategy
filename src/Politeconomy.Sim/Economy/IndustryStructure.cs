using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Economy;

/// <summary>
/// Структура экономики страны по 20 отраслям: доли в ВВП и в занятости.
/// Из данных известны только три сектора (сельское хозяйство, промышленность, услуги), обрабатывающая
/// промышленность, рента от сырья, туризм и госпотребление; остальное делится по типичным долям
/// из data/industries.json. Подробно — MODEL.md, «Калибровка отраслей».
/// </summary>
public static class IndustryStructure
{
    /// <summary>Относительная зарплата работника без образования, с профессиональным и с высшим.</summary>
    static readonly double[] SkillWages = [0.6, 1.0, 1.8];

    public sealed record Shares(double[] Output, double[] Employment);

    public static Shares Compute(IReadOnlyList<IndustryRecord> industries, Func<string, double> stat, string incomeKey)
    {
        var n = industries.Count;
        var va = new double[n];
        int Index(string id) => industries.Select((x, i) => (x, i)).First(t => t.x.Id == id).i;

        var agri = Math.Max(0, stat("agriculture_pct_gdp"));
        var ind = Math.Max(0, stat("industry_pct_gdp"));
        var serv = Math.Max(0, stat("services_pct_gdp"));
        var total = agri + ind + serv;
        if (total <= 0)
            (agri, ind, serv, total) = (10, 30, 60, 100);
        agri /= total;
        ind /= total;
        serv /= total;

        // Промышленность: обрабатывающая, добыча (по ренте), остальное — строительство и энергетика.
        var manufacturing = Math.Min(Math.Max(0, stat("manufacturing_pct_gdp")) / total, ind * 0.95);
        var oilGas = 1.2 * (Math.Max(0, stat("oil_rents_pct_gdp")) + Math.Max(0, stat("gas_rents_pct_gdp"))) / 100;
        var ores = 1.2 * (Math.Max(0, stat("coal_rents_pct_gdp")) + Math.Max(0, stat("mineral_rents_pct_gdp"))) / 100;
        var rest = ind - manufacturing;
        if (oilGas + ores > 0.85 * rest && oilGas + ores > 0)
        {
            var scale = 0.85 * rest / (oilGas + ores);
            oilGas *= scale;
            ores *= scale;
        }
        va[Index("agriculture")] = agri;
        va[Index("oil_gas")] = oilGas;
        va[Index("coal_ores")] = ores;
        va[Index("construction")] = rest - oilGas - ores;

        var defense = Math.Min(0.25 * manufacturing, 0.3 * Math.Max(0, stat("military_pct_gdp")) / 100);
        va[Index("defense")] = defense;
        var manufacturingShares = industries.Select(i => i.ManufacturingShare?.GetValueOrDefault(incomeKey) ?? 0).ToArray();
        Spread(va, manufacturingShares, manufacturing - defense);

        // Услуги: туризм по выручке от иностранцев, бюджетные отрасли по госпотреблению, остальное — рыночные услуги.
        var tourism = Math.Min(0.4 * serv, 0.6 * Math.Max(0, stat("tourism_pct_gdp")) / 100);
        va[Index("tourism")] = tourism;
        var govConsumption = Math.Max(0, stat("consumption_gov_pct_gdp") - stat("military_pct_gdp")) / 100;
        var publicServices = Math.Clamp(0.9 * govConsumption, 0.15 * serv, 0.6 * serv);
        var publicShares = industries.Select(i => i.Kind == "public" ? i.ServicesShare ?? 0 : 0).ToArray();
        Spread(va, publicShares, publicServices);
        var marketShares = industries.Select(i => i.MarketService ? i.ServicesShare ?? 0 : 0).ToArray();
        Spread(va, marketShares, serv - tourism - publicServices);

        for (var i = 0; i < n; i++)
            va[i] = Math.Max(0, va[i]);
        Normalize(va);

        // Занятость: сектора — из данных МОТ, внутри сектора — по выпуску и трудоёмкости отрасли.
        var sectorEmployment = new Dictionary<string, double>
        {
            ["agriculture"] = Math.Max(0, stat("employment_agriculture_pct")),
            ["industry"] = Math.Max(0, stat("employment_industry_pct")),
            ["services"] = Math.Max(0, stat("employment_services_pct")),
        };
        if (sectorEmployment.Values.Sum() <= 0)
            sectorEmployment = new() { ["agriculture"] = agri, ["industry"] = ind, ["services"] = serv };
        var employment = new double[n];
        foreach (var (sector, share) in sectorEmployment)
        {
            var weights = new double[n];
            for (var i = 0; i < n; i++)
                if (industries[i].Sector == sector)
                    weights[i] = va[i] * (1 - industries[i].Alpha) / Wage(industries[i]);
            var sum = weights.Sum();
            if (sum <= 0)
                continue;
            for (var i = 0; i < n; i++)
                employment[i] += share * weights[i] / sum;
        }
        Normalize(employment);
        return new Shares(va, employment);
    }

    public static double Wage(IndustryRecord industry) => industry.Skills.Select((s, k) => s * SkillWages[k]).Sum();

    static void Spread(double[] target, double[] weights, double amount)
    {
        var sum = weights.Sum();
        if (sum <= 0 || amount <= 0)
            return;
        for (var i = 0; i < target.Length; i++)
            target[i] += amount * weights[i] / sum;
    }

    static void Normalize(double[] values)
    {
        var sum = values.Sum();
        if (sum <= 0)
            return;
        for (var i = 0; i < values.Length; i++)
            values[i] /= sum;
    }
}
