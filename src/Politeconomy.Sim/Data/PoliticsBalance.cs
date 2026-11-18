namespace Politeconomy.Sim.Data;

// Коэффициенты взглядов, партий и опросов из data/balance.json (раздел politics). Смысл — в MODEL.md, «Взгляды»,
// «Партии» и «Опросы». Списки из четырёх чисел — по группам дохода: низкий, ниже среднего, выше среднего, высокий;
// из трёх — по режимам: демократия, гибридный, авторитарный (или по осям: экономика, общество, внешний мир).

public sealed class PoliticsBalance
{
    /// <summary>Сдвиг средних взглядов страны по группам дохода — по каждой оси.</summary>
    public IReadOnlyList<double> IncomeEconomy { get; init; } = [-10, -5, 0, 5];
    public IReadOnlyList<double> IncomeCivil { get; init; } = [25, 15, 5, -15];
    public IReadOnlyList<double> IncomeForeign { get; init; } = [15, 10, 5, -10];
    /// <summary>Сдвиг по режимам: несвободные режимы воспитывают «порядок» и «суверенитет».</summary>
    public IReadOnlyList<double> RegimeCivil { get; init; } = [-5, 5, 10];
    public IReadOnlyList<double> RegimeForeign { get; init; } = [0, 10, 15];
    /// <summary>Случайный сдвиг взглядов страны по каждой оси (стандартное отклонение).</summary>
    public double CountrySpread { get; init; } = 10;

    /// <summary>Насколько важна каждая ось при выборе партии.</summary>
    public IReadOnlyList<double> Salience { get; init; } = [1, 0.8, 0.6];
    /// <summary>Расстояние до партии (в сотнях единиц оси), которое снижает её привлекательность на единицу.</summary>
    public double DistanceScale { get; init; } = 0.3;
    /// <summary>Радикализм выше спокойного растягивает взгляды к краям: ×(1 + RadicalStretch × прирост/100).</summary>
    public double RadicalStretch { get; init; } = 1;
    /// <summary>Сдвиг взглядов недовольного класса (при удовлетворённости 0) по осям.</summary>
    public IReadOnlyList<double> CrisisShift { get; init; } = [-20, 15, 20];
    /// <summary>За сколько месяцев взгляды и опросы догоняют новые условия.</summary>
    public double ViewMonths { get; init; } = 6;
    public double PollMonths { get; init; } = 3;
    /// <summary>Погрешность опроса, процентные пункты.</summary>
    public double PollNoise { get; init; } = 0.8;
    /// <summary>Ответственность власти: удовлетворённость 0 снижает привлекательность правящих партий на Incumbency.</summary>
    public double Incumbency { get; init; } = 1.5;
    /// <summary>
    /// Экономическое голосование, на процентный пункт: разрыв выпуска (спад наказывается, подъём награждается),
    /// безработица выше естественной, инфляция выше цели больше чем на 2 п. п.
    /// </summary>
    public double GapWeight { get; init; } = 0.03;
    public double UnemploymentWeight { get; init; } = 0.05;
    public double InflationWeight { get; init; } = 0.02;
    /// <summary>Популярность лидера (−50…+50 от середины) и организованность партии (0–1 от 0,5).</summary>
    public double LeaderWeight { get; init; } = 0.6;
    public double OrganizationWeight { get; init; } = 0.8;
    /// <summary>Усталость от власти: минус к привлекательности правящих за год у власти и её предел.</summary>
    public double FatiguePerYear { get; init; } = 0.08;
    public double FatigueMax { get; init; } = 0.6;
    /// <summary>Сколько лет (до) правительство демократии у власти на старте; у партии власти — от этого до втрое больше.</summary>
    public int StartTenure { get; init; } = 8;
    /// <summary>Случайный дрейф привлекательности партии: разброс за месяц и скорость возврата к нулю.</summary>
    public double MomentumSpread { get; init; } = 0.04;
    public double MomentumReversion { get; init; } = 0.05;
    /// <summary>В мажоритарной демократии партии кроме двух ведущих теряют столько привлекательности («потерянный голос»).</summary>
    public double WastedVote { get; init; } = 0.7;
    /// <summary>Административный ресурс партии власти по режимам.</summary>
    public IReadOnlyList<double> AdminBonus { get; init; } = [0, 1, 1.8];

    /// <summary>Сколько партий создаётся (от и до): по избирательной системе для демократий, по режиму для остальных.</summary>
    public IReadOnlyList<int> MajoritarianParties { get; init; } = [3, 4];
    public IReadOnlyList<int> ProportionalParties { get; init; } = [5, 7];
    public IReadOnlyList<int> MixedParties { get; init; } = [4, 6];
    public IReadOnlyList<int> HybridParties { get; init; } = [4, 5];
    public IReadOnlyList<int> AuthoritarianParties { get; init; } = [2, 4];
    public IReadOnlyList<int> OnePartyParties { get; init; } = [1, 3];
    /// <summary>Позиция партии = средние взгляды страны + FamilyScale × (позиция семейства − средняя) + разброс.</summary>
    public double FamilyScale { get; init; } = 0.8;
    public double PositionSpread { get; init; } = 8;
    /// <summary>
    /// Две крупные партии мажоритарной системы ближе к центру (BigScale вместо FamilyScale) и по экономике
    /// не ближе BigSide к средним взглядам: левая — левее, правая — правее.
    /// </summary>
    public double BigScale { get; init; } = 0.45;
    public double BigSide { get; init; } = 10;
    /// <summary>Организованность партий 0–1: у двух крупных партий мажоритарной системы, у остальных, у партии власти.</summary>
    public double BigOrganization { get; init; } = 0.75;
    public double OrganizationMean { get; init; } = 0.5;
    public double OrganizationSpread { get; init; } = 0.12;
    public double PowerOrganization { get; init; } = 0.85;
    /// <summary>В мажоритарной системе доля мест пропорциональна доле голосов в этой степени.</summary>
    public double MajoritarianPower { get; init; } = 2;
    /// <summary>Сколько партнёров может быть в коалиции.</summary>
    public int CoalitionPartners { get; init; } = 3;
    /// <summary>Опросы до старта: сколько месяцев и случайный дрейф долей за месяц (в долях, логарифмически).</summary>
    public int PollBackfill { get; init; } = 12;
    public double BackfillDrift { get; init; } = 0.05;
    /// <summary>Сколько месяцев опросов хранится.</summary>
    public int PollHistory { get; init; } = 36;

