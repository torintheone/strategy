using Politeconomy.Sim.Model;

namespace Politeconomy.Sim.Economy;

/// <summary>
/// Насколько модель за время разогрева ушла от стартовых данных. Показатель «в норме», если отклонение
/// не больше 10% от значения или абсолютного допуска (для ставок и долей — в процентных пунктах).
/// </summary>
public sealed class WarmupReport
{
    /// <summary>
    /// Показатель и абсолютный допуск в его единицах. Текущий счёт — разность экспорта и импорта, поэтому
    /// проверяются сами экспорт и импорт, а текущий счёт только показывается в отчёте.
    /// </summary>
    public static readonly (string Id, double Tolerance, bool Gated)[] Checked =
    [
        ("gdp_growth_pct", 1.0, true),
        ("inflation_pct", 1.0, true),
        ("unemployment_pct", 1.0, true),
        ("budget_balance_pct_gdp", 1.0, true),
        ("gov_debt_pct_gdp", 5.0, true),
        ("exports_pct_gdp", 1.0, true),
        ("imports_pct_gdp", 1.0, true),
        ("current_account_pct_gdp", 1.0, false),
    ];

    public sealed record Deviation(string Country, string Indicator, double Start, double Warmed, bool WithinTolerance);

    public required int Months { get; init; }
    public required IReadOnlyList<Deviation> Deviations { get; init; }
    /// <summary>Доля мирового ВВП в странах, у которых все проверяемые показатели в норме.</summary>
    public required double GdpShareWithinTolerance { get; init; }
    public required double CountryShareWithinTolerance { get; init; }

    public static WarmupReport Measure(World world, int months)
    {
        var deviations = new List<Deviation>();
        double okGdp = 0, totalGdp = 0;
        var okCountries = 0;
        foreach (var country in world.Countries)
        {
            var allOk = true;
            foreach (var (id, tolerance, gated) in Checked)
            {
                if (!country.Info.Stats.TryGetValue(id, out var data) || !country.Stats.TryGetValue(id, out var now))
                    continue;
                // Рост за год — поток, а не состояние: сравниваем с ростом, который модель задаёт на первый год
                // (прогноз МВФ на 2026 год в пределах ±2 п. п. от тренда), а не с ростом 2025 года.
                var start = id == "gdp_growth_pct" ? world.Economy![country.Id].FirstYearGrowth * 100 : data.Value;
                var ok = Math.Abs(now.Value - start) <= Math.Max(0.1 * Math.Abs(start), tolerance);
                allOk &= ok || !gated;
                deviations.Add(new Deviation(country.Id, id, start, now.Value, ok));
            }
            var gdp = country.Info.Stats.GetValueOrDefault("gdp_usd")?.Value ?? 0;
            totalGdp += gdp;
            if (allOk)
            {
                okGdp += gdp;
                okCountries++;
            }
        }
        return new WarmupReport
        {
            Months = months,
            Deviations = deviations,
            GdpShareWithinTolerance = totalGdp > 0 ? okGdp / totalGdp : 0,
            CountryShareWithinTolerance = world.Countries.Count > 0 ? (double)okCountries / world.Countries.Count : 0,
        };
    }
}
