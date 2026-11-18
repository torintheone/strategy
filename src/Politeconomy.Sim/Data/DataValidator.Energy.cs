namespace Politeconomy.Sim.Data;

public static partial class DataValidator
{
    /// <summary>energy.json: станции с показателями из indicators.json, стратегии, цены углерода и тарифы — по вариантам законов.</summary>
    static void ValidateEnergy(GameData data, Action<string, string> add)
    {
        const string file = "energy.json";
        var energy = data.Energy;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        var indicators = data.Indicators.Indicators.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var p in energy.Plants)
        {
            if (!ids.Add(p.Id))
                add(file, $"станция {p.Id} встречается дважды");
            if (!indicators.Contains(p.Stat))
                add(file, $"{p.Id}: нет показателя «{p.Stat}» в indicators.json");
            if (p.CapacityFactor is <= 0 or > 1 || p.BuildYears <= 0 || p.LifeYears <= 0)
                add(file, $"{p.Id}: доля часов работы — от 0 до 1, сроки стройки и службы — больше нуля");
            if (p.Fuel is not (null or "oil" or "ores"))
                add(file, $"{p.Id}: топливо — oil, ores или null");
        }
        foreach (var plant in new[] { "gas", "oil", "hydro", "renewables" })
            if (!ids.Contains(plant))
                add(file, $"нет станций «{plant}»: модель на них опирается");
        void Variants(string law, IEnumerable<string> keys, string what)
        {
            var record = data.Laws.Laws.FirstOrDefault(l => l.Id == law);
            if (record == null)
            {
                add(file, $"{what}: нет закона «{law}» в laws.json");
                return;
            }
            var variants = record.Variants.Select(v => v.Id).ToHashSet(StringComparer.Ordinal);
            if (!variants.SetEquals(keys))
                add(file, $"{what}: варианты должны совпадать с вариантами закона «{law}» ({string.Join(", ", variants)})");
        }
        Variants("energy_strategy", energy.Strategies.Keys, "strategies");
        Variants("carbon_tax", energy.CarbonPrices.Keys, "carbon_prices");
        Variants("electricity_tariffs", energy.Tariffs.Keys, "tariffs");
        foreach (var (strategy, weights) in energy.Strategies)
            foreach (var plant in weights.Keys.Where(k => !ids.Contains(k)))
                add(file, $"стратегия {strategy}: нет станций «{plant}»");
        var industries = data.Industries.Industries.Select(i => i.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var industry in energy.Intensity.Keys.Where(k => !industries.Contains(k)))
            add(file, $"intensity: нет отрасли «{industry}» в industries.json");
    }
}