    // --- Выборы (MODEL.md, «Выборы»)

    /// <summary>Явка: радикализм выше спокойного поднимает её, недовольство (удовлетворённость ниже 45) снижает; пределы.</summary>
    public double TurnoutRadicalism { get; init; } = 0.5;
    public double TurnoutApathy { get; init; } = 0.3;
    public double TurnoutMin { get; init; } = 0.15;
    public double TurnoutMax { get; init; } = 0.95;
    /// <summary>Своя постоянная поправка явки у каждой страны (относительная, стандартное отклонение).</summary>
    public double TurnoutSpread { get; init; } = 0.12;
    /// <summary>Погрешность дня выборов: логарифмический разброс доли каждой партии.</summary>
    public double ElectionNoise { get; init; } = 0.05;
    /// <summary>На президентских выборах популярность кандидата весит ещё столько (сверх LeaderWeight).</summary>
    public double CandidateWeight { get; init; } = 0.8;
    /// <summary>Итоги по регионам: разброс взглядов региона, сдвиг за каждый порядок плотности, чувствительность.</summary>
    public double RegionSpread { get; init; } = 12;
    public double RegionDensity { get; init; } = 8;
    public double RegionScale { get; init; } = 0.15;
    /// <summary>Сколько итогов выборов хранится у страны.</summary>
    public int ElectionsKept { get; init; } = 6;
    /// <summary>Переговоры о коалиции: срок (месяцев), партия отказывается, если дальше по взглядам; требование закона —
    /// если одобрение её избирателей не ниже DemandSupport; обещание надо выполнить за PromiseYears лет.</summary>
    public int TalksMonths { get; init; } = 1;
    public double CoalitionRefuse { get; init; } = 0.8;
    public double DemandSupport { get; init; } = 0.6;
    public int PromiseYears { get; init; } = 2;
    /// <summary>Вероятность, что президент на старте уже на втором сроке (если сроков два).</summary>
    public double SecondTermShare { get; init; } = 0.4;

    // --- Кампания и фальсификации (MODEL.md, «Кампания»)

    /// <summary>Кампания начинается за столько месяцев до выборов.</summary>
    public int CampaignMonths { get; init; } = 3;
    /// <summary>Очки штаба в месяц: основа + за организованность партии; копятся не больше чем на три месяца.</summary>
    public double CampaignPoints { get; init; } = 3;
    public double CampaignOrganization { get; init; } = 2;
    /// <summary>Поездка к классу и агитация в СМИ: цена в очках и прибавка к привлекательности (у класса или у всех).</summary>
    public double TripCost { get; init; } = 2;
    public double TripBonus { get; init; } = 0.15;
    public double MediaCost { get; init; } = 3;
    public double MediaBonus { get; init; } = 0.05;
    /// <summary>Обещание закона: цена и сила (× изменение отношения класса к закону).</summary>
    public double PromiseCost { get; init; } = 1;
    public double PromiseWeight { get; init; } = 0.05;
    /// <summary>Прибавки кампании за месяц уменьшаются в CampaignDecay раз; одна прибавка не больше CampaignCap.</summary>
    public double CampaignDecay { get; init; } = 0.8;
    public double CampaignCap { get; init; } = 0.8;
    /// <summary>Кампании остальных партий: прибавка AiCampaign × организованность у всех классов.</summary>
    public double AiCampaign { get; init; } = 0.15;
    /// <summary>Дебаты: сила позиции (× взгляд класса по оси / 100), обаяние лидера и случайность выступления.</summary>
    public double DebateWeight { get; init; } = 0.3;
    public double DebateCharisma { get; init; } = 0.1;
    public double DebateNoise { get; init; } = 0.06;
    /// <summary>Выполненное и невыполненное обещание кампании: дрейф партии и популярность лидера.</summary>
    public double KeptPromise { get; init; } = 0.1;
    public double BrokenPromise { get; init; } = 0.3;
    public double BrokenPopularity { get; init; } = 8;
    /// <summary>Фальсификации по уровням (нет, вбросы, массовые): прибавка к доле партии и базовый риск разоблачения.</summary>
    public IReadOnlyList<double> FraudShare { get; init; } = [0, 0.03, 0.08];
    public IReadOnlyList<double> FraudRisk { get; init; } = [0, 0.3, 0.6];
    /// <summary>Разоблачение: скандал (минус к привлекательности правящих), как быстро он затихает, его вклад в радикализм и легитимность.</summary>
    public double Scandal { get; init; } = 0.6;
    public double ScandalDecay { get; init; } = 0.9;
    public double ScandalRadicalism { get; init; } = 12;
    public double ScandalLegitimacy { get; init; } = 20;
    public double ScandalPopularity { get; init; } = 15;
}
