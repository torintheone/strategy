using Politeconomy.Sim.Data;
using Politeconomy.Sim.Model;
using Politeconomy.Sim.Society;

namespace Politeconomy.Sim.Politics;

/// <summary>
/// Взгляды классов по трём осям (экономика, общество, внешний мир; −100…100). Основа — средние взгляды класса
/// (data/classes.json) плюс сдвиг страны; радикализм растягивает их к краям, недовольство сдвигает к
/// перераспределению, порядку и суверенитету. MODEL.md, «Взгляды».
/// </summary>
public static class PoliticalViews
{
    public const int Axes = 3;

    /// <summary>Сдвиг взглядов страны: по уровню дохода, по режиму и случайный.</summary>
    public static double[] CountryOffset(Country country, PoliticsBalance b, DeterministicRandom r)
    {
        var income = (int)country.Income;
        var regime = (int)country.Regime;
        static double At(IReadOnlyList<double> list, int i) => list[Math.Clamp(i, 0, list.Count - 1)];
        return
        [
            At(b.IncomeEconomy, income) + r.NextGaussian() * b.CountrySpread,
            At(b.IncomeCivil, income) + At(b.RegimeCivil, regime) + r.NextGaussian() * b.CountrySpread,
            At(b.IncomeForeign, income) + At(b.RegimeForeign, regime) + r.NextGaussian() * b.CountrySpread,
        ];
    }

    /// <summary>К каким взглядам класс придёт при нынешних удовлетворённости и радикализме.</summary>
    public static double[] Target(ClassRecord c, double[] offset, double satisfaction, double radicalism, PoliticsBalance b)
    {
        var stretch = 1 + b.RadicalStretch * Math.Max(0, radicalism - c.Radicalism) / 100;
        var discontent = Math.Max(0, 50 - satisfaction) / 50;
        var view = new double[Axes];
        for (var a = 0; a < Axes; a++)
        {
            var calm = (c.Views is { } v && a < v.Count ? v[a] : 0) + offset[a];
            view[a] = Math.Clamp(calm * stretch + b.CrisisShift[a] * discontent, -100, 100);
        }
        return view;
    }

    /// <summary>Взвешенное расстояние между позициями, в сотнях единиц оси.</summary>
    public static double Distance(IReadOnlyList<double> a, IReadOnlyList<double> b, IReadOnlyList<double> salience)
    {
        var sum = 0.0;
        for (var i = 0; i < Axes; i++)
        {
            var d = (a[i] - b[i]) / 100;
            sum += salience[i] * d * d;
        }
        return Math.Sqrt(sum);
    }

    /// <summary>Средние взгляды взрослых страны (классы взвешены по числу работающих, безработных и пенсионеров).</summary>
    public static double[] Mean(double[][] views, CountrySociety s)
    {
        var mean = new double[Axes];
        var total = s.Heads.Sum();
        if (total <= 0 || views.Length == 0)
            return mean;
        for (var k = 0; k < views.Length; k++)
            for (var a = 0; a < Axes; a++)
                mean[a] += views[k][a] * s.Heads[k] / total;
        return mean;
    }

    /// <summary>Средние взгляды классов из данных (без сдвига страны) — точка отсчёта для позиций семейств партий.</summary>
    public static double[] Center(IReadOnlyList<ClassRecord> classes)
    {
        var center = new double[Axes];
        foreach (var c in classes)
            for (var a = 0; a < Axes; a++)
                center[a] += (c.Views is { } v && a < v.Count ? v[a] : 0) / classes.Count;
        return center;
    }
}
