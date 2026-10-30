namespace Politeconomy.Sim.Data;

// Коэффициенты классов из data/balance.json (раздел society). Смысл каждого — в MODEL.md, «Классы».
// Списки из четырёх чисел — по группам дохода: низкий, ниже среднего, выше среднего, высокий.

public sealed class SocietyBalance
{
    // --- Состав классов
    /// <summary>Доля занятых в сельском хозяйстве, которые ведут своё хозяйство (крестьяне и фермеры); остальные — батраки.</summary>
    public double FarmerShare { get; init; } = 0.85;
    /// <summary>Доля самозанятых вне сельского хозяйства, у которых своё дело; остальные — неформальные рабочие.</summary>
    public double SmallBusinessShare { get; init; } = 0.35;
    /// <summary>Крупный капитал и силовики — доли занятых.</summary>
    public double CapitalistShare { get; init; } = 0.004;
    public double SecurityShare { get; init; } = 0.012;
    /// <summary>Охват высшим образованием (%), при котором квалификация в отраслях — как в industries.json.</summary>
    public double TertiaryReference { get; init; } = 45;
    public double SkillTiltMin { get; init; } = 0.3;
    public double SkillTiltMax { get; init; } = 1.4;

    // --- Доходы
    /// <summary>Доля прибыли, которая уходит иностранным владельцам (после налогов и доходов госпредприятий).</summary>
    public IReadOnlyList<double> ForeignProfitShare { get; init; } = [0.25, 0.2, 0.15, 0.1];
    /// <summary>Зарплата в отрасли не выше стольких зарплат медианного работника страны; сверх — рента владельцам.</summary>
    public double WagePremiumMax { get; init; } = 3;
    /// <summary>Доля прибыли сельского хозяйства, которая остаётся у крестьян и фермеров.</summary>
    public double FarmerProfitShare { get; init; } = 0.8;
    /// <summary>Во сколько раз крупный собственник претендует на большую часть прибыли, чем владелец мелкого дела.</summary>
    public double CapitalistWeight { get; init; } = 60;
    /// <summary>Доля прибыли крупного бизнеса, которая достаётся владельцам; остальное — дивиденды, проценты и аренда всем.</summary>
    public double CapitalistTake { get; init; } = 0.5;
    /// <summary>Пенсия — доля средней зарплаты, пособие по безработице — зарплаты неквалифицированного рабочего (по группам дохода).</summary>
    public IReadOnlyList<double> PensionReplacement { get; init; } = [0.15, 0.3, 0.45, 0.5];
    public IReadOnlyList<double> BenefitReplacement { get; init; } = [0.05, 0.1, 0.3, 0.5];
    /// <summary>Пенсии и пособия не больше этих долей расходов на соцзащиту; остальное — семьям поровну на человека.</summary>
    public double PensionShare { get; init; } = 0.8;
    public double BenefitShare { get; init; } = 0.15;
    /// <summary>Случайные заработки безработного относительно зарплаты неквалифицированного рабочего (по группам дохода).</summary>
    public IReadOnlyList<double> InformalEarnings { get; init; } = [0.4, 0.3, 0.2, 0.1];
    /// <summary>Вес класса в прямых налогах относительно его дохода (прогрессия), по порядку classes.json.</summary>
    public IReadOnlyList<double> TaxWeights { get; init; } = [0, 0.6, 0.9, 0.4, 1, 1.5, 0.8, 1.8, 1, 0.3];
    /// <summary>Семья поддерживает пенсионеров и безработных: их доход на человека не ниже этой доли среднего.</summary>
    public double PensionerFloor { get; init; } = 0.5;
    public double UnemployedFloor { get; init; } = 0.35;
    /// <summary>Сколько сбережений в год тратится (покупки, наследство, обесценение).</summary>
    public double SavingsDecay { get; init; } = 0.04;

    // --- Потребности, долл. по ППС 2026 года на человека в год
    public double BasicBasket { get; init; } = 2000;
    public double EverydayBasket { get; init; } = 9000;
    /// <summary>Престижные траты сверх двух корзин по логарифмической шкале: PrestigeBasket — масштаб, PrestigeMax — полный уровень.</summary>
    public double PrestigeBasket { get; init; } = 15000;
    public double PrestigeMax { get; init; } = 150000;
    public double BasicWeight { get; init; } = 40;
    public double EverydayWeight { get; init; } = 30;
    public double PrestigeWeight { get; init; } = 30;
    /// <summary>Доля еды в базовой корзине и насколько мировые цены на еду доходят до прилавка.</summary>
    public double FoodShare { get; init; } = 0.5;
    public double FoodPassThrough { get; init; } = 0.5;

    // --- Настроение
    /// <summary>За сколько месяцев ожидания подстраиваются под достигнутый уровень жизни.</summary>
    public double ExpectationMonths { get; init; } = 36;
    /// <summary>Удовлетворённость: 50 + ChangeWeight × (уровень − ожидания) + LevelWeight × (уровень − 50) − страхи.</summary>
    public double ChangeWeight { get; init; } = 2.5;
    public double LevelWeight { get; init; } = 0.1;
    /// <summary>Минус за каждый процентный пункт безработицы выше естественной (умножается на job_risk класса).</summary>
    public double UnemploymentFear { get; init; } = 1.5;
    /// <summary>Сама безработица неприятна, даже если семья кормит.</summary>
    public double UnemployedPenalty { get; init; } = 8;
    /// <summary>
    /// Минус за инфляцию выше порога (умножается на чувствительность класса): InflationPain за каждый пункт,
    /// пока превышение мало, дальше — по логарифму с масштабом InflationScale (к хронической инфляции привыкают).
    /// </summary>
    public double InflationPain { get; init; } = 0.4;
    public double InflationThreshold { get; init; } = 5;
    public double InflationScale { get; init; } = 20;
    public double SatisfactionMonths { get; init; } = 6;
    /// <summary>Радикализм тянется к спокойному уровню класса + RadicalismSlope × (порог − удовлетворённость).</summary>
    public double RadicalismSpeed { get; init; } = 0.03;
    public double RadicalismThreshold { get; init; } = 40;
    public double RadicalismSlope { get; init; } = 1.2;

    // --- Влияние: вес численности и богатства при выборной власти и без неё
    public double DemocracyNumbers { get; init; } = 0.6;
    public double DemocracyWealth { get; init; } = 0.4;
    public double AutocracyNumbers { get; init; } = 0.2;
    public double AutocracyWealth { get; init; } = 0.5;
    /// <summary>Без выборов госаппарат и крупный капитал ближе к власти: их влияние умножается.</summary>
    public double AutocracyOfficials { get; init; } = 2.5;
    public double AutocracyCapital { get; init; } = 1.5;
}
