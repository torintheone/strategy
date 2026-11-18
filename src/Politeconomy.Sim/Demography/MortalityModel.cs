using Politeconomy.Sim.Data;

namespace Politeconomy.Sim.Demography;

/// <summary>
/// Смертность по возрастам по модели Силера: μ(x) = a1·e^(−b1·x) + L·(a2 + a3·e^(b3·x)).
/// Детская часть a1 задаётся младенческой смертностью, уровень взрослой L подбирается так, чтобы
/// продолжительность жизни при рождении совпала с заданной. Результат — годовые коэффициенты смертности
/// по пятилетним группам (0–4 … 75–79, 80+).
/// </summary>
public static class MortalityModel
{
    const double Step = 0.5;
    const int Steps = 230; // до 115 лет

    /// <summary>Множители e^(−b1·x) и e^(b3·x) в серединах шагов — считаются один раз для набора коэффициентов.</summary>
    sealed record Curves(DemographyBalance Balance, double[] Child, double[] Senescent);

    static Curves? _curves;

    static Curves For(DemographyBalance b)
    {
        var c = _curves;
        if (c != null && ReferenceEquals(c.Balance, b))
            return c;
        var child = new double[Steps];
        var old = new double[Steps];
        for (var i = 0; i < Steps; i++)
        {
            var x = (i + 0.5) * Step;
            child[i] = Math.Exp(-b.ChildDecline * x);
            old[i] = b.Background + b.Senescence * Math.Exp(b.SenescenceSlope * x);
        }
        return _curves = new Curves(b, child, old);
    }

    public static double[] Rates(double lifeExpectancy, double infantDeathsPer1000, DemographyBalance b)
    {
        var curves = For(b);
        var q0 = Math.Clamp(infantDeathsPer1000 / 1000, 0.0005, 0.3);
        var a1 = -Math.Log(1 - q0) * b.ChildDecline / (1 - Math.Exp(-b.ChildDecline));
        double lo = Math.Log(0.001), hi = Math.Log(300);
        for (var i = 0; i < 32; i++)
        {
            var mid = (lo + hi) / 2;
            if (LifeExpectancy(a1, Math.Exp(mid), curves) > lifeExpectancy)
                lo = mid;
            else
                hi = mid;
        }
        return GroupRates(a1, Math.Exp((lo + hi) / 2), curves);
    }

    /// <summary>Продолжительность жизни: площадь под кривой дожития.</summary>
    static double LifeExpectancy(double a1, double level, Curves c)
    {
        double survival = 1, total = 0;
        for (var i = 0; i < Steps; i++)
        {
            var next = survival * Math.Exp(-(a1 * c.Child[i] + level * c.Senescent[i]) * Step);
            total += (survival + next) / 2 * Step;
            survival = next;
            if (survival < 1e-6)
                break;
        }
        return total;
    }

    static double[] GroupRates(double a1, double level, Curves c)
    {
        var rates = new double[CountryDemography.Groups];
        double survival = 1, deaths = 0, years = 0;
        var group = 0;
        const int stepsPerGroup = (int)(5 / Step);
        for (var i = 0; i < Steps; i++)
        {
            var next = survival * Math.Exp(-(a1 * c.Child[i] + level * c.Senescent[i]) * Step);
            deaths += survival - next;
            years += (survival + next) / 2 * Step;
            survival = next;
            if (group < CountryDemography.Groups - 1 && (i + 1) % stepsPerGroup == 0)
            {
                rates[group] = years > 0 ? deaths / years : 0;
                group++;
                deaths = years = 0;
            }
        }
        // Открытая группа 80+: все оставшиеся умирают; коэффициент — умершие на прожитые годы.
        rates[^1] = years > 0 ? (deaths + survival) / years : 0.2;
        return rates;
    }
}
