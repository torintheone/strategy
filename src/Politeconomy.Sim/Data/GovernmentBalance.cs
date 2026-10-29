namespace Politeconomy.Sim.Data;

// Коэффициенты правительства и парламента из data/balance.json (раздел government). Смысл — в MODEL.md, «Правительство».
// Списки из четырёх чисел — по группам дохода: низкий, ниже среднего, выше среднего, высокий.

public sealed class GovernmentBalance
{
    /// <summary>Доля женщин среди министров и среди глав исполнительной власти.</summary>
    public IReadOnlyList<double> FemaleShare { get; init; } = [0.15, 0.2, 0.25, 0.33];
    public double FemaleLeaderShare { get; init; } = 0.12;

    /// <summary>Качества персонажа 0–100: среднее и разброс.</summary>
    public IReadOnlyList<double> CompetenceMean { get; init; } = [45, 50, 55, 60];
    public double CompetenceSpread { get; init; } = 15;
    public double LoyaltyMean { get; init; } = 65;
    public double LoyaltySpread { get; init; } = 15;
    public IReadOnlyList<double> CorruptionMean { get; init; } = [45, 40, 32, 20];
    public double CorruptionSpread { get; init; } = 15;
    public double PopularityMean { get; init; } = 45;
    public double PopularitySpread { get; init; } = 12;
    /// <summary>Разброс взглядов по трём осям (−100…100).</summary>
    public double ViewsSpread { get; init; } = 35;

    /// <summary>Возраст при назначении: от и до.</summary>
    public IReadOnlyList<int> LeaderAge { get; init; } = [45, 75];
    public IReadOnlyList<int> MinisterAge { get; init; } = [35, 70];

    /// <summary>Вероятность первой и второй черты; силовые посты чаще получают силовиков, экономические — технократов.</summary>
    public double FirstTrait { get; init; } = 0.8;
    public double SecondTrait { get; init; } = 0.3;
    public double SecurityTraitBoost { get; init; } = 4;
    public double FinanceTraitBoost { get; init; } = 3;

    /// <summary>Отставки: старше RetirementAge — RetirementRate в месяц за каждый год сверх; ещё ResignationRate в месяц у всех.</summary>
    public int RetirementAge { get; init; } = 72;
    public double RetirementRate { get; init; } = 0.003;
    public double ResignationRate { get; init; } = 0.0015;
    /// <summary>Сколько кандидатов предлагается на пост.</summary>
    public int Candidates { get; init; } = 3;
    /// <summary>Новый министр благодарен за назначение: прибавка к лояльности.</summary>
    public double AppointmentLoyalty { get; init; } = 10;

    public ParliamentBalance Parliament { get; init; } = new();
}

public sealed class ParliamentBalance
{
    /// <summary>Доля мест правящего блока (от и до) и партийная дисциплина по режимам.</summary>
    public IReadOnlyList<double> DemocracyRuling { get; init; } = [0.51, 0.6];
    public IReadOnlyList<double> PresidentialRuling { get; init; } = [0.38, 0.62];
    public IReadOnlyList<double> HybridRuling { get; init; } = [0.6, 0.8];
    public IReadOnlyList<double> AuthoritarianRuling { get; init; } = [0.85, 0.98];
    public double DemocracyDiscipline { get; init; } = 0.82;
    public double HybridDiscipline { get; init; } = 0.92;
    public double AuthoritarianDiscipline { get; init; } = 0.99;
    /// <summary>Мест в парламенте без данных: кубический корень из населения, в пределах SeatsMin…SeatsMax.</summary>
    public int SeatsMin { get; init; } = 15;
    public int SeatsMax { get; init; } = 750;
    /// <summary>Оппозиция поддерживает закон, если его одобрение в обществе выше этого порога (0–1).</summary>
    public double OppositionThreshold { get; init; } = 0.62;
    /// <summary>Доля воздержавшихся и случайный разброс голосов.</summary>
    public double Abstain { get; init; } = 0.04;
    public double Noise { get; init; } = 0.03;
    /// <summary>Конституционный закон требует такой доли всех мест.</summary>
    public double Constitutional { get; init; } = 2.0 / 3;
}
